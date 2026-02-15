using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WarehouseSimulation.Managers;
using WarehouseSimulation.Robots;

namespace WarehouseSimulation.UI
{
    /// <summary>
    /// Runtime reward diagnostics panel for ML-agent behavior tuning.
    /// </summary>
    public class RewardDebugger : MonoBehaviour
    {
        [SerializeField] private bool showOverlay = true;
        [SerializeField, Min(1f)] private float trendLogIntervalSeconds = 5f;
        [SerializeField, Min(10)] private int maxTrendHistory = 120;

        private readonly Queue<string> trendHistory = new();

        private RewardTuner tuner;
        private Vector2 scroll;
        private float trendTimer;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<RewardDebugger>() != null)
            {
                return;
            }

            var debuggerObject = new GameObject("RewardDebugger");
            debuggerObject.AddComponent<RewardDebugger>();
        }

        private void Awake()
        {
            tuner = FindObjectOfType<RewardTuner>();
        }

        private void Update()
        {
            trendTimer += Time.deltaTime;
            if (trendTimer < trendLogIntervalSeconds)
            {
                return;
            }

            trendTimer = 0f;
            LogRewardTrends();
        }

        private void LogRewardTrends()
        {
            RobotAgent[] agents = FindObjectsOfType<RobotAgent>();
            if (agents.Length == 0)
            {
                return;
            }

            float cumulative = 0f;
            foreach (RobotAgent agent in agents)
            {
                cumulative += agent.GetCumulativeReward();
            }

            float average = cumulative / agents.Length;
            string trendLine = $"t={Time.time:0.0}s | avg reward={average:0.00} | robots={agents.Length}";
            trendHistory.Enqueue(trendLine);
            if (trendHistory.Count > maxTrendHistory)
            {
                trendHistory.Dequeue();
            }

            Debug.Log($"[RewardDebugger] {trendLine}");
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            RobotAgent[] agents = FindObjectsOfType<RobotAgent>();
            GUILayout.BeginArea(new Rect(20f, 200f, 560f, 540f), "Reward Debugger", GUI.skin.window);

            if (tuner != null)
            {
                GUILayout.Label($"Preset: {tuner.ActivePreset}");
                GUILayout.Label("Auto-balance suggestions:");
                foreach (string suggestion in tuner.BalanceSuggestions)
                {
                    GUILayout.Label($"• {suggestion}");
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label($"Active robots: {agents.Length}");

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(290f));
            foreach (RobotAgent agent in agents)
            {
                DrawRobotBreakdown(agent);
                GUILayout.Space(8f);
            }
            GUILayout.EndScrollView();

            GUILayout.Label("Recent reward trends:");
            foreach (string line in trendHistory)
            {
                GUILayout.Label(line);
            }

            GUILayout.EndArea();
        }
#endif

        private void DrawRobotBreakdown(RobotAgent agent)
        {
            var builder = new StringBuilder();
            builder.Append($"{agent.name} | reward {agent.GetCumulativeReward():0.00} | trend {agent.RewardTrendPerSecond:0.00}/s");

            bool problematic = agent.RewardTrendPerSecond < -0.2f || agent.EpisodeCollisionCount > 4;
            if (problematic)
            {
                builder.Append("  [PROBLEM]");
            }

            GUILayout.Label(builder.ToString());

            Dictionary<string, float> breakdown = agent.GetRewardBreakdownSnapshot();
            foreach (var pair in breakdown)
            {
                GUILayout.Label($"   {pair.Key}: {pair.Value:0.00}");
            }

            if (problematic)
            {
                if (agent.EpisodeCollisionCount > 4)
                {
                    GUILayout.Label("   -> High collisions detected. Increase safety penalties or coordination rewards.");
                }

                if (agent.RewardTrendPerSecond < -0.2f)
                {
                    GUILayout.Label("   -> Reward trend is negative. Relax penalties or increase task/movement rewards.");
                }
            }
        }
    }
}
