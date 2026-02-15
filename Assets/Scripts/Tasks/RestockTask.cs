using System;
using UnityEngine;

namespace WarehouseSimulation.Tasks
{
    /// <summary>
    /// Represents an internal replenishment task generated when shelf stock is low.
    /// </summary>
    [Serializable]
    public class RestockTask : IWarehouseTask
    {
        public string TaskId { get; }
        public Shelf TargetShelf { get; }
        public int Quantity { get; }
        public TaskPriority Priority { get; }
        public float CreatedAt { get; }
        public bool IsRestockTask => true;

        public RestockTask(string taskId, Shelf targetShelf, int quantity, TaskPriority priority)
        {
            TaskId = taskId;
            TargetShelf = targetShelf;
            Quantity = Mathf.Max(1, quantity);
            Priority = priority;
            CreatedAt = Time.time;
        }
    }
}
