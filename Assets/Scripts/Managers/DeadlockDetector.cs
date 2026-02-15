using System.Collections.Generic;
using UnityEngine;
using WarehouseSimulation.Robots;

namespace WarehouseSimulation.Managers
{
    /// <summary>
    /// Detects groups of stalled robots that are likely blocking each other.
    /// </summary>
    public class DeadlockDetector : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float checkIntervalSeconds = 2f;
        [SerializeField, Min(0.1f)] private float stuckDurationSeconds = 3f;
        [SerializeField, Min(0.1f)] private float movementThreshold = 0.2f;
        [SerializeField, Min(0.1f)] private float deadlockClusterDistance = 2f;

        private readonly Dictionary<RobotAgent, Vector3> previousPositions = new();
        private readonly Dictionary<RobotAgent, float> lastMovementTime = new();
        private float nextCheckTime;

        public List<RobotAgent> DetectDeadlocks(IReadOnlyList<RobotAgent> robots)
        {
            if (robots == null || Time.time < nextCheckTime)
            {
                return new List<RobotAgent>();
            }

            nextCheckTime = Time.time + checkIntervalSeconds;
            UpdateMovementTracking(robots);

            List<RobotAgent> stuckRobots = new();
            for (int i = 0; i < robots.Count; i++)
            {
                RobotAgent robot = robots[i];
                if (robot == null)
                {
                    continue;
                }

                if (Time.time - GetLastMovementTime(robot) >= stuckDurationSeconds)
                {
                    stuckRobots.Add(robot);
                }
            }

            if (stuckRobots.Count < 2)
            {
                return new List<RobotAgent>();
            }

            List<RobotAgent> clusteredStuckRobots = new();
            float clusterDistanceSqr = deadlockClusterDistance * deadlockClusterDistance;

            for (int i = 0; i < stuckRobots.Count; i++)
            {
                RobotAgent candidate = stuckRobots[i];
                int neighborCount = 0;

                for (int j = 0; j < stuckRobots.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    RobotAgent other = stuckRobots[j];
                    float sqrDistance = (candidate.transform.position - other.transform.position).sqrMagnitude;
                    if (sqrDistance <= clusterDistanceSqr)
                    {
                        neighborCount++;
                    }
                }

                if (neighborCount > 0)
                {
                    clusteredStuckRobots.Add(candidate);
                }
            }

            return clusteredStuckRobots.Count >= 2 ? clusteredStuckRobots : new List<RobotAgent>();
        }

        private void UpdateMovementTracking(IReadOnlyList<RobotAgent> robots)
        {
            for (int i = 0; i < robots.Count; i++)
            {
                RobotAgent robot = robots[i];
                if (robot == null)
                {
                    continue;
                }

                Vector3 currentPosition = robot.transform.position;
                if (!previousPositions.TryGetValue(robot, out Vector3 previousPosition))
                {
                    previousPositions[robot] = currentPosition;
                    lastMovementTime[robot] = Time.time;
                    continue;
                }

                if ((currentPosition - previousPosition).sqrMagnitude >= movementThreshold * movementThreshold)
                {
                    previousPositions[robot] = currentPosition;
                    lastMovementTime[robot] = Time.time;
                }
            }
        }

        private float GetLastMovementTime(RobotAgent robot)
        {
            if (!lastMovementTime.TryGetValue(robot, out float movementTime))
            {
                movementTime = Time.time;
                lastMovementTime[robot] = movementTime;
                previousPositions[robot] = robot.transform.position;
            }

            return movementTime;
        }
    }
}
