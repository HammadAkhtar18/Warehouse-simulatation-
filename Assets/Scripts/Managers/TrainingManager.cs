using System.Collections.Generic;
using UnityEngine;
using WarehouseSimulation.Robots;

namespace WarehouseSimulation.Managers
{
    /// <summary>
    /// Coordinates ML-Agents training runs, progress logging, and operator controls.
    /// </summary>
    public class TrainingManager : MonoBehaviour
    {
        [Header("Training State")]
        [SerializeField] private bool trainingActive;
        [SerializeField, Min(1)] private int maxTrainingSteps = 500000;
        [SerializeField, Min(1000)] private int checkpointStepInterval = 50000;

        [Header("Runtime Controls")]
        [SerializeField, Min(0.1f)] private float trainingSpeedMultiplier = 1f;
        [SerializeField] private bool showEditorOverlay = true;
        [SerializeField] private bool verboseLogging = true;

        private readonly Dictionary<RobotAgent, int> knownEpisodeCounts = new();

        private RobotAgent[] agents = System.Array.Empty<RobotAgent>();
        private int trainingEpisodes;
        private int accumulatedSteps;
        private int nextCheckpointStep;
        private float cumulativeEpisodeReward;
        private int loggedEpisodeCount;

        public bool TrainingActive => trainingActive;
        public float TrainingSpeedMultiplier => trainingSpeedMultiplier;

        private void Awake()
        {
            CacheAgents();
            nextCheckpointStep = checkpointStepInterval;
        }

        private void Update()
        {
            if (!trainingActive)
            {
                return;
            }

            if (agents.Length == 0)
            {
                CacheAgents();
                if (agents.Length == 0)
                {
                    return;
                }
            }

            accumulatedSteps = 0;
            foreach (RobotAgent agent in agents)
            {
                if (agent == null)
                {
                    continue;
                }

                accumulatedSteps += agent.StepCount;
                TrackEpisodeForAgent(agent);
            }

            if (accumulatedSteps >= nextCheckpointStep)
            {
                SaveCheckpoint(nextCheckpointStep);
                nextCheckpointStep += checkpointStepInterval;
            }

            if (accumulatedSteps >= maxTrainingSteps)
            {
                StopTraining();
            }
        }

        public void StartTraining()
        {
            CacheAgents();
            trainingActive = true;
            Time.timeScale = trainingSpeedMultiplier;
            if (verboseLogging)
            {
                Debug.Log("[TrainingManager] Training started.");
            }
        }

        public void StopTraining()
        {
            trainingActive = false;
            Time.timeScale = 1f;
            if (verboseLogging)
            {
                Debug.Log("[TrainingManager] Training stopped.");
            }
        }

        public void ResetEnvironment()
        {
            CacheAgents();
            foreach (RobotAgent agent in agents)
            {
                if (agent == null)
                {
                    continue;
                }

                agent.EndEpisode();
            }

            if (verboseLogging)
            {
                Debug.Log("[TrainingManager] Environment reset.");
            }
        }

        public void SetTrainingSpeedMultiplier(float multiplier)
        {
            trainingSpeedMultiplier = Mathf.Max(0.1f, multiplier);
            if (trainingActive)
            {
                Time.timeScale = trainingSpeedMultiplier;
            }
        }

        private void CacheAgents()
        {
            agents = FindObjectsOfType<RobotAgent>();
            knownEpisodeCounts.Clear();
            foreach (RobotAgent agent in agents)
            {
                if (agent != null)
                {
                    knownEpisodeCounts[agent] = agent.CompletedEpisodes;
                }
            }
        }

        private void TrackEpisodeForAgent(RobotAgent agent)
        {
            int knownEpisodes = knownEpisodeCounts.TryGetValue(agent, out int cached) ? cached : 0;
            if (agent.CompletedEpisodes <= knownEpisodes)
            {
                return;
            }

            float reward = agent.GetCumulativeReward();
            trainingEpisodes += agent.CompletedEpisodes - knownEpisodes;
            cumulativeEpisodeReward += reward;
            loggedEpisodeCount++;
            knownEpisodeCounts[agent] = agent.CompletedEpisodes;

            Debug.Log($"[TrainingManager] Episode {trainingEpisodes} reward: {reward:F2}");
        }

        private void SaveCheckpoint(int step)
        {
            Debug.Log($"[TrainingManager] Checkpoint reached at step {step}. Save model with: mlagents-learn --resume");
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!showEditorOverlay)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(20f, 20f, 420f, 160f), "Training Progress", GUI.skin.window);
            GUILayout.Label($"Active: {trainingActive}");
            GUILayout.Label($"Episodes: {trainingEpisodes}");
            GUILayout.Label($"Steps: {accumulatedSteps}/{maxTrainingSteps}");
            float avgReward = loggedEpisodeCount > 0 ? cumulativeEpisodeReward / loggedEpisodeCount : 0f;
            GUILayout.Label($"Average episode reward: {avgReward:F2}");
            GUILayout.EndArea();
        }
#endif
    }
}
