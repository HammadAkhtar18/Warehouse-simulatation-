using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WarehouseSimulation.Managers;

namespace WarehouseSimulation.UI
{
    public class LearningGraphs : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PerformanceValidator performanceValidator;

        [Header("Graph Containers")]
        [SerializeField] private RectTransform rewardGraphArea;
        [SerializeField] private RectTransform collisionGraphArea;
        [SerializeField] private RectTransform completionGraphArea;
        [SerializeField] private RectTransform comparisonBarArea;

        [Header("Styles")]
        [SerializeField] private Color rewardColor = new(0.15f, 0.8f, 0.2f, 1f);
        [SerializeField] private Color collisionColor = new(0.9f, 0.2f, 0.2f, 1f);
        [SerializeField] private Color completionColor = new(0.25f, 0.55f, 0.95f, 1f);
        [SerializeField] private Color baselineBarColor = new(0.85f, 0.55f, 0.15f, 0.9f);
        [SerializeField] private Color trainedBarColor = new(0.2f, 0.8f, 0.4f, 0.9f);
        [SerializeField, Min(1f)] private float lineThickness = 3f;
        [SerializeField] private Font labelFont;

        private readonly List<GameObject> spawned = new();

        private void Awake()
        {
            if (performanceValidator == null)
            {
                performanceValidator = FindObjectOfType<PerformanceValidator>();
            }
        }

        private void OnEnable()
        {
            if (performanceValidator == null)
            {
                return;
            }

            performanceValidator.RewardHistoryUpdated += HandleRewardUpdated;
            performanceValidator.CollisionTrendUpdated += HandleCollisionUpdated;
        }

        private void OnDisable()
        {
            if (performanceValidator == null)
            {
                return;
            }

            performanceValidator.RewardHistoryUpdated -= HandleRewardUpdated;
            performanceValidator.CollisionTrendUpdated -= HandleCollisionUpdated;
        }

        public void RefreshComparisonBars()
        {
            if (performanceValidator == null)
            {
                return;
            }

            var report = performanceValidator.LastComparisonReport;
            if (report == null)
            {
                return;
            }

            ClearContainer(comparisonBarArea);
            float width = comparisonBarArea.rect.width;
            float height = comparisonBarArea.rect.height;
            int count = report.Metrics.Count;
            if (count == 0 || width <= 1f || height <= 1f)
            {
                return;
            }

            float slotWidth = width / Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                var metric = report.Metrics[i];
                float max = Mathf.Max(0.001f, metric.BaselineMean, metric.TrainedMean);
                float baselineHeight = (metric.BaselineMean / max) * (height * 0.75f);
                float trainedHeight = (metric.TrainedMean / max) * (height * 0.75f);

                float x = i * slotWidth + slotWidth * 0.15f;
                CreateBar(comparisonBarArea, x, baselineHeight, slotWidth * 0.3f, baselineBarColor);
                CreateBar(comparisonBarArea, x + slotWidth * 0.35f, trainedHeight, slotWidth * 0.3f, trainedBarColor);
                CreateLabel(comparisonBarArea, x, -20f, slotWidth * 0.8f, 40f, metric.MetricName);
            }
        }

        public void DrawTaskCompletionImprovementLine()
        {
            if (performanceValidator == null)
            {
                return;
            }

            var baseline = performanceValidator.BaselineEpisodes;
            var trained = performanceValidator.TrainedEpisodes;
            int count = Mathf.Min(baseline.Count, trained.Count);
            var points = new List<float>(count);
            for (int i = 0; i < count; i++)
            {
                float source = baseline[i].AverageTaskCompletionTime;
                float target = trained[i].AverageTaskCompletionTime;
                float improvement = source > 0f ? ((source - target) / source) * 100f : 0f;
                points.Add(improvement);
            }

            DrawLineGraph(completionGraphArea, points, completionColor);
        }

        private void HandleRewardUpdated(IReadOnlyList<float> values)
        {
            DrawLineGraph(rewardGraphArea, values, rewardColor);
        }

        private void HandleCollisionUpdated(IReadOnlyList<float> values)
        {
            DrawLineGraph(collisionGraphArea, values, collisionColor);
        }

        private void DrawLineGraph(RectTransform container, IReadOnlyList<float> values, Color color)
        {
            ClearContainer(container);
            if (container == null || values == null || values.Count < 2)
            {
                return;
            }

            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < values.Count; i++)
            {
                min = Mathf.Min(min, values[i]);
                max = Mathf.Max(max, values[i]);
            }

            float range = Mathf.Max(0.001f, max - min);
            float width = container.rect.width;
            float height = container.rect.height;
            Vector2? previous = null;

            for (int i = 0; i < values.Count; i++)
            {
                float x = (i / Mathf.Max(1f, values.Count - 1f)) * width;
                float y = ((values[i] - min) / range) * height;
                Vector2 point = new(x, y);
                if (previous.HasValue)
                {
                    CreateLineSegment(container, previous.Value, point, color);
                }

                previous = point;
            }
        }

        private void CreateLineSegment(RectTransform parent, Vector2 a, Vector2 b, Color color)
        {
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(parent, false);
            spawned.Add(line);

            var image = line.GetComponent<Image>();
            image.color = color;

            var rect = line.GetComponent<RectTransform>();
            Vector2 dir = (b - a).normalized;
            float distance = Vector2.Distance(a, b);
            rect.sizeDelta = new Vector2(distance, lineThickness);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = a;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        private void CreateBar(RectTransform parent, float x, float h, float w, Color color)
        {
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(parent, false);
            spawned.Add(bar);

            var image = bar.GetComponent<Image>();
            image.color = color;

            var rect = bar.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(w, Mathf.Max(0f, h));
        }

        private void CreateLabel(RectTransform parent, float x, float y, float w, float h, string text)
        {
            var labelObj = new GameObject("BarLabel", typeof(RectTransform), typeof(Text));
            labelObj.transform.SetParent(parent, false);
            spawned.Add(labelObj);

            var label = labelObj.GetComponent<Text>();
            label.text = text;
            label.font = labelFont != null ? labelFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.alignment = TextAnchor.UpperLeft;
            label.resizeTextForBestFit = true;
            label.color = Color.white;

            var rect = labelObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private void ClearContainer(RectTransform container)
        {
            if (container == null)
            {
                return;
            }

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
        }
    }
}
