using UnityEngine;

namespace WarehouseSimulation.Managers
{
    /// <summary>
    /// Central lifecycle manager for the warehouse simulation.
    /// Coordinates high-level state transitions and scene-wide systems.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Manager References")]
        [SerializeField] private TaskManager taskManager;
        [SerializeField] private RobotCoordinator robotCoordinator;
        [SerializeField] private PerformanceMetrics performanceMetrics;

        [Header("Runtime Controls")]
        [SerializeField] private bool runIndefinitely = true;
        [SerializeField] private bool enableDebugLogging;

        private void Awake()
        {
            if (runIndefinitely)
            {
                // Prevent pausing when the window loses focus so the simulation can run continuously.
                Application.runInBackground = true;
            }
        }

        private void Start()
        {
            if (taskManager != null)
            {
                taskManager.SetVerboseLogging(enableDebugLogging);
            }
        }
    }
}
