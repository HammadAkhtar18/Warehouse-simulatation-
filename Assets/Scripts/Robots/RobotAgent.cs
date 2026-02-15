using UnityEngine;
using UnityEngine.AI;

namespace WarehouseSimulation.Robots
{
    /// <summary>
    /// Controls a single autonomous warehouse robot using Unity's NavMesh.
    /// Handles movement constraints, simple pick/deliver flow, test destination input,
    /// and basic collision/obstacle detection against shelves and walls.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class RobotAgent : MonoBehaviour
    {
        public enum RobotStatus
        {
            Idle,
            Moving,
            Picking,
            Delivering
        }

        [Header("Movement Constraints")]
        [SerializeField, Min(0.1f)] private float maxSpeed = 3f;
        [SerializeField, Min(0.1f)] private float acceleration = 2.5f;
        [SerializeField, Min(0.1f)] private float deceleration = 4f;
        [SerializeField, Min(0.1f)] private float turningRadius = 1.2f;
        [SerializeField, Min(0.01f)] private float stopDistance = 0.35f;

        [Header("Destination Test")]
        [SerializeField] private bool clickToMove = true;
        [SerializeField] private bool autoMoveToRandomPoint;
        [SerializeField, Min(0.5f)] private float autoMoveInterval = 4f;
        [SerializeField, Min(0.5f)] private float randomMoveRadius = 8f;
        [SerializeField] private Transform randomMoveCenter;
        [SerializeField] private LayerMask clickSurfaceMask = ~0;

        [Header("Task Points")]
        [SerializeField] private Transform deliveryPoint;
        [SerializeField, Min(0.1f)] private float pickDuration = 1.5f;

        [Header("Collision / Obstacle Detection")]
        [SerializeField] private LayerMask obstacleLayers;
        [SerializeField, Min(0.05f)] private float obstacleProbeRadius = 0.3f;
        [SerializeField, Min(0.2f)] private float obstacleProbeDistance = 0.8f;

        public RobotStatus Status { get; private set; } = RobotStatus.Idle;

        private NavMeshAgent agent;
        private Camera cachedMainCamera;
        private float currentSpeed;
        private float autoMoveTimer;
        private float statusTimer;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            cachedMainCamera = Camera.main;

            ConfigureNavAgent();
            SetStatus(RobotStatus.Idle);
        }

        private void Update()
        {
            HandleDestinationInput();
            HandleAutoMove();
            UpdateStatusStateMachine();
            UpdateConstrainedSpeed();
            ProbeForwardForObstacle();
        }

        private void ConfigureNavAgent()
        {
            agent.updateRotation = true;
            agent.autoBraking = true;
            agent.stoppingDistance = stopDistance;
            agent.acceleration = acceleration;

            // Turning radius approximation:
            // angular speed (deg/s) = linear speed (m/s) / radius (m) converted to degrees.
            agent.angularSpeed = Mathf.Rad2Deg * (maxSpeed / turningRadius);

            currentSpeed = 0f;
            agent.speed = 0f;
        }

        private void HandleDestinationInput()
        {
            if (!clickToMove || !Input.GetMouseButtonDown(0))
            {
                return;
            }

            if (cachedMainCamera == null)
            {
                cachedMainCamera = Camera.main;
                if (cachedMainCamera == null)
                {
                    return;
                }
            }

            Ray ray = cachedMainCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f, clickSurfaceMask))
            {
                return;
            }

            SetPickDestination(hit.point);
        }

        private void HandleAutoMove()
        {
            if (!autoMoveToRandomPoint || Status == RobotStatus.Picking)
            {
                return;
            }

            autoMoveTimer += Time.deltaTime;
            if (autoMoveTimer < autoMoveInterval)
            {
                return;
            }

            autoMoveTimer = 0f;

            Vector3 center = randomMoveCenter != null ? randomMoveCenter.position : transform.position;
            Vector3 randomOffset = Random.insideUnitSphere * randomMoveRadius;
            randomOffset.y = 0f;

            if (NavMesh.SamplePosition(center + randomOffset, out NavMeshHit navHit, randomMoveRadius, NavMesh.AllAreas))
            {
                SetPickDestination(navHit.position);
            }
        }

        private void SetPickDestination(Vector3 worldPosition)
        {
            if (!agent.isOnNavMesh)
            {
                return;
            }

            if (agent.SetDestination(worldPosition))
            {
                statusTimer = 0f;
                SetStatus(RobotStatus.Moving);
            }
        }

        private void UpdateStatusStateMachine()
        {
            if (Status == RobotStatus.Moving || Status == RobotStatus.Delivering)
            {
                if (HasArrivedAtDestination())
                {
                    if (Status == RobotStatus.Moving)
                    {
                        agent.ResetPath();
                        statusTimer = pickDuration;
                        SetStatus(RobotStatus.Picking);
                    }
                    else
                    {
                        agent.ResetPath();
                        SetStatus(RobotStatus.Idle);
                    }
                }

                return;
            }

            if (Status == RobotStatus.Picking)
            {
                statusTimer -= Time.deltaTime;
                if (statusTimer > 0f)
                {
                    return;
                }

                if (deliveryPoint != null && agent.isOnNavMesh)
                {
                    bool hasPath = agent.SetDestination(deliveryPoint.position);
                    SetStatus(hasPath ? RobotStatus.Delivering : RobotStatus.Idle);
                }
                else
                {
                    SetStatus(RobotStatus.Idle);
                }
            }
        }

        private bool HasArrivedAtDestination()
        {
            if (agent.pathPending)
            {
                return false;
            }

            if (agent.remainingDistance > agent.stoppingDistance)
            {
                return false;
            }

            return !agent.hasPath || agent.velocity.sqrMagnitude < 0.02f;
        }

        private void UpdateConstrainedSpeed()
        {
            float targetSpeed = 0f;
            if (Status == RobotStatus.Moving || Status == RobotStatus.Delivering)
            {
                float distance = Mathf.Max(agent.remainingDistance - agent.stoppingDistance, 0f);

                // Speed needed to brake smoothly to stop within remaining distance.
                float decelLimitedSpeed = Mathf.Sqrt(2f * deceleration * Mathf.Max(distance, 0f));
                targetSpeed = Mathf.Min(maxSpeed, decelLimitedSpeed);
            }

            float rate = targetSpeed > currentSpeed ? acceleration : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);
            agent.speed = Mathf.Clamp(currentSpeed, 0f, maxSpeed);
        }

        private void ProbeForwardForObstacle()
        {
            if (Status != RobotStatus.Moving && Status != RobotStatus.Delivering)
            {
                return;
            }

            Vector3 origin = transform.position + Vector3.up * 0.3f;
            Vector3 direction = transform.forward;

            bool blocked = Physics.SphereCast(origin, obstacleProbeRadius, direction, out _, obstacleProbeDistance, obstacleLayers, QueryTriggerInteraction.Ignore);
            if (blocked)
            {
                agent.speed = Mathf.Min(agent.speed, maxSpeed * 0.4f);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsObstacleCollision(collision.gameObject.layer))
            {
                Debug.Log($"[{name}] Collided with obstacle: {collision.gameObject.name}");
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            if (IsObstacleCollision(collision.gameObject.layer))
            {
                // While touching walls/shelves, force a near stop to avoid jitter pushing.
                agent.speed = Mathf.Min(agent.speed, 0.2f);
            }
        }

        private bool IsObstacleCollision(int otherLayer)
        {
            return (obstacleLayers.value & (1 << otherLayer)) != 0;
        }

        private void SetStatus(RobotStatus newStatus)
        {
            Status = newStatus;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 center = randomMoveCenter != null ? randomMoveCenter.position : transform.position;
            Gizmos.DrawWireSphere(center, randomMoveRadius);

            Gizmos.color = Color.red;
            Vector3 origin = transform.position + Vector3.up * 0.3f;
            Gizmos.DrawWireSphere(origin + transform.forward * obstacleProbeDistance, obstacleProbeRadius);
        }
#endif
    }
}
