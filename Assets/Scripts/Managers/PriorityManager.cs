using UnityEngine;
using WarehouseSimulation.Robots;
using WarehouseSimulation.Tasks;

namespace WarehouseSimulation.Managers
{
    /// <summary>
    /// Computes task-based robot priority for coordination decisions.
    /// </summary>
    public class PriorityManager : MonoBehaviour
    {
        public int GetRobotPriority(RobotAgent robot)
        {
            if (robot == null)
            {
                return 0;
            }

            IWarehouseTask activeTask = robot.ActiveWarehouseTask;
            if (activeTask == null)
            {
                return 1;
            }

            return activeTask.IsRestockTask ? 5 : 10;
        }
    }
}
