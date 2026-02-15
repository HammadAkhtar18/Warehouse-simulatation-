using System.Collections.Generic;
using UnityEngine;
using WarehouseSimulation.Robots;
using WarehouseSimulation.Tasks;

namespace WarehouseSimulation.Managers
{
    /// <summary>
    /// Full warehouse task orchestration:
    /// - Generates random customer orders
    /// - Monitors low-stock shelves and creates restock tasks
    /// - Maintains priority queues
    /// - Assigns work to available robots
    /// - Balances delivery vs. restock throughput
    /// </summary>
    public class TaskManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RobotCoordinator robotCoordinator;
        [SerializeField] private Transform deliveryZone;
        [SerializeField] private LearningMetrics learningMetrics;

        [Header("Order Generation")]
        [SerializeField] private bool autoGenerateOrders = true;
        [SerializeField, Min(0.2f)] private float orderGenerationInterval = 3f;
        [SerializeField, Min(1)] private int minOrderQuantity = 1;
        [SerializeField, Min(1)] private int maxOrderQuantity = 6;

        [Header("Restocking")]
        [SerializeField, Min(1)] private int defaultRestockAmount = 20;
        [SerializeField, Min(0f)] private float restockWeightMultiplier = 0.9f;
        [SerializeField] private bool elevateCriticalRestocks = true;

        [Header("Robot Work")]
        [SerializeField, Min(0.1f)] private float pickDurationSeconds = 1.5f;
        [SerializeField] private bool disableRandomRoaming = true;

        [Header("Balancing")]
        [SerializeField, Min(1)] private int maxConsecutiveDeliveryAssignments = 2;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging;

        private readonly List<Shelf> allShelves = new();
        private readonly List<Shelf> orderShelfCandidates = new();
        private readonly List<Order> orderQueue = new();
        private readonly List<RestockTask> restockQueue = new();
        private readonly HashSet<Shelf> restockShelvesPending = new();
        private sealed class AssignmentTelemetry
        {
            public float StartedAt;
            public float DistanceAtAssignment;
            public float OptimalDistance;
        }

        private readonly Dictionary<RobotAgent, AssignmentTelemetry> activeAssignments = new();

        private float orderTimer;
        private int deliveryStreak;
        private int taskSequence;

        public void SetVerboseLogging(bool enabled)
        {
            verboseLogging = enabled;
        }

        private void Awake()
        {
            if (robotCoordinator == null)
            {
                robotCoordinator = FindObjectOfType<RobotCoordinator>();
            }

            DiscoverShelves();
            SubscribeToShelfEvents();
            SeedInitialLowStockTasks();

            if (robotCoordinator != null)
            {
                robotCoordinator.SetRandomMovementEnabled(!disableRandomRoaming);
            }

            if (learningMetrics == null)
            {
                learningMetrics = FindObjectOfType<LearningMetrics>();
            }
        }

        private void Update()
        {
            TryGenerateRandomOrder();
            AssignTasksToAvailableRobots();

            if (learningMetrics != null && robotCoordinator != null)
            {
                learningMetrics.Tick(Time.deltaTime, robotCoordinator.ActiveRobots);
            }
        }

        private void OnDestroy()
        {
            foreach (Shelf shelf in allShelves)
            {
                if (shelf != null)
                {
                    shelf.OnLowStockDetected -= HandleLowStockDetected;
                }
            }

            // Clear retained references so scene reloads do not accumulate stale task state.
            allShelves.Clear();
            orderShelfCandidates.Clear();
            orderQueue.Clear();
            restockQueue.Clear();
            restockShelvesPending.Clear();
            activeAssignments.Clear();
        }

        private void DiscoverShelves()
        {
            allShelves.Clear();
            allShelves.AddRange(FindObjectsOfType<Shelf>());
        }

        private void SubscribeToShelfEvents()
        {
            foreach (Shelf shelf in allShelves)
            {
                if (shelf == null)
                {
                    continue;
                }

                shelf.OnLowStockDetected -= HandleLowStockDetected;
                shelf.OnLowStockDetected += HandleLowStockDetected;
            }
        }

        private void SeedInitialLowStockTasks()
        {
            foreach (Shelf shelf in allShelves)
            {
                if (shelf != null && shelf.IsLowStock)
                {
                    EnqueueRestockTask(shelf);
                }
            }
        }

        private void TryGenerateRandomOrder()
        {
            if (!autoGenerateOrders)
            {
                return;
            }

            orderTimer += Time.deltaTime;
            if (orderTimer < orderGenerationInterval)
            {
                return;
            }

            orderTimer = 0f;
            Shelf targetShelf = PickRandomOrderShelf();
            if (targetShelf == null)
            {
                return;
            }

            int quantity = UnityEngine.Random.Range(minOrderQuantity, maxOrderQuantity + 1);
            quantity = Mathf.Min(quantity, Mathf.Max(1, targetShelf.CurrentStock));

            Order order = new(BuildTaskId("ORD"), targetShelf, quantity, EvaluateOrderPriority(targetShelf, quantity));
            orderQueue.Add(order);

            if (verboseLogging)
            {
                Debug.Log($"[TaskManager] Enqueued order {order.TaskId} -> shelf={targetShelf.name}, qty={quantity}, prio={order.Priority}");
            }
        }

        private Shelf PickRandomOrderShelf()
        {
            // Optimization strategy:
            // Reuse a persistent candidate list to avoid per-order allocations during
            // long-running simulations and higher order throughput.
            orderShelfCandidates.Clear();
            foreach (Shelf shelf in allShelves)
            {
                if (shelf != null && shelf.CurrentStock > 0 && shelf.InventoryItem != null)
                {
                    orderShelfCandidates.Add(shelf);
                }
            }

            if (orderShelfCandidates.Count == 0)
            {
                return null;
            }

            int randomIndex = UnityEngine.Random.Range(0, orderShelfCandidates.Count);
            return orderShelfCandidates[randomIndex];
        }

        private TaskPriority EvaluateOrderPriority(Shelf shelf, int quantity)
        {
            if (shelf == null)
            {
                return TaskPriority.Normal;
            }

            // Prefer faster handling for orders that consume a large part of a shelf's current stock.
            float demandRatio = quantity / Mathf.Max(1f, shelf.CurrentStock);
            if (demandRatio >= 0.85f)
            {
                return TaskPriority.Critical;
            }

            if (demandRatio >= 0.55f)
            {
                return TaskPriority.High;
            }

            return TaskPriority.Normal;
        }

        private void HandleLowStockDetected(Shelf shelf)
        {
            EnqueueRestockTask(shelf);
        }

        private void EnqueueRestockTask(Shelf shelf)
        {
            if (shelf == null || restockShelvesPending.Contains(shelf))
            {
                return;
            }

            int quantity = Mathf.Min(defaultRestockAmount, shelf.AvailableSpace);
            if (quantity <= 0)
            {
                return;
            }

            TaskPriority priority = elevateCriticalRestocks && shelf.StockColor == ShelfStockColor.Red
                ? TaskPriority.Critical
                : TaskPriority.High;

            RestockTask restockTask = new(BuildTaskId("RST"), shelf, quantity, priority);
            restockQueue.Add(restockTask);
            restockShelvesPending.Add(shelf);

            if (verboseLogging)
            {
                Debug.Log($"[TaskManager] Enqueued restock {restockTask.TaskId} -> shelf={shelf.name}, qty={quantity}, prio={priority}");
            }
        }

        private void AssignTasksToAvailableRobots()
        {
            if (robotCoordinator == null)
            {
                return;
            }

            IReadOnlyList<RobotAgent> robots = robotCoordinator.ActiveRobots;
            if (robots == null || robots.Count == 0)
            {
                return;
            }

            foreach (RobotAgent robot in robots)
            {
                if (robot == null || robot.Status != RobotAgent.RobotStatus.Idle || activeAssignments.ContainsKey(robot))
                {
                    continue;
                }

                IWarehouseTask nextTask = DequeueBestTask();
                if (nextTask == null)
                {
                    return;
                }

                bool assigned = robot.AssignWarehouseTask(nextTask, deliveryZone, pickDurationSeconds, OnRobotTaskCompleted);
                if (!assigned)
                {
                    Requeue(nextTask);
                    continue;
                }

                activeAssignments[robot] = BuildAssignmentTelemetry(robot, nextTask);
                if (nextTask.IsRestockTask)
                {
                    deliveryStreak = 0;
                }
                else
                {
                    deliveryStreak++;
                }

                if (verboseLogging)
                {
                    Debug.Log($"[TaskManager] Assigned {nextTask.TaskId} ({(nextTask.IsRestockTask ? "RESTOCK" : "ORDER")}, prio={nextTask.Priority}) to {robot.name}");
                }
            }
        }

        private IWarehouseTask DequeueBestTask()
        {
            Order bestOrder = PickBest(orderQueue);
            RestockTask bestRestock = PickBest(restockQueue, restockWeightMultiplier);

            if (bestOrder == null && bestRestock == null)
            {
                return null;
            }

            if (bestOrder == null)
            {
                restockQueue.Remove(bestRestock);
                restockShelvesPending.Remove(bestRestock.TargetShelf);
                return bestRestock;
            }

            if (bestRestock == null)
            {
                orderQueue.Remove(bestOrder);
                return bestOrder;
            }

            if (deliveryStreak >= maxConsecutiveDeliveryAssignments)
            {
                restockQueue.Remove(bestRestock);
                restockShelvesPending.Remove(bestRestock.TargetShelf);
                return bestRestock;
            }

            float orderScore = Score(bestOrder.Priority, bestOrder.CreatedAt, 1f);
            float restockScore = Score(bestRestock.Priority, bestRestock.CreatedAt, restockWeightMultiplier);

            if (restockScore > orderScore)
            {
                restockQueue.Remove(bestRestock);
                restockShelvesPending.Remove(bestRestock.TargetShelf);
                return bestRestock;
            }

            orderQueue.Remove(bestOrder);
            return bestOrder;
        }

        private static T PickBest<T>(List<T> queue, float weightMultiplier = 1f) where T : class, IWarehouseTask
        {
            if (queue == null || queue.Count == 0)
            {
                return null;
            }

            T best = queue[0];
            float bestScore = Score(best.Priority, best.CreatedAt, weightMultiplier);

            for (int i = 1; i < queue.Count; i++)
            {
                T current = queue[i];
                float currentScore = Score(current.Priority, current.CreatedAt, weightMultiplier);
                if (currentScore > bestScore)
                {
                    bestScore = currentScore;
                    best = current;
                }
            }

            return best;
        }

        private static float Score(TaskPriority priority, float createdAt, float weightMultiplier)
        {
            float age = Time.time - createdAt;
            return (((int)priority + 1) * 10f + age) * weightMultiplier;
        }

        private void Requeue(IWarehouseTask task)
        {
            switch (task)
            {
                case Order order:
                    orderQueue.Add(order);
                    break;
                case RestockTask restock:
                    restockQueue.Add(restock);
                    restockShelvesPending.Add(restock.TargetShelf);
                    break;
            }
        }

        private void OnRobotTaskCompleted(RobotAgent robot, IWarehouseTask completedTask)
        {
            AssignmentTelemetry telemetry = null;
            if (robot != null)
            {
                activeAssignments.TryGetValue(robot, out telemetry);
                activeAssignments.Remove(robot);
            }

            if (learningMetrics != null && robot != null && telemetry != null)
            {
                float completionTime = Mathf.Max(0f, Time.time - telemetry.StartedAt);
                float actualDistance = Mathf.Max(0f, robot.EpisodeDistanceTraveled - telemetry.DistanceAtAssignment);
                learningMetrics.RecordTaskCompletion(completionTime, actualDistance, telemetry.OptimalDistance);
            }

            if (completedTask?.TargetShelf == null)
            {
                return;
            }

            switch (completedTask)
            {
                case Order order:
                    int picked = completedTask.TargetShelf.RemoveStock(order.Quantity);
                    if (verboseLogging)
                    {
                        Debug.Log($"[TaskManager] Completed order {order.TaskId}: picked {picked} from {completedTask.TargetShelf.name}");
                    }
                    break;

                case RestockTask restock:
                    int stocked = completedTask.TargetShelf.AddStock(restock.Quantity);
                    if (verboseLogging)
                    {
                        Debug.Log($"[TaskManager] Completed restock {restock.TaskId}: added {stocked} to {completedTask.TargetShelf.name}");
                    }
                    break;
            }

            if (completedTask.TargetShelf.IsLowStock)
            {
                EnqueueRestockTask(completedTask.TargetShelf);
            }
        }


        private AssignmentTelemetry BuildAssignmentTelemetry(RobotAgent robot, IWarehouseTask task)
        {
            float startDistance = robot != null ? robot.EpisodeDistanceTraveled : 0f;
            Vector3 robotPosition = robot != null ? robot.transform.position : Vector3.zero;
            Vector3 shelfPosition = task?.TargetShelf != null ? task.TargetShelf.transform.position : robotPosition;
            Vector3 dropPosition = deliveryZone != null ? deliveryZone.position : shelfPosition;

            float optimalDistance = Vector3.Distance(robotPosition, shelfPosition) + Vector3.Distance(shelfPosition, dropPosition);

            return new AssignmentTelemetry
            {
                StartedAt = Time.time,
                DistanceAtAssignment = startDistance,
                OptimalDistance = Mathf.Max(0.0001f, optimalDistance)
            };
        }

        private string BuildTaskId(string prefix)
        {
            taskSequence++;
            return $"{prefix}-{taskSequence:00000}";
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!verboseLogging)
            {
                return;
            }

            GUI.Label(new Rect(15f, 15f, 480f, 25f), $"Orders: {orderQueue.Count} | Restocks: {restockQueue.Count} | Active: {activeAssignments.Count}");
        }
#endif
    }
}
