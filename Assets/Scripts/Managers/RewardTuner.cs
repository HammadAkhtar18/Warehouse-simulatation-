using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WarehouseSimulation.Robots;

namespace WarehouseSimulation.Managers
{
    [Serializable]
    public struct RewardWeights
    {
        [Header("Task Completion Rewards")]
        public float TaskCompletionBaseReward;
        public float TaskCompletionSpeedBonus;
        public float TaskCompletionEfficiencyBonus;

        [Header("Movement Rewards")]
        public float ProgressTowardTargetRewardPerUnit;
        public float EfficientPathfindingRewardPerMove;
        public float SmoothMovementReward;

        [Header("Penalties")]
        public float CollisionWithRobotPenalty;
        public float CollisionWithObstaclePenalty;
        public float WrongDirectionPenalty;
        public float ExcessiveIdlePenalty;
        public float PathInefficiencyPenaltyPerUnit;

        [Header("Coordination Rewards")]
        public float SuccessfulYieldingReward;
        public float AvoidingCongestionReward;
        public float CooperativePathfindingReward;

        public static RewardWeights CreateDefault()
        {
            return new RewardWeights
            {
                TaskCompletionBaseReward = 10f,
                TaskCompletionSpeedBonus = 5f,
                TaskCompletionEfficiencyBonus = 3f,
                ProgressTowardTargetRewardPerUnit = 0.1f,
                EfficientPathfindingRewardPerMove = 0.05f,
                SmoothMovementReward = 0.02f,
                CollisionWithRobotPenalty = 5f,
                CollisionWithObstaclePenalty = 3f,
                WrongDirectionPenalty = 0.5f,
                ExcessiveIdlePenalty = 2f,
                PathInefficiencyPenaltyPerUnit = 0.1f,
                SuccessfulYieldingReward = 1f,
                AvoidingCongestionReward = 0.5f,
                CooperativePathfindingReward = 0.3f,
            };
        }
    }

    public enum RewardPreset
    {
        Balanced,
        SpeedFocused,
        EfficiencyFocused,
        SafetyFocused,
    }

    [DisallowMultipleComponent]
    public class RewardTuner : MonoBehaviour
    {
        [Header("Runtime State")]
        [SerializeField] private RewardPreset activePreset = RewardPreset.Balanced;
        [SerializeField] private RewardWeights currentWeights = default;
        [SerializeField] private bool autoBuildPanel = true;
        [SerializeField] private bool autoSuggestBalance = true;
        [SerializeField, Min(1f)] private float analysisIntervalSeconds = 5f;

        [Header("Events")]
        [SerializeField] private UnityEvent<RewardPreset> onPresetChanged;

        private readonly List<string> balanceSuggestions = new();
        private float analysisTimer;

        private Text presetLabel;

        public RewardWeights CurrentWeights => currentWeights;
        public RewardPreset ActivePreset => activePreset;
        public IReadOnlyList<string> BalanceSuggestions => balanceSuggestions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<RewardTuner>() != null)
            {
                return;
            }

            var tuner = new GameObject("RewardTuner");
            tuner.AddComponent<RewardTuner>();
        }

        private void Awake()
        {
            if (currentWeights.TaskCompletionBaseReward <= 0f)
            {
                currentWeights = RewardWeights.CreateDefault();
            }

            ApplyPreset(activePreset);

            if (autoBuildPanel)
            {
                EnsureEventSystem();
                BuildUIPanel();
            }
        }

        private void Update()
        {
            if (!autoSuggestBalance)
            {
                return;
            }

            analysisTimer += Time.deltaTime;
            if (analysisTimer < analysisIntervalSeconds)
            {
                return;
            }

            analysisTimer = 0f;
            AnalyzeAgentBehavior();
        }

        public void ApplyPreset(RewardPreset preset)
        {
            activePreset = preset;
            currentWeights = GetPresetWeights(preset);
            onPresetChanged?.Invoke(activePreset);

            if (presetLabel != null)
            {
                presetLabel.text = $"Preset: {activePreset}";
            }
        }

        private RewardWeights GetPresetWeights(RewardPreset preset)
        {
            RewardWeights weights = RewardWeights.CreateDefault();

            switch (preset)
            {
                case RewardPreset.SpeedFocused:
                    weights.TaskCompletionSpeedBonus = 8f;
                    weights.ProgressTowardTargetRewardPerUnit = 0.16f;
                    weights.EfficientPathfindingRewardPerMove = 0.03f;
                    weights.CollisionWithRobotPenalty = 4f;
                    weights.CollisionWithObstaclePenalty = 2.5f;
                    break;
                case RewardPreset.EfficiencyFocused:
                    weights.TaskCompletionEfficiencyBonus = 6f;
                    weights.EfficientPathfindingRewardPerMove = 0.12f;
                    weights.PathInefficiencyPenaltyPerUnit = 0.2f;
                    weights.ProgressTowardTargetRewardPerUnit = 0.08f;
                    break;
                case RewardPreset.SafetyFocused:
                    weights.CollisionWithRobotPenalty = 8f;
                    weights.CollisionWithObstaclePenalty = 5f;
                    weights.WrongDirectionPenalty = 0.9f;
                    weights.ExcessiveIdlePenalty = 1f;
                    weights.AvoidingCongestionReward = 1f;
                    weights.SuccessfulYieldingReward = 2f;
                    break;
                case RewardPreset.Balanced:
                default:
                    break;
            }

            return weights;
        }

        private void AnalyzeAgentBehavior()
        {
            RobotAgent[] agents = FindObjectsOfType<RobotAgent>();
            if (agents.Length == 0)
            {
                return;
            }

            float avgCollisions = agents.Average(a => (float)a.EpisodeCollisionCount);
            float avgTrend = agents.Average(a => a.RewardTrendPerSecond);
            float avgTasks = agents.Average(a => (float)a.CompletedTaskCount);
            float avgEfficiency = agents.Average(a => a.AverageTaskEfficiency);

            balanceSuggestions.Clear();

            if (avgCollisions > 3f)
            {
                balanceSuggestions.Add("High collision rate: increase collision penalties or raise congestion/yielding rewards.");
            }

            if (avgTrend < 0f)
            {
                balanceSuggestions.Add("Negative reward trend: reduce frequent penalties (wrong-direction/idle) or increase progress reward.");
            }

            if (avgTasks < 1f)
            {
                balanceSuggestions.Add("Low task throughput: increase task completion base/speed bonus.");
            }

            if (avgEfficiency < 0.75f)
            {
                balanceSuggestions.Add("Path efficiency is low: increase path inefficiency penalty or efficiency bonus.");
            }

            if (balanceSuggestions.Count == 0)
            {
                balanceSuggestions.Add("Reward profile looks balanced for current behavior.");
            }
        }

        private void BuildUIPanel()
        {
            var canvasObject = new GameObject("RewardTunerCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = CreatePanel("RewardPanel", canvasObject.transform, new Color(0f, 0f, 0f, 0.55f));
            RectTransform rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0.68f, 0.04f);
            rect.anchorMax = new Vector2(0.99f, 0.96f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateText("Header", panel.transform, "Reward Tuning", 20, FontStyle.Bold, TextAnchor.MiddleCenter);
            presetLabel = CreateText("PresetLabel", panel.transform, $"Preset: {activePreset}", 15, FontStyle.Bold, TextAnchor.MiddleCenter);

            CreatePresetButtons(panel.transform);

            AddSlider(panel.transform, "Task Base", 0f, 30f, currentWeights.TaskCompletionBaseReward, v => currentWeights.TaskCompletionBaseReward = v);
            AddSlider(panel.transform, "Task Speed", 0f, 15f, currentWeights.TaskCompletionSpeedBonus, v => currentWeights.TaskCompletionSpeedBonus = v);
            AddSlider(panel.transform, "Task Efficiency", 0f, 10f, currentWeights.TaskCompletionEfficiencyBonus, v => currentWeights.TaskCompletionEfficiencyBonus = v);

            AddSlider(panel.transform, "Progress/Unit", 0f, 1f, currentWeights.ProgressTowardTargetRewardPerUnit, v => currentWeights.ProgressTowardTargetRewardPerUnit = v);
            AddSlider(panel.transform, "Efficient Move", 0f, 1f, currentWeights.EfficientPathfindingRewardPerMove, v => currentWeights.EfficientPathfindingRewardPerMove = v);
            AddSlider(panel.transform, "Smooth Move", 0f, 0.2f, currentWeights.SmoothMovementReward, v => currentWeights.SmoothMovementReward = v);

            AddSlider(panel.transform, "Robot Collision", 0f, 15f, currentWeights.CollisionWithRobotPenalty, v => currentWeights.CollisionWithRobotPenalty = v);
            AddSlider(panel.transform, "Obstacle Collision", 0f, 10f, currentWeights.CollisionWithObstaclePenalty, v => currentWeights.CollisionWithObstaclePenalty = v);
            AddSlider(panel.transform, "Wrong Direction", 0f, 2f, currentWeights.WrongDirectionPenalty, v => currentWeights.WrongDirectionPenalty = v);
            AddSlider(panel.transform, "Idle Penalty", 0f, 10f, currentWeights.ExcessiveIdlePenalty, v => currentWeights.ExcessiveIdlePenalty = v);
            AddSlider(panel.transform, "Path Inefficiency", 0f, 1f, currentWeights.PathInefficiencyPenaltyPerUnit, v => currentWeights.PathInefficiencyPenaltyPerUnit = v);

            AddSlider(panel.transform, "Yielding", 0f, 3f, currentWeights.SuccessfulYieldingReward, v => currentWeights.SuccessfulYieldingReward = v);
            AddSlider(panel.transform, "Avoid Congestion", 0f, 2f, currentWeights.AvoidingCongestionReward, v => currentWeights.AvoidingCongestionReward = v);
            AddSlider(panel.transform, "Cooperative Path", 0f, 2f, currentWeights.CooperativePathfindingReward, v => currentWeights.CooperativePathfindingReward = v);
        }

        private void CreatePresetButtons(Transform parent)
        {
            var row = new GameObject("PresetButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            CreateButton(row.transform, "Balanced", () => ApplyPreset(RewardPreset.Balanced));
            CreateButton(row.transform, "Speed", () => ApplyPreset(RewardPreset.SpeedFocused));
            CreateButton(row.transform, "Efficiency", () => ApplyPreset(RewardPreset.EfficiencyFocused));
            CreateButton(row.transform, "Safety", () => ApplyPreset(RewardPreset.SafetyFocused));
        }

        private void AddSlider(Transform parent, string title, float min, float max, float value, Action<float> onChanged)
        {
            var row = new GameObject($"{title}_Row", typeof(RectTransform), typeof(VerticalLayoutGroup));
            row.transform.SetParent(parent, false);
            var layout = row.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2;

            Text label = CreateText($"{title}_Label", row.transform, $"{title}: {value:0.###}", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
            Slider slider = CreateSlider(row.transform, min, max, value);
            slider.onValueChanged.AddListener(v =>
            {
                onChanged(v);
                label.text = $"{title}: {v:0.###}";
            });
        }

        private static Image CreatePanel(string name, Transform parent, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            Image image = panel.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Slider CreateSlider(Transform parent, float min, float max, float value)
        {
            var sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderObject.transform.SetParent(parent, false);
            var slider = sliderObject.GetComponent<Slider>();

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(sliderObject.transform, false);
            var bgImage = bg.GetComponent<Image>();
            bgImage.color = new Color(1f, 1f, 1f, 0.2f);

            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(sliderObject.transform, false);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            fill.GetComponent<Image>().color = new Color(0.2f, 0.75f, 0.9f, 1f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(sliderObject.transform, false);
            handle.GetComponent<Image>().color = Color.white;

            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            return slider;
        }

        private static Text CreateText(string name, Transform parent, string value, int size, FontStyle style, TextAnchor anchor)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = Color.white;
            return text;
        }

        private static void CreateButton(Transform parent, string label, UnityAction onClick)
        {
            var buttonObject = new GameObject($"{label}Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.2f, 0.4f, 0.7f, 0.9f);

            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(onClick);

            CreateText("Label", buttonObject.transform, label, 12, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }
    }
}
