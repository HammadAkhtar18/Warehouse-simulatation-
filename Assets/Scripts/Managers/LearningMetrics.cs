using System.Collections.Generic;
using UnityEngine;
using WarehouseSimulation.Robots;

namespace WarehouseSimulation.Managers
{
    /// <summary>
    /// Tracks learning-focused performance snapshots and logs trend values that can be
    /// plotted as an improvement graph by external tooling/UI.
    /// </summary>
    public class LearningMetrics : MonoBehaviour
    {
        [Header("Episode Tracking")]
        [SerializeField, Min(5f)] private float episodeDurationSeconds = 60f;
        [SerializeField] private bool logEpisodeSummaries = true;

        private readonly List<float> completionTimeHistory = new();
        private readonly List<int> collisionHistory = new();
        private readonly List<float> pathEfficiencyHistory = new();
        private readonly List<float> throughputPerHourHistory = new();

        private readonly Dictionary<RobotAgent, int> robotCollisionBaseline = new();

        private int currentEpisode = 1;
        private float episodeTimer;

        private float episodeCompletionTimeTotal;
        private int episodeCompletionCount;
        private float episodeActualDistanceTotal;
        private float episodeOptimalDistanceTotal;

        public void RecordTaskCompletion(float completionTimeSeconds, float actualDistance, float optimalDistance)
        {
            episodeCompletionTimeTotal += Mathf.Max(0f, completionTimeSeconds);
            episodeCompletionCount++;
            episodeActualDistanceTotal += Mathf.Max(0f, actualDistance);
            episodeOptimalDistanceTotal += Mathf.Max(0.0001f, optimalDistance);
        }

        public void Tick(float deltaTime, IReadOnlyList<RobotAgent> robots)
        {
            episodeTimer += Mathf.Max(0f, deltaTime);

            if (robots != null)
            {
                CacheRobotBaselinesIfMissing(robots);
            }

            if (episodeTimer < episodeDurationSeconds)
            {
                return;
            }

            FinalizeEpisode(robots);
            ResetEpisodeBaselines(robots);
        }

        private void CacheRobotBaselinesIfMissing(IReadOnlyList<RobotAgent> robots)
        {
            for (int i = 0; i < robots.Count; i++)
            {
                RobotAgent robot = robots[i];
                if (robot == null)
                {
                    continue;
                }


                if (!robotCollisionBaseline.ContainsKey(robot))
                {
                    robotCollisionBaseline[robot] = robot.EpisodeCollisionCount;
                }
            }
        }

        private void FinalizeEpisode(IReadOnlyList<RobotAgent> robots)
        {
            float avgCompletionTime = episodeCompletionCount > 0
                ? episodeCompletionTimeTotal / episodeCompletionCount
                : 0f;

            int collisions = 0;
            if (robots != null)
            {
                for (int i = 0; i < robots.Count; i++)
                {
                    RobotAgent robot = robots[i];
                    if (robot == null)
                    {
                        continue;
                    }

                    int baseline = robotCollisionBaseline.TryGetValue(robot, out int priorCollisions) ? priorCollisions : 0;
                    collisions += Mathf.Max(0, robot.EpisodeCollisionCount - baseline);
                }
            }

            float pathEfficiency = episodeOptimalDistanceTotal > 0f
                ? episodeActualDistanceTotal / episodeOptimalDistanceTotal
                : 1f;

            float elapsedHours = Mathf.Max(episodeDurationSeconds / 3600f, 0.0001f);
            float throughputPerHour = episodeCompletionCount / elapsedHours;

            completionTimeHistory.Add(avgCompletionTime);
            collisionHistory.Add(collisions);
            pathEfficiencyHistory.Add(pathEfficiency);
            throughputPerHourHistory.Add(throughputPerHour);

            if (logEpisodeSummaries)
            {
                Debug.Log(
                    $"[LearningMetrics] Episode {currentEpisode} | AvgCompletionTime={avgCompletionTime:F2}s | " +
                    $"Collisions={collisions} | PathEfficiency={pathEfficiency:F3} | ThroughputPerHour={throughputPerHour:F2}");
            }

            currentEpisode++;
            episodeTimer = 0f;
            episodeCompletionTimeTotal = 0f;
            episodeCompletionCount = 0;
            episodeActualDistanceTotal = 0f;
            episodeOptimalDistanceTotal = 0f;
        }

        private void ResetEpisodeBaselines(IReadOnlyList<RobotAgent> robots)
        {
            robotCollisionBaseline.Clear();
            CacheRobotBaselinesIfMissing(robots);
        }
    }
}
