using System;
using UnityEngine;

namespace WarehouseSimulation.Tasks
{
    /// <summary>
    /// Shared priority levels used by all warehouse work items.
    /// Higher numeric value means the task should be serviced sooner.
    /// </summary>
    public enum TaskPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    /// <summary>
    /// Common contract for tasks that can be queued and assigned to robots.
    /// </summary>
    public interface IWarehouseTask
    {
        string TaskId { get; }
        Shelf TargetShelf { get; }
        int Quantity { get; }
        TaskPriority Priority { get; }
        float CreatedAt { get; }
        bool IsRestockTask { get; }
    }

    /// <summary>
    /// Represents a customer-facing order that requires a robot to fetch items
    /// from a shelf and deliver them to the delivery zone.
    /// </summary>
    [Serializable]
    public class Order : IWarehouseTask
    {
        public string TaskId { get; }
        public Shelf TargetShelf { get; }
        public int Quantity { get; }
        public TaskPriority Priority { get; }
        public float CreatedAt { get; }
        public bool IsRestockTask => false;

        public Order(string taskId, Shelf targetShelf, int quantity, TaskPriority priority)
        {
            TaskId = taskId;
            TargetShelf = targetShelf;
            Quantity = Mathf.Max(1, quantity);
            Priority = priority;
            CreatedAt = Time.time;
        }
    }
}
