using System;
using UnityEngine;

namespace WarehouseSimulation.Managers
{
    [DisallowMultipleComponent]
    public class SimulationMetrics : MonoBehaviour
    {
        [Serializable]
        public struct Snapshot
        {
            public float OrdersCompletedPerHour;
            public float AverageDeliveryTimeSeconds;
            public float RobotUtilizationRate;
            public int CollisionCount;
            public float WarehouseThroughput;
            [Range(0f, 1f)] public float LearningProgress;
        }

        [SerializeField] private Snapshot current;

        public Snapshot Current => current;

        public event Action<Snapshot> MetricsChanged;

        public void SetOrdersCompletedPerHour(float value)
        {
            current.OrdersCompletedPerHour = Mathf.Max(0f, value);
            NotifyChanged();
        }

        public void SetAverageDeliveryTime(float seconds)
        {
            current.AverageDeliveryTimeSeconds = Mathf.Max(0f, seconds);
            NotifyChanged();
        }

        public void SetRobotUtilizationRate(float value)
        {
            current.RobotUtilizationRate = Mathf.Clamp01(value);
            NotifyChanged();
        }

        public void SetCollisionCount(int value)
        {
            current.CollisionCount = Mathf.Max(0, value);
            NotifyChanged();
        }

        public void SetWarehouseThroughput(float value)
        {
            current.WarehouseThroughput = Mathf.Max(0f, value);
            NotifyChanged();
        }

        public void SetLearningProgress(float value)
        {
            current.LearningProgress = Mathf.Clamp01(value);
            NotifyChanged();
        }

        public void SetSnapshot(Snapshot snapshot)
        {
            current.OrdersCompletedPerHour = Mathf.Max(0f, snapshot.OrdersCompletedPerHour);
            current.AverageDeliveryTimeSeconds = Mathf.Max(0f, snapshot.AverageDeliveryTimeSeconds);
            current.RobotUtilizationRate = Mathf.Clamp01(snapshot.RobotUtilizationRate);
            current.CollisionCount = Mathf.Max(0, snapshot.CollisionCount);
            current.WarehouseThroughput = Mathf.Max(0f, snapshot.WarehouseThroughput);
            current.LearningProgress = Mathf.Clamp01(snapshot.LearningProgress);
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            MetricsChanged?.Invoke(current);
        }

        private void OnValidate()
        {
            current.OrdersCompletedPerHour = Mathf.Max(0f, current.OrdersCompletedPerHour);
            current.AverageDeliveryTimeSeconds = Mathf.Max(0f, current.AverageDeliveryTimeSeconds);
            current.RobotUtilizationRate = Mathf.Clamp01(current.RobotUtilizationRate);
            current.CollisionCount = Mathf.Max(0, current.CollisionCount);
            current.WarehouseThroughput = Mathf.Max(0f, current.WarehouseThroughput);
            current.LearningProgress = Mathf.Clamp01(current.LearningProgress);
        }
    }
}
