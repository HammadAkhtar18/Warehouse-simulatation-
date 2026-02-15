using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using WarehouseSimulation.Robots;

namespace WarehouseSimulation.Managers
{
    /// <summary>
    /// Coordinates robot agents and shared movement/task orchestration.
    /// Spawns robots, dispatches random roam targets, resolves node contention,
    /// and monitors active robot states.
    /// </summary>
    public class RobotCoordinator : MonoBehaviour
    {
        [Header("Robot Spawning")]
        [SerializeField] private RobotAgent robotPrefab;
        [SerializeField, Min(1)] private int robotCount = 5;
        [SerializeField] private Transform spawnCenter;
        [SerializeField, Min(0.5f)] private float spawnRadius = 4f;

        [Header("Random Movement")]
        [SerializeField, Min(3)] private int navigationNodeCount = 20;
        [SerializeField, Min(1f)] private float navigationAreaRadius = 12f;
        [SerializeField, Min(0.2f)] private float assignmentInterval = 0.7f;
        [SerializeField, Min(0.1f)] private float yieldDuration = 1f;
        [SerializeField, Min(0.5f)] private float nodeReassignmentDistance = 1.5f;

        [Header("Debug")]
        [SerializeField] private bool logRobotStates;
        [SerializeField] private bool enableRandomMovement = true;
        [SerializeField] private bool enableRobotDebugLogging;

        private readonly List<RobotAgent> robots = new();
        private readonly List<Vector3> navigationNodes = new();
        private readonly Dictionary<RobotAgent, int> desiredNodeByRobot = new();
        private readonly Dictionary<int, RobotAgent> nodeOwner = new();
        private readonly HashSet<int> occupiedNodes = new();

        private float assignmentTimer;
        private float monitorTimer;

        public IReadOnlyList<RobotAgent> ActiveRobots => robots;

        public void SetRandomMovementEnabled(bool enabled)
        {
            enableRandomMovement = enabled;
        }

        private void Awake()
        {
            BuildNavigationNodes();
            SpawnRobots();
            ConfigureLocalAvoidancePriorities();
            ConfigureRobotDebugLogging();
        }

        private void OnDestroy()
        {
            // Explicitly clear containers on teardown so long-running play sessions can restart
            // without retaining stale references from prior simulation runs.
            robots.Clear();
            navigationNodes.Clear();
            desiredNodeByRobot.Clear();
            nodeOwner.Clear();
            occupiedNodes.Clear();
        }

        private void FixedUpdate()
        {
            if (enableRandomMovement)
            {
                assignmentTimer += Time.fixedDeltaTime;
                if (assignmentTimer >= assignmentInterval)
                {
                    assignmentTimer = 0f;
                    AssignRandomMovementTasks();
                }
            }

            MonitorRobotStates();
        }

        private void BuildNavigationNodes()
        {
            navigationNodes.Clear();
            Vector3 center = spawnCenter != null ? spawnCenter.position : transform.position;

            for (int i = 0; i < navigationNodeCount; i++)
            {
                Vector2 ring = Random.insideUnitCircle * navigationAreaRadius;
                Vector3 sample = center + new Vector3(ring.x, 0f, ring.y);

                if (NavMesh.SamplePosition(sample, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    navigationNodes.Add(hit.position);
                }
            }

            if (navigationNodes.Count == 0 && NavMesh.SamplePosition(center, out NavMeshHit fallback, 5f, NavMesh.AllAreas))
            {
                navigationNodes.Add(fallback.position);
            }
        }

        private void SpawnRobots()
        {
            robots.Clear();
            Vector3 center = spawnCenter != null ? spawnCenter.position : transform.position;

            for (int i = 0; i < robotCount; i++)
            {
                Vector2 spread = Random.insideUnitCircle * spawnRadius;
                Vector3 sample = center + new Vector3(spread.x, 0f, spread.y);

                if (!NavMesh.SamplePosition(sample, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                {
                    continue;
                }

                RobotAgent robot = InstantiateRobot(hit.position, i);
                if (robot == null)
                {
                    continue;
                }

                robots.Add(robot);
            }
        }

        private RobotAgent InstantiateRobot(Vector3 position, int index)
        {
            if (robotPrefab != null)
            {
                RobotAgent robot = Instantiate(robotPrefab, position, Quaternion.identity, transform);
                robot.name = $"Robot_{index + 1}";
                robot.SetClickToMoveEnabled(false);
                robot.SetAutoMoveEnabled(false);
                return robot;
            }

            GameObject fallbackRobot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fallbackRobot.transform.SetPositionAndRotation(position, Quaternion.identity);
            fallbackRobot.transform.SetParent(transform);
            fallbackRobot.name = $"Robot_{index + 1}";
            fallbackRobot.AddComponent<Rigidbody>().isKinematic = true;
            fallbackRobot.AddComponent<NavMeshAgent>();
            RobotAgent generated = fallbackRobot.AddComponent<RobotAgent>();
            generated.SetClickToMoveEnabled(false);
            generated.SetAutoMoveEnabled(false);
            return generated;
        }

        private void ConfigureLocalAvoidancePriorities()
        {
            for (int i = 0; i < robots.Count; i++)
            {
                RobotAgent robot = robots[i];

                // Lower priority value means higher right-of-way in Unity NavMesh local avoidance.
                int avoidancePriority = Mathf.Clamp(10 + (i * 12), 0, 99);
                robot.SetAvoidancePriority(avoidancePriority);
            }
        }

        private void AssignRandomMovementTasks()
        {
            if (navigationNodes.Count == 0)
            {
                return;
            }

            desiredNodeByRobot.Clear();
            nodeOwner.Clear();
            occupiedNodes.Clear();

            CacheOccupiedNodes();

            foreach (RobotAgent robot in robots)
            {
                if (robot == null || robot.Status != RobotAgent.RobotStatus.Idle)
                {
                    continue;
                }

                int nodeIndex = PickBestNodeForRobot(robot);
                if (nodeIndex < 0)
                {
                    continue;
                }

                desiredNodeByRobot[robot] = nodeIndex;
            }

            foreach (KeyValuePair<RobotAgent, int> request in desiredNodeByRobot)
            {
                RobotAgent robot = request.Key;
                int nodeIndex = request.Value;

                if (!nodeOwner.TryGetValue(nodeIndex, out RobotAgent currentOwner))
                {
                    nodeOwner[nodeIndex] = robot;
                    continue;
                }

                if (HasHigherPriority(robot, currentOwner))
                {
                    currentOwner.StartYield(yieldDuration);
                    nodeOwner[nodeIndex] = robot;
                }
                else
                {
                    robot.StartYield(yieldDuration);
                }
            }

            foreach (KeyValuePair<int, RobotAgent> assignment in nodeOwner)
            {
                int nodeIndex = assignment.Key;
                RobotAgent winner = assignment.Value;

                if (winner == null)
                {
                    continue;
                }

                if (winner.AssignRoamingDestination(navigationNodes[nodeIndex], nodeIndex))
                {
                    occupiedNodes.Add(nodeIndex);
                }
            }
        }

        private void CacheOccupiedNodes()
        {
            foreach (RobotAgent robot in robots)
            {
                if (robot == null)
                {
                    continue;
                }

                if (robot.AssignedNodeId >= 0)
                {
                    occupiedNodes.Add(robot.AssignedNodeId);
                }
            }
        }

        private int PickBestNodeForRobot(RobotAgent robot)
        {
            int bestIndex = -1;
            float bestScore = float.MaxValue;
            Vector3 robotPosition = robot.transform.position;

            // Optimization strategy:
            // prefer nearest currently unoccupied node to minimize path length, lower
            // pathfinding pressure, and reduce cross-traffic between 10+ robots.
            for (int i = 0; i < navigationNodes.Count; i++)
            {
                if (occupiedNodes.Contains(i))
                {
                    continue;
                }

                Vector3 node = navigationNodes[i];
                float sqrDistance = (node - robotPosition).sqrMagnitude;
                if (sqrDistance < nodeReassignmentDistance * nodeReassignmentDistance)
                {
                    continue;
                }

                if (sqrDistance < bestScore)
                {
                    bestScore = sqrDistance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void ConfigureRobotDebugLogging()
        {
            foreach (RobotAgent robot in robots)
            {
                if (robot == null)
                {
                    continue;
                }

                robot.SetDebugLogging(enableRobotDebugLogging);
            }
        }

        private bool HasHigherPriority(RobotAgent lhs, RobotAgent rhs)
        {
            if (lhs == null)
            {
                return false;
            }

            if (rhs == null)
            {
                return true;
            }

            int lhsPriority = lhs.Agent != null ? lhs.Agent.avoidancePriority : 99;
            int rhsPriority = rhs.Agent != null ? rhs.Agent.avoidancePriority : 99;
            return lhsPriority < rhsPriority;
        }

        private void MonitorRobotStates()
        {
            if (!logRobotStates)
            {
                return;
            }

            monitorTimer += Time.fixedDeltaTime;
            if (monitorTimer < 1f)
            {
                return;
            }

            monitorTimer = 0f;
            int idle = 0;
            int moving = 0;
            int yielding = 0;

            foreach (RobotAgent robot in robots)
            {
                if (robot == null)
                {
                    continue;
                }

                switch (robot.Status)
                {
                    case RobotAgent.RobotStatus.Moving:
                    case RobotAgent.RobotStatus.Delivering:
                        moving++;
                        break;
                    case RobotAgent.RobotStatus.Yielding:
                        yielding++;
                        break;
                    default:
                        idle++;
                        break;
                }
            }

            Debug.Log($"[RobotCoordinator] Robots => Idle: {idle}, Moving: {moving}, Yielding: {yielding}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 center = spawnCenter != null ? spawnCenter.position : transform.position;
            Gizmos.DrawWireSphere(center, navigationAreaRadius);

            Gizmos.color = Color.green;
            foreach (Vector3 node in navigationNodes)
            {
                Gizmos.DrawSphere(node + Vector3.up * 0.2f, 0.15f);
            }
        }
#endif
    }
}
