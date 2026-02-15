using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WarehouseSimulation.Managers;

namespace WarehouseSimulation.UI
{
    [DisallowMultipleComponent]
    public class WarehouseDashboardUI : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private SimulationMetrics metrics;

        [Header("Control Events")]
        [SerializeField] private UnityEvent onStartSimulation;
        [SerializeField] private UnityEvent onStopSimulation;
        [SerializeField] private UnityEvent onResetSimulation;
        [SerializeField] private UnityEvent onSpawnRobot;
        [SerializeField] private UnityEvent onStartTraining;
        [SerializeField] private UnityEvent onStopTraining;
        [SerializeField] private UnityEvent onResetTrainingEnvironment;
        [SerializeField] private UnityEvent<float> onTimeScaleChanged;
        [SerializeField] private UnityEvent<float> onOrderRateChanged;
        [SerializeField] private UnityEvent<float> onTrainingSpeedMultiplierChanged;

        [Header("Control Defaults")]
        [SerializeField] private Vector2 timeScaleRange = new Vector2(0.2f, 4f);
        [SerializeField] private float defaultTimeScale = 1f;
        [SerializeField] private Vector2 orderRateRange = new Vector2(1f, 30f);
        [SerializeField] private float defaultOrderRate = 8f;
        [SerializeField] private Vector2 trainingSpeedRange = new Vector2(0.2f, 10f);
        [SerializeField] private float defaultTrainingSpeed = 1f;

        private TextMeshProUGUI ordersCompletedValue;
        private TextMeshProUGUI averageDeliveryValue;
        private TextMeshProUGUI utilizationValue;
        private TextMeshProUGUI collisionsValue;
        private TextMeshProUGUI throughputValue;
        private TextMeshProUGUI learningProgressValue;
        private Slider learningProgressSlider;
        private TextMeshProUGUI timeScaleLabel;
        private TextMeshProUGUI orderRateLabel;
        private TextMeshProUGUI trainingSpeedLabel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapDashboard()
        {
            if (FindObjectOfType<WarehouseDashboardUI>() != null)
            {
                return;
            }

            var dashboard = new GameObject("WarehouseDashboardUI");
            dashboard.AddComponent<WarehouseDashboardUI>();
        }

        private void Awake()
        {
            EnsureEventSystem();
            BuildCanvas();
            ConnectMetrics();
        }

        private void OnDestroy()
        {
            if (metrics != null)
            {
                metrics.MetricsChanged -= RefreshMetrics;
            }
        }

        private void ConnectMetrics()
        {
            if (metrics == null)
            {
                metrics = FindObjectOfType<SimulationMetrics>();
            }

            if (metrics == null)
            {
                var metricsObject = new GameObject("SimulationMetrics");
                metrics = metricsObject.AddComponent<SimulationMetrics>();
            }

            metrics.MetricsChanged += RefreshMetrics;
            RefreshMetrics(metrics.Current);
        }

        private void BuildCanvas()
        {
            var canvasObject = new GameObject("DashboardCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var rootPanel = CreatePanel("RootPanel", canvasObject.transform, new Color(0f, 0f, 0f, 0.58f));
            var rootRect = rootPanel.rectTransform;
            rootRect.anchorMin = new Vector2(0.015f, 0.05f);
            rootRect.anchorMax = new Vector2(0.36f, 0.95f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var rootLayout = rootPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(20, 20, 20, 20);
            rootLayout.spacing = 16;
            rootLayout.childForceExpandHeight = false;
            rootLayout.childControlHeight = true;
            rootLayout.childControlWidth = true;

            var fitter = rootPanel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateHeader(rootPanel.transform);
            CreateMetricsSection(rootPanel.transform);
            CreateControlsSection(rootPanel.transform);
        }

        private void CreateHeader(Transform parent)
        {
            var title = CreateText("Title", parent, "Warehouse Simulation Dashboard", 36, FontStyles.Bold);
            title.alignment = TextAlignmentOptions.Center;
        }

        private void CreateMetricsSection(Transform parent)
        {
            var metricsContainer = CreatePanel("Metrics", parent, new Color(1f, 1f, 1f, 0.08f));
            ConfigureSectionLayout(metricsContainer.gameObject);
            CreateSectionTitle(metricsContainer.transform, "Live Performance");

            ordersCompletedValue = CreateMetricRow(metricsContainer.transform, "Orders completed / hour");
            averageDeliveryValue = CreateMetricRow(metricsContainer.transform, "Avg delivery time");
            utilizationValue = CreateMetricRow(metricsContainer.transform, "Robot utilization rate");
            collisionsValue = CreateMetricRow(metricsContainer.transform, "Collision count");
            throughputValue = CreateMetricRow(metricsContainer.transform, "Warehouse throughput");

            var learningRow = CreateRow("LearningProgressRow", metricsContainer.transform);
            CreateText("LearningLabel", learningRow.transform, "Learning progress", 24, FontStyles.Normal);

            var learningColumn = new GameObject("LearningColumn", typeof(RectTransform), typeof(VerticalLayoutGroup));
            learningColumn.transform.SetParent(learningRow.transform, false);
            var learningLayout = learningColumn.GetComponent<VerticalLayoutGroup>();
            learningLayout.spacing = 8;
            learningLayout.childControlHeight = true;
            learningLayout.childForceExpandHeight = false;

            learningProgressSlider = CreateSlider(learningColumn.transform, 0f, 1f, 0f);
            learningProgressSlider.interactable = false;
            learningProgressValue = CreateText("LearningValue", learningColumn.transform, "0%", 22, FontStyles.Bold);
            learningProgressValue.alignment = TextAlignmentOptions.Right;
        }

        private void CreateControlsSection(Transform parent)
        {
            var controlsContainer = CreatePanel("Controls", parent, new Color(1f, 1f, 1f, 0.08f));
            ConfigureSectionLayout(controlsContainer.gameObject);
            CreateSectionTitle(controlsContainer.transform, "Controls");

            var buttonGrid = new GameObject("ButtonGrid", typeof(RectTransform), typeof(GridLayoutGroup));
            buttonGrid.transform.SetParent(controlsContainer.transform, false);
            var grid = buttonGrid.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(250f, 58f);
            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            CreateButton(buttonGrid.transform, "Start simulation", () => onStartSimulation?.Invoke());
            CreateButton(buttonGrid.transform, "Stop simulation", () => onStopSimulation?.Invoke());
            CreateButton(buttonGrid.transform, "Reset simulation", () => onResetSimulation?.Invoke());
            CreateButton(buttonGrid.transform, "Spawn new robot", () => onSpawnRobot?.Invoke());
            CreateButton(buttonGrid.transform, "Start training", () => onStartTraining?.Invoke());
            CreateButton(buttonGrid.transform, "Stop training", () => onStopTraining?.Invoke());
            CreateButton(buttonGrid.transform, "Reset environment", () => onResetTrainingEnvironment?.Invoke());

            timeScaleLabel = CreateLabeledSlider(
                controlsContainer.transform,
                "Adjust time scale",
                timeScaleRange.x,
                timeScaleRange.y,
                defaultTimeScale,
                value =>
                {
                    Time.timeScale = value;
                    timeScaleLabel.text = $"{value:0.00}x";
                    onTimeScaleChanged?.Invoke(value);
                });

            orderRateLabel = CreateLabeledSlider(
                controlsContainer.transform,
                "Adjust order rate",
                orderRateRange.x,
                orderRateRange.y,
                defaultOrderRate,
                value =>
                {
                    orderRateLabel.text = $"{value:0.0} orders/min";
                    onOrderRateChanged?.Invoke(value);
                });

            trainingSpeedLabel = CreateLabeledSlider(
                controlsContainer.transform,
                "Training speed multiplier",
                trainingSpeedRange.x,
                trainingSpeedRange.y,
                defaultTrainingSpeed,
                value =>
                {
                    trainingSpeedLabel.text = $"{value:0.00}x";
                    onTrainingSpeedMultiplierChanged?.Invoke(value);
                });
        }

        private static void ConfigureSectionLayout(GameObject section)
        {
            var layout = section.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 12;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;

            var fitter = section.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void RefreshMetrics(SimulationMetrics.Snapshot snapshot)
        {
            ordersCompletedValue.text = $"{snapshot.OrdersCompletedPerHour:0.0}";
            averageDeliveryValue.text = $"{snapshot.AverageDeliveryTimeSeconds:0.0}s";
            utilizationValue.text = $"{snapshot.RobotUtilizationRate * 100f:0.0}%";
            collisionsValue.text = snapshot.CollisionCount.ToString();
            throughputValue.text = $"{snapshot.WarehouseThroughput:0.0} units/hr";
            learningProgressValue.text = $"{snapshot.LearningProgress * 100f:0.0}%";
            learningProgressSlider.SetValueWithoutNotify(snapshot.LearningProgress);
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

        private static Image CreatePanel(string name, Transform parent, Color color)
        {
            var panelObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(parent, false);
            var image = panelObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static TextMeshProUGUI CreateSectionTitle(Transform parent, string text)
        {
            var title = CreateText("SectionTitle", parent, text, 28, FontStyles.Bold);
            title.color = new Color(0.84f, 0.93f, 1f, 1f);
            return title;
        }

        private static GameObject CreateRow(string name, Transform parent)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.spacing = 8;
            return row;
        }

        private static TextMeshProUGUI CreateMetricRow(Transform parent, string label)
        {
            var row = CreateRow($"{label}Row", parent);

            var labelText = CreateText("Label", row.transform, label, 24, FontStyles.Normal);
            labelText.alignment = TextAlignmentOptions.Left;

            var valueText = CreateText("Value", row.transform, "--", 24, FontStyles.Bold);
            valueText.alignment = TextAlignmentOptions.Right;
            valueText.color = new Color(0.63f, 0.9f, 1f, 1f);
            return valueText;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, FontStyles style)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var tmp = textObject.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            return tmp;
        }

        private static void CreateButton(Transform parent, string label, UnityAction callback)
        {
            var buttonRoot = new GameObject($"{label}Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonRoot.transform.SetParent(parent, false);

            var background = buttonRoot.GetComponent<Image>();
            background.color = new Color(0.12f, 0.34f, 0.56f, 0.9f);

            var button = buttonRoot.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(callback);

            var buttonText = CreateText("Label", buttonRoot.transform, label, 20, FontStyles.Bold);
            buttonText.alignment = TextAlignmentOptions.Center;

            var textRect = buttonText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private TextMeshProUGUI CreateLabeledSlider(
            Transform parent,
            string title,
            float minValue,
            float maxValue,
            float defaultValue,
            UnityAction<float> onValueChanged)
        {
            var row = new GameObject($"{title}Row", typeof(RectTransform), typeof(VerticalLayoutGroup));
            row.transform.SetParent(parent, false);

            var layout = row.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childForceExpandHeight = false;

            var titleRow = CreateRow($"{title}Title", row.transform);
            CreateText("SliderLabel", titleRow.transform, title, 22, FontStyles.Normal);
            var valueLabel = CreateText("SliderValue", titleRow.transform, string.Empty, 22, FontStyles.Bold);
            valueLabel.alignment = TextAlignmentOptions.Right;

            var slider = CreateSlider(row.transform, minValue, maxValue, defaultValue);
            slider.onValueChanged.AddListener(onValueChanged);
            slider.SetValueWithoutNotify(defaultValue);
            onValueChanged.Invoke(defaultValue);
            return valueLabel;
        }

        private static Slider CreateSlider(Transform parent, float minValue, float maxValue, float defaultValue)
        {
            var sliderRoot = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderRoot.transform.SetParent(parent, false);

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(sliderRoot.transform, false);
            var backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = new Color(1f, 1f, 1f, 0.2f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderRoot.transform, false);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = new Color(0.2f, 0.75f, 1f, 1f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(sliderRoot.transform, false);
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = new Color(0.95f, 0.95f, 0.95f, 1f);

            var slider = sliderRoot.GetComponent<Slider>();
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.value = defaultValue;
            slider.targetGraphic = handleImage;
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.direction = Slider.Direction.LeftToRight;

            var rootRect = sliderRoot.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(0f, 26f);

            Stretch(background.GetComponent<RectTransform>(), new Vector2(0f, 0.25f), new Vector2(1f, 0.75f));
            Stretch(fillArea.GetComponent<RectTransform>(), new Vector2(0.02f, 0.3f), new Vector2(0.98f, 0.7f));
            Stretch(fill.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            Stretch(handle.GetComponent<RectTransform>(), new Vector2(0f, 0.1f), new Vector2(0f, 0.9f));
            handle.GetComponent<RectTransform>().sizeDelta = new Vector2(20f, 0f);

            return slider;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
