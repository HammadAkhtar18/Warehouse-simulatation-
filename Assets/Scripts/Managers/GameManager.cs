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

        // Reserved for future initialization flow.
        private void Awake()
        {
        }

        // Reserved for future simulation start sequence.
        private void Start()
        {
        }
    }
}
