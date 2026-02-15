using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WarehouseSimulation.Managers;

namespace WarehouseSimulation.UI
{
    [DisallowMultipleComponent]
    public class ValidationUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PerformanceValidator validator;
        [SerializeField] private LearningGraphs learningGraphs;

        [Header("Buttons")]
        [SerializeField] private Button runBaselineButton;
        [SerializeField] private Button runTrainedButton;
        [SerializeField] private Button compareResultsButton;
        [SerializeField] private Button exportReportButton;

        [Header("Display")]
        [SerializeField] private TextMeshProUGUI summaryTableText;
        [SerializeField] private TextMeshProUGUI statusText;

        private void Awake()
        {
            if (validator == null)
            {
                validator = FindObjectOfType<PerformanceValidator>();
            }

            if (learningGraphs == null)
            {
                learningGraphs = FindObjectOfType<LearningGraphs>();
            }

            if (runBaselineButton != null)
            {
                runBaselineButton.onClick.AddListener(HandleRunBaseline);
            }

            if (runTrainedButton != null)
            {
                runTrainedButton.onClick.AddListener(HandleRunTrained);
            }

            if (compareResultsButton != null)
            {
                compareResultsButton.onClick.AddListener(HandleCompare);
            }

            if (exportReportButton != null)
            {
                exportReportButton.onClick.AddListener(HandleExport);
            }

            RefreshTable();
        }

        private void HandleRunBaseline()
        {
            if (validator == null)
            {
                return;
            }

            validator.RunBaselineTest();
            SetStatus("Running baseline validation (100 episodes)...");
        }

        private void HandleRunTrained()
        {
            if (validator == null)
            {
                return;
            }

            validator.RunTrainedTest();
            SetStatus("Running trained validation (100 episodes)...");
        }

        private void HandleCompare()
        {
            if (validator == null)
            {
                return;
            }

            validator.CompareResults();
            learningGraphs?.RefreshComparisonBars();
            learningGraphs?.DrawTaskCompletionImprovementLine();
            RefreshTable();
            SetStatus("Comparison report generated.");
        }

        private void HandleExport()
        {
            if (validator == null)
            {
                return;
            }

            string folder = validator.ExportAllReports();
            SetStatus($"Export completed: {folder}");
        }

        private void RefreshTable()
        {
            if (summaryTableText == null || validator == null)
            {
                return;
            }

            var report = validator.LastComparisonReport;
            var sb = new StringBuilder();
            sb.AppendLine("Metric | Baseline | Trained | Improvement");
            sb.AppendLine("------------------------------------------------");

            if (report != null && report.Metrics != null && report.Metrics.Count > 0)
            {
                for (int i = 0; i < report.Metrics.Count; i++)
                {
                    var metric = report.Metrics[i];
                    sb.AppendLine($"{metric.MetricName} | {metric.BaselineMean:F2} | {metric.TrainedMean:F2} | {metric.ImprovementPercent:F2}%");
                }
            }
            else
            {
                sb.AppendLine("No comparison data yet. Run baseline + trained tests, then compare.");
            }

            summaryTableText.text = sb.ToString();
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
