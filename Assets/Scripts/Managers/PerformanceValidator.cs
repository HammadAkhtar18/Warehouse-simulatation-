using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using WarehouseSimulation.Robots;

namespace WarehouseSimulation.Managers
{
    [DisallowMultipleComponent]
    public class PerformanceValidator : MonoBehaviour
    {
        [Serializable]
        public class EpisodeMetrics
        {
            public int EpisodeIndex;
            public float AverageTaskCompletionTime;
            public int TotalCollisions;
            public float PathEfficiencyRatio;
            public float TasksCompletedPerHour;
            public float AverageIdleTimePercentage;
            public float CumulativeReward;
            public float EpisodeLengthSeconds;
            public float SuccessRate;
            public float CollisionRate;
            public string TimestampUtc;
        }

        [Serializable]
        public class MetricSummary
        {
            public string MetricName;
            public float Mean;
            public float StandardDeviation;
            public float ConfidenceScore;
            public List<int> OutlierEpisodes = new();
        }

        [Serializable]
        public class TrainingMetrics
        {
            public List<float> CumulativeRewardPerEpisode = new();
            public List<float> EpisodeLengthHistory = new();
            public List<float> SuccessRateHistory = new();
            public List<float> CollisionRateTrend = new();
            public int EpisodesToConvergence;
        }

        [Serializable]
        public class ComparisonMetric
        {
            public string MetricName;
            public float BaselineMean;
            public float TrainedMean;
            public float ImprovementPercent;
        }

        [Serializable]
        public class ValidationDataset
        {
            public string Label;
            public string GeneratedAtUtc;
            public string BuildVersion;
            public List<EpisodeMetrics> Episodes = new();
            public List<MetricSummary> Statistics = new();
        }

        [Serializable]
        public class ComparisonReport
        {
            public string GeneratedAtUtc;
            public string BuildVersion;
            public List<ComparisonMetric> Metrics = new();
            public List<MetricSummary> BaselineStatistics = new();
            public List<MetricSummary> TrainedStatistics = new();
        }

        [Header("Validation Setup")]
        [SerializeField, Min(1)] private int validationEpisodes = 100;
        [SerializeField, Min(1f)] private float episodeDurationSeconds = 120f;
        [SerializeField, Min(0.1f)] private float idleSamplingInterval = 0.25f;
        [SerializeField] private string exportFolderName = "ValidationReports";
        [SerializeField] private string simulationVersion = "phase-10";

        [Header("Environment Hooks")]
        [SerializeField] private RobotCoordinator robotCoordinator;
        [SerializeField] private UnityEvent onConfigureUntrainedAgents;
        [SerializeField] private UnityEvent onConfigureTrainedAgents;
        [SerializeField] private UnityEvent onResetEpisode;

        public IReadOnlyList<EpisodeMetrics> BaselineEpisodes => baselineData.Episodes;
        public IReadOnlyList<EpisodeMetrics> TrainedEpisodes => trainedData.Episodes;
        public TrainingMetrics CurrentTrainingMetrics => trainingMetrics;
        public ComparisonReport LastComparisonReport => comparison;

        public event Action<IReadOnlyList<float>> RewardHistoryUpdated;
        public event Action<IReadOnlyList<float>> CollisionTrendUpdated;

        private readonly TrainingMetrics trainingMetrics = new();
        private readonly ValidationDataset baselineData = new();
        private readonly ValidationDataset trainedData = new();
        private readonly ComparisonReport comparison = new();

        private bool isRunningValidation;

        private int episodeTasksAssigned;
        private int episodeTasksCompleted;
        private float episodeOptimalDistance;
        private float episodeActualDistance;

        private void Awake()
        {
            if (robotCoordinator == null)
            {
                robotCoordinator = FindObjectOfType<RobotCoordinator>();
            }

            baselineData.Label = "Baseline";
            trainedData.Label = "Trained";
        }

        public void ReportTaskAssigned() => episodeTasksAssigned++;

        public void ReportTaskCompleted(float actualDistance, float optimalDistance)
        {
            episodeTasksCompleted++;
            episodeActualDistance += Mathf.Max(0f, actualDistance);
            episodeOptimalDistance += Mathf.Max(0.0001f, optimalDistance);
        }

        public void RecordTrainingEpisode(float cumulativeReward, float episodeLengthSeconds, int tasksAssigned, int tasksCompleted, int collisions)
        {
            trainingMetrics.CumulativeRewardPerEpisode.Add(cumulativeReward);
            trainingMetrics.EpisodeLengthHistory.Add(Mathf.Max(0f, episodeLengthSeconds));

            float successRate = tasksAssigned > 0 ? Mathf.Clamp01((float)tasksCompleted / tasksAssigned) : 0f;
            trainingMetrics.SuccessRateHistory.Add(successRate);

            float collisionRate = episodeLengthSeconds > 0f ? collisions / episodeLengthSeconds : 0f;
            trainingMetrics.CollisionRateTrend.Add(collisionRate);

            trainingMetrics.EpisodesToConvergence = EstimateConvergenceEpisode(trainingMetrics.CumulativeRewardPerEpisode);
            RewardHistoryUpdated?.Invoke(trainingMetrics.CumulativeRewardPerEpisode);
            CollisionTrendUpdated?.Invoke(trainingMetrics.CollisionRateTrend);
        }

        public void RunBaselineTest() => StartCoroutine(RunValidationRoutine(false));

        public void RunTrainedTest() => StartCoroutine(RunValidationRoutine(true));

        public void CompareResults()
        {
            comparison.GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            comparison.BuildVersion = ResolveVersion();
            comparison.Metrics = BuildComparisonMetrics();
            comparison.BaselineStatistics = baselineData.Statistics;
            comparison.TrainedStatistics = trainedData.Statistics;
        }

        public string ExportAllReports()
        {
            string folder = Path.Combine(Application.persistentDataPath, exportFolderName);
            Directory.CreateDirectory(folder);

            SaveJson(Path.Combine(folder, "baseline_metrics.json"), baselineData);
            SaveJson(Path.Combine(folder, "trained_metrics.json"), trainedData);
            SaveJson(Path.Combine(folder, "comparison_report.json"), comparison);
            ExportTrainingCsv(Path.Combine(folder, "training_metrics.csv"));
            ExportTextSummary(Path.Combine(folder, "performance_summary.txt"));
            return folder;
        }

        private IEnumerator RunValidationRoutine(bool trainedAgents)
        {
            if (isRunningValidation)
            {
                yield break;
            }

            isRunningValidation = true;
            ValidationDataset targetData = trainedAgents ? trainedData : baselineData;
            targetData.Episodes.Clear();
            targetData.Statistics.Clear();

            if (trainedAgents)
            {
                onConfigureTrainedAgents?.Invoke();
            }
            else
            {
                onConfigureUntrainedAgents?.Invoke();
            }

            for (int i = 0; i < validationEpisodes; i++)
            {
                onResetEpisode?.Invoke();
                ResetEpisodeCounters();
                yield return CollectEpisode(i + 1, targetData.Episodes);
            }

            targetData.GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            targetData.BuildVersion = ResolveVersion();
            targetData.Statistics = BuildStatistics(targetData.Episodes);

            string fileName = trainedAgents ? "trained_metrics.json" : "baseline_metrics.json";
            SaveJson(Path.Combine(Application.persistentDataPath, exportFolderName, fileName), targetData);
            isRunningValidation = false;
        }

        private IEnumerator CollectEpisode(int episodeIndex, List<EpisodeMetrics> sink)
        {
            if (robotCoordinator == null)
            {
                yield break;
            }

            IReadOnlyList<RobotAgent> robots = robotCoordinator.ActiveRobots;
            if (robots == null || robots.Count == 0)
            {
                yield break;
            }

            var preDistances = new Dictionary<RobotAgent, float>();
            var preCollisions = new Dictionary<RobotAgent, int>();
            var preCompletedTasks = new Dictionary<RobotAgent, int>();
            float idleSamples = 0f;
            float totalSamples = 0f;

            foreach (RobotAgent robot in robots)
            {
                if (robot == null)
                {
                    continue;
                }

                preDistances[robot] = robot.EpisodeDistanceTraveled;
                preCollisions[robot] = robot.EpisodeCollisionCount;
                preCompletedTasks[robot] = robot.CompletedTaskCount;
            }

            float elapsed = 0f;
            float idleTimer = 0f;
            while (elapsed < episodeDurationSeconds)
            {
                elapsed += Time.deltaTime;
                idleTimer += Time.deltaTime;

                if (idleTimer >= idleSamplingInterval)
                {
                    idleTimer = 0f;
                    for (int i = 0; i < robots.Count; i++)
                    {
                        RobotAgent robot = robots[i];
                        if (robot == null)
                        {
                            continue;
                        }

                        totalSamples++;
                        if (!robot.IsBusy)
                        {
                            idleSamples++;
                        }
                    }
                }

                yield return null;
            }

            float completionTimeSum = 0f;
            float pathRatioSum = 0f;
            int pathSamples = 0;
            int collisionCount = 0;
            int completedTasksDelta = 0;

            foreach (RobotAgent robot in robots)
            {
                if (robot == null)
                {
                    continue;
                }

                completionTimeSum += robot.AverageTaskCompletionTime;

                int preCollision = preCollisions.TryGetValue(robot, out int preC) ? preC : 0;
                collisionCount += Mathf.Max(0, robot.EpisodeCollisionCount - preCollision);

                int preTasks = preCompletedTasks.TryGetValue(robot, out int preT) ? preT : 0;
                completedTasksDelta += Mathf.Max(0, robot.CompletedTaskCount - preTasks);

                if (robot.AverageTaskEfficiency > 0f)
                {
                    pathRatioSum += robot.AverageTaskEfficiency;
                    pathSamples++;
                }
            }

            float avgCompletionTime = robots.Count > 0 ? completionTimeSum / robots.Count : 0f;
            float durationHours = Mathf.Max(episodeDurationSeconds / 3600f, 0.0001f);
            float completedPerHour = completedTasksDelta / durationHours;
            float idlePercent = totalSamples > 0f ? (idleSamples / totalSamples) * 100f : 0f;

            float pathRatio = pathSamples > 0 ? pathRatioSum / pathSamples : 1f;
            if (episodeOptimalDistance > 0f)
            {
                pathRatio = episodeActualDistance / episodeOptimalDistance;
            }

            sink.Add(new EpisodeMetrics
            {
                EpisodeIndex = episodeIndex,
                AverageTaskCompletionTime = avgCompletionTime,
                TotalCollisions = collisionCount,
                PathEfficiencyRatio = pathRatio,
                TasksCompletedPerHour = completedPerHour,
                AverageIdleTimePercentage = idlePercent,
                CumulativeReward = 0f,
                EpisodeLengthSeconds = episodeDurationSeconds,
                SuccessRate = episodeTasksAssigned > 0 ? (float)episodeTasksCompleted / episodeTasksAssigned : 0f,
                CollisionRate = collisionCount / Mathf.Max(episodeDurationSeconds, 1f),
                TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            });
        }

        private void ResetEpisodeCounters()
        {
            episodeTasksAssigned = 0;
            episodeTasksCompleted = 0;
            episodeOptimalDistance = 0f;
            episodeActualDistance = 0f;
        }

        private List<MetricSummary> BuildStatistics(List<EpisodeMetrics> episodes)
        {
            var stats = new List<MetricSummary>
            {
                BuildSummary("Average Task Completion Time", episodes, e => e.AverageTaskCompletionTime),
                BuildSummary("Total Collisions", episodes, e => e.TotalCollisions),
                BuildSummary("Path Efficiency Ratio", episodes, e => e.PathEfficiencyRatio),
                BuildSummary("Tasks Completed Per Hour", episodes, e => e.TasksCompletedPerHour),
                BuildSummary("Average Idle Time Percentage", episodes, e => e.AverageIdleTimePercentage)
            };

            return stats;
        }

        private static MetricSummary BuildSummary(string name, List<EpisodeMetrics> episodes, Func<EpisodeMetrics, float> selector)
        {
            var summary = new MetricSummary { MetricName = name };
            if (episodes.Count == 0)
            {
                return summary;
            }

            float mean = 0f;
            for (int i = 0; i < episodes.Count; i++)
            {
                mean += selector(episodes[i]);
            }

            mean /= episodes.Count;
            float variance = 0f;
            for (int i = 0; i < episodes.Count; i++)
            {
                float delta = selector(episodes[i]) - mean;
                variance += delta * delta;
            }

            float stdDev = Mathf.Sqrt(variance / episodes.Count);
            summary.Mean = mean;
            summary.StandardDeviation = stdDev;

            float ci95 = (1.96f * stdDev) / Mathf.Sqrt(Mathf.Max(1, episodes.Count));
            summary.ConfidenceScore = mean != 0f
                ? Mathf.Clamp01(1f - Mathf.Abs(ci95 / mean))
                : 0f;

            if (stdDev > 0f)
            {
                for (int i = 0; i < episodes.Count; i++)
                {
                    float z = Mathf.Abs((selector(episodes[i]) - mean) / stdDev);
                    if (z >= 2f)
                    {
                        summary.OutlierEpisodes.Add(episodes[i].EpisodeIndex);
                    }
                }
            }

            return summary;
        }

        private List<ComparisonMetric> BuildComparisonMetrics()
        {
            var metrics = new List<ComparisonMetric>();
            AddComparison(metrics, "Average Task Completion Time", baselineData.Episodes, trainedData.Episodes, e => e.AverageTaskCompletionTime, invertImprovement: true);
            AddComparison(metrics, "Total Collisions", baselineData.Episodes, trainedData.Episodes, e => e.TotalCollisions, invertImprovement: true);
            AddComparison(metrics, "Path Efficiency Ratio", baselineData.Episodes, trainedData.Episodes, e => e.PathEfficiencyRatio, invertImprovement: true);
            AddComparison(metrics, "Tasks Completed Per Hour", baselineData.Episodes, trainedData.Episodes, e => e.TasksCompletedPerHour, invertImprovement: false);
            AddComparison(metrics, "Average Idle Time Percentage", baselineData.Episodes, trainedData.Episodes, e => e.AverageIdleTimePercentage, invertImprovement: true);
            return metrics;
        }

        private static void AddComparison(List<ComparisonMetric> sink, string name, List<EpisodeMetrics> baseline, List<EpisodeMetrics> trained, Func<EpisodeMetrics, float> selector, bool invertImprovement)
        {
            float baselineMean = ComputeMean(baseline, selector);
            float trainedMean = ComputeMean(trained, selector);

            float improvement = 0f;
            if (Mathf.Abs(baselineMean) > 0.0001f)
            {
                improvement = ((trainedMean - baselineMean) / baselineMean) * 100f;
            }

            if (invertImprovement)
            {
                improvement *= -1f;
            }

            sink.Add(new ComparisonMetric
            {
                MetricName = name,
                BaselineMean = baselineMean,
                TrainedMean = trainedMean,
                ImprovementPercent = improvement
            });
        }

        private static float ComputeMean(List<EpisodeMetrics> episodes, Func<EpisodeMetrics, float> selector)
        {
            if (episodes.Count == 0)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < episodes.Count; i++)
            {
                sum += selector(episodes[i]);
            }

            return sum / episodes.Count;
        }

        private int EstimateConvergenceEpisode(List<float> rewards)
        {
            const int window = 15;
            const float threshold = 0.03f;

            if (rewards.Count < window * 2)
            {
                return 0;
            }

            for (int i = window * 2; i < rewards.Count; i++)
            {
                float prev = AverageWindow(rewards, i - window * 2, window);
                float current = AverageWindow(rewards, i - window, window);
                float delta = Mathf.Abs(current - prev);
                if (Mathf.Abs(prev) > 0.0001f && delta / Mathf.Abs(prev) < threshold)
                {
                    return i;
                }
            }

            return 0;
        }

        private static float AverageWindow(List<float> values, int start, int count)
        {
            float sum = 0f;
            int safeEnd = Mathf.Min(values.Count, start + count);
            for (int i = start; i < safeEnd; i++)
            {
                sum += values[i];
            }

            return count > 0 ? sum / count : 0f;
        }

        private void SaveJson<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.persistentDataPath);
            string json = JsonUtility.ToJson(value, true);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        private void ExportTrainingCsv(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("episode,cumulative_reward,episode_length,success_rate,collision_rate,timestamp_utc,version");
            int count = trainingMetrics.CumulativeRewardPerEpisode.Count;
            for (int i = 0; i < count; i++)
            {
                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1:F4},{2:F4},{3:F4},{4:F4},{5},{6}",
                    i + 1,
                    SafeListValue(trainingMetrics.CumulativeRewardPerEpisode, i),
                    SafeListValue(trainingMetrics.EpisodeLengthHistory, i),
                    SafeListValue(trainingMetrics.SuccessRateHistory, i),
                    SafeListValue(trainingMetrics.CollisionRateTrend, i),
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ResolveVersion());

                sb.AppendLine(line);
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private void ExportTextSummary(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Warehouse Performance Summary");
            sb.AppendLine($"Generated: {DateTime.UtcNow:o}");
            sb.AppendLine($"Version: {ResolveVersion()}");
            sb.AppendLine();

            sb.AppendLine("== Baseline Statistics ==");
            AppendStatLines(sb, baselineData.Statistics);
            sb.AppendLine();

            sb.AppendLine("== Trained Statistics ==");
            AppendStatLines(sb, trainedData.Statistics);
            sb.AppendLine();

            sb.AppendLine("== Comparison ==");
            for (int i = 0; i < comparison.Metrics.Count; i++)
            {
                ComparisonMetric metric = comparison.Metrics[i];
                sb.AppendLine($"{metric.MetricName}: baseline={metric.BaselineMean:F3}, trained={metric.TrainedMean:F3}, improvement={metric.ImprovementPercent:F2}%");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void AppendStatLines(StringBuilder sb, List<MetricSummary> stats)
        {
            for (int i = 0; i < stats.Count; i++)
            {
                MetricSummary stat = stats[i];
                sb.AppendLine($"{stat.MetricName}: mean={stat.Mean:F3}, std={stat.StandardDeviation:F3}, confidence={stat.ConfidenceScore:P1}, outliers={stat.OutlierEpisodes.Count}");
            }
        }

        private static float SafeListValue(List<float> values, int index)
        {
            return index >= 0 && index < values.Count ? values[index] : 0f;
        }

        private string ResolveVersion()
        {
            return string.IsNullOrWhiteSpace(simulationVersion)
                ? Application.version
                : $"{simulationVersion}|unity:{Application.version}";
        }
    }
}
