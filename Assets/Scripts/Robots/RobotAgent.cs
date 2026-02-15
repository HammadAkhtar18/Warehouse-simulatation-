using System;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.AI;
using WarehouseSimulation.Tasks;
using WarehouseSimulation.Managers;

namespace WarehouseSimulation.Robots
{
    /// <summary>
    /// Controls a single autonomous warehouse robot using Unity's NavMesh.
    /// Handles movement constraints, pick/deliver flow, and optional manual/random roaming.
    /// Phase 6A: also exposes ML-Agents observations/actions/rewards for learning-based decisions.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class RobotAgent : Agent
    {
        public enum RobotStatus
        {
            Idle,
            Moving,
            Picking,
            Delivering,
            Yielding
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

        [Header("Robot Avoidance")]
        [SerializeField] private ObstacleAvoidanceType obstacleAvoidanceQuality = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        [Header("ML-Agents Decision Setup")]
        [SerializeField] private bool useLearningDecisions;
        [SerializeField, Min(0.1f)] private float decisionTargetRadius = 6f;
        [SerializeField, Min(0.1f)] private float decisionStepDistance = 2.5f;
        [SerializeField, Min(0f)] private float timePenaltyPerStep = 0.01f;
        [SerializeField, Min(0.1f)] private float idleTimeoutSeconds = 3f;
        [SerializeField] private LayerMask robotDetectionLayers;
        [SerializeField, Min(0.5f)] private float nearbyRobotDetectionRadius = 5f;
        [SerializeField, Range(1, 8)] private int maxNearbyRobotObservations = 3;
        [SerializeField, Min(0.05f)] private float idleSpeedThreshold = 0.15f;

        [Header("Debug / Visualization")]
        [SerializeField] private bool debugLogging;
        [SerializeField] private bool visualizePath;
        [SerializeField] private Color pathVisualizationColor = Color.cyan;

        public RobotStatus Status { get; private set; } = RobotStatus.Idle;
        public int AssignedNodeId { get; private set; } = -1;
        public NavMeshAgent Agent => agent;
        public bool IsBusy => activeWarehouseTask != null || Status == RobotStatus.Picking || Status == RobotStatus.Delivering || Status == RobotStatus.Moving;
        public float EpisodeDistanceTraveled { get; private set; }
        public int EpisodeCollisionCount { get; private set; }
        public int CompletedTaskCount { get; private set; }
        public float AverageTaskCompletionTime => completedTaskCounter > 0 ? cumulativeTaskDuration / completedTaskCounter : 0f;
        public float AverageTaskEfficiency => completedTaskCounter > 0 ? cumulativeTaskEfficiency / completedTaskCounter : 1f;
        public float RewardTrendPerSecond { get; private set; }

        private NavMeshAgent agent;
        private Camera cachedMainCamera;
        private float currentSpeed;
        private float autoMoveTimer;
        private float statusTimer;
        private bool shouldPerformPickOnArrival;
        private float yieldingTimer;
        private float taskStartTime;
        private Vector3 previousPosition;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private float previousDistanceToTarget;
        private float idleTime;
        private float previousVelocity;
        private float lastTrendReward;
        private float trendTimer;
        private float taskPathDistance;
        private float taskDirectDistance;
        private float runningAverageTaskTime = 10f;
        private float cumulativeTaskDuration;
        private float cumulativeTaskEfficiency;
        private int completedTaskCounter;
        private float coordinationRewardCooldown;

        private readonly Dictionary<string, float> rewardBreakdown = new();

        private readonly List<RobotAgent> nearbyRobotBuffer = new();
        private Collider[] nearbyRobotColliders;

        private IWarehouseTask activeWarehouseTask;
        private Action<RobotAgent, IWarehouseTask> taskCompletedCallback;
        private Transform activeDeliveryZone;
        private RewardTuner rewardTuner;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            cachedMainCamera = Camera.main;
            nearbyRobotColliders = new Collider[Mathf.Max(8, maxNearbyRobotObservations * 4)];

            // Keep the learning agent alive for continuous warehouse operation.
            // This prevents automatic episode cutoffs from stopping the simulation loop.
            if (useLearningDecisions && MaxStep != 0)
            {
                MaxStep = 0;
            }

            ConfigureNavAgent();
            SetStatus(RobotStatus.Idle);
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            previousPosition = transform.position;
            previousDistanceToTarget = 0f;
            rewardTuner = FindObjectOfType<RewardTuner>();
        }

        private void Update()
        {
            HandleDestinationInput();
            HandleAutoMove();
            UpdateStatusStateMachine();
            UpdateConstrainedSpeed();
            ProbeForwardForObstacle();
            AccumulateDistanceTraveled();

        }

        public override void OnEpisodeBegin()
        {
            AssignedNodeId = -1;
            shouldPerformPickOnArrival = false;
            statusTimer = 0f;
            yieldingTimer = 0f;
            taskStartTime = 0f;
            activeWarehouseTask = null;
            activeDeliveryZone = null;
            taskCompletedCallback = null;
            EpisodeDistanceTraveled = 0f;
            EpisodeCollisionCount = 0;

            transform.position = initialPosition;
            transform.rotation = initialRotation;
            previousPosition = transform.position;
            previousDistanceToTarget = Vector3.Distance(transform.position, GetCurrentLearningTarget());
            idleTime = 0f;
            previousVelocity = 0f;
            taskPathDistance = 0f;
            taskDirectDistance = 0f;
            rewardBreakdown.Clear();
            lastTrendReward = 0f;
            RewardTrendPerSecond = 0f;
            trendTimer = 0f;

            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }

            SetStatus(RobotStatus.Idle);
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            Vector3 target = GetCurrentLearningTarget();
            Vector3 toTarget = target - transform.position;
            float distanceToTarget = toTarget.magnitude;

            sensor.AddObservation(Mathf.Clamp(transform.position.x / decisionTargetRadius, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(transform.position.z / decisionTargetRadius, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(target.x / decisionTargetRadius, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(target.z / decisionTargetRadius, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp01(distanceToTarget / Mathf.Max(decisionTargetRadius, 0.1f)));
            sensor.AddObservation(Mathf.Clamp01(agent.velocity.magnitude / Mathf.Max(maxSpeed, 0.1f)));

            AddObstacleDistanceObservations(sensor);

            // Task type encoded as one-hot: [Idle, Order, Restock].
            sensor.AddObservation(activeWarehouseTask == null ? 1f : 0f);
            sensor.AddObservation(activeWarehouseTask != null && !activeWarehouseTask.IsRestockTask ? 1f : 0f);
            sensor.AddObservation(activeWarehouseTask != null && activeWarehouseTask.IsRestockTask ? 1f : 0f);

            sensor.AddObservation(activeWarehouseTask == null ? 0f : 1f);

            PopulateNearbyRobots();
            sensor.AddObservation(Mathf.Clamp01((float)nearbyRobotBuffer.Count / Mathf.Max(1f, maxNearbyRobotObservations)));
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (!useLearningDecisions || !agent.isOnNavMesh)
            {
                return;
            }

            ActionSegment<float> continuous = actions.ContinuousActions;
            if (continuous.Length < 3)
            {
                return;
            }

            Vector2 moveDirection = new Vector2(
                Mathf.Clamp(continuous[0], -1f, 1f),
                Mathf.Clamp(continuous[1], -1f, 1f));
            float speedScale = Mathf.Clamp01((continuous[2] + 1f) * 0.5f);

            ApplyLearningMovement(moveDirection, speedScale);
            ApplyLearningRewards(moveDirection);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            ActionSegment<float> continuous = actionsOut.ContinuousActions;
            continuous[0] = Input.GetAxisRaw("Horizontal");
            continuous[1] = Input.GetAxisRaw("Vertical");
            continuous[2] = Input.GetKey(KeyCode.LeftShift) ? 1f : 0f;
        }

        private void ConfigureNavAgent()
        {
            agent.updateRotation = true;
            agent.autoBraking = true;
            agent.stoppingDistance = stopDistance;
            agent.acceleration = acceleration;
            agent.obstacleAvoidanceType = obstacleAvoidanceQuality;

            agent.angularSpeed = Mathf.Rad2Deg * (maxSpeed / turningRadius);

            currentSpeed = 0f;
            agent.speed = 0f;
        }

        public void SetClickToMoveEnabled(bool enabled) => clickToMove = enabled;

        public void SetAutoMoveEnabled(bool enabled) => autoMoveToRandomPoint = enabled;

        public void SetAvoidancePriority(int priority)
        {
            if (agent != null)
            {
                agent.avoidancePriority = Mathf.Clamp(priority, 0, 99);
            }
        }

        public void SetDebugLogging(bool enabled)
        {
            debugLogging = enabled;
        }

        public bool AssignRoamingDestination(Vector3 worldPosition, int nodeId)
        {
            if (!agent.isOnNavMesh)
            {
                return false;
            }

            shouldPerformPickOnArrival = false;
            AssignedNodeId = nodeId;

            if (agent.SetDestination(worldPosition))
            {
                statusTimer = 0f;
                yieldingTimer = 0f;
                SetStatus(RobotStatus.Moving);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Assigns a warehouse task (order/restock) to this robot.
        /// Robot path: shelf -> timed pick action -> delivery zone -> completion callback.
        /// </summary>
        public bool AssignWarehouseTask(
            IWarehouseTask task,
            Transform deliveryZone,
            float pickSeconds,
            Action<RobotAgent, IWarehouseTask> onTaskCompleted)
        {
            if (task == null || task.TargetShelf == null || !agent.isOnNavMesh || Status == RobotStatus.Yielding)
            {
                return false;
            }

            if (activeWarehouseTask != null)
            {
                return false;
            }

            activeWarehouseTask = task;
            taskStartTime = Time.time;
            taskPathDistance = 0f;
            taskDirectDistance = Vector3.Distance(task.TargetShelf.transform.position, transform.position);
            activeDeliveryZone = deliveryZone != null ? deliveryZone : deliveryPoint;
            taskCompletedCallback = onTaskCompleted;
            pickDuration = Mathf.Max(0.1f, pickSeconds);

            shouldPerformPickOnArrival = true;
            AssignedNodeId = -1;

            bool hasPath = agent.SetDestination(task.TargetShelf.transform.position);
            if (!hasPath)
            {
                activeWarehouseTask = null;
                activeDeliveryZone = null;
                taskCompletedCallback = null;
                return false;
            }

            statusTimer = 0f;
            SetStatus(RobotStatus.Moving);
            return true;
        }

        public void StartYield(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            yieldingTimer = seconds;
            AssignedNodeId = -1;
            agent.ResetPath();
            SetStatus(RobotStatus.Yielding);

            if (useLearningDecisions)
            {
                AddRewardByCategory("Coordination.SuccessfulYielding", GetWeights().SuccessfulYieldingReward);
            }
        }

        private void HandleDestinationInput()
        {
            if (!clickToMove || !Input.GetMouseButtonDown(0) || activeWarehouseTask != null)
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
            if (!autoMoveToRandomPoint || Status == RobotStatus.Picking || activeWarehouseTask != null)
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

            shouldPerformPickOnArrival = true;
            AssignedNodeId = -1;

            if (agent.SetDestination(worldPosition))
            {
                statusTimer = 0f;
                SetStatus(RobotStatus.Moving);
            }
        }

        private void UpdateStatusStateMachine()
        {
            if (Status == RobotStatus.Yielding)
            {
                yieldingTimer -= Time.deltaTime;
                if (yieldingTimer <= 0f)
                {
                    SetStatus(RobotStatus.Idle);
                }

                return;
            }

            if (Status == RobotStatus.Moving || Status == RobotStatus.Delivering)
            {
                if (HasArrivedAtDestination())
                {
                    if (Status == RobotStatus.Moving)
                    {
                        agent.ResetPath();

                        if (shouldPerformPickOnArrival)
                        {
                            statusTimer = pickDuration;
                            SetStatus(RobotStatus.Picking);
                        }
                        else
                        {
                            AssignedNodeId = -1;
                            SetStatus(RobotStatus.Idle);
                        }
                    }
                    else
                    {
                        agent.ResetPath();
                        CompleteActiveTaskIfAny();
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

                Transform destination = activeDeliveryZone != null ? activeDeliveryZone : deliveryPoint;
                if (destination != null && agent.isOnNavMesh)
                {
                    bool hasPath = agent.SetDestination(destination.position);
                    SetStatus(hasPath ? RobotStatus.Delivering : RobotStatus.Idle);

                    if (!hasPath)
                    {
                        CompleteActiveTaskIfAny();
                    }
                }
                else
                {
                    CompleteActiveTaskIfAny();
                    SetStatus(RobotStatus.Idle);
                }
            }
        }

        private void CompleteActiveTaskIfAny()
        {
            if (activeWarehouseTask == null)
            {
                return;
            }

            IWarehouseTask completedTask = activeWarehouseTask;
            activeWarehouseTask = null;

            if (useLearningDecisions)
            {
                RewardWeights weights = GetWeights();
                float taskDuration = taskStartTime > 0f ? Time.time - taskStartTime : 0f;
                bool fasterThanAverage = taskDuration > 0f && taskDuration < runningAverageTaskTime;
                float efficiency = taskPathDistance > 0.1f
                    ? Mathf.Clamp01(taskDirectDistance / taskPathDistance)
                    : 1f;

                AddRewardByCategory("Task.BaseCompletion", weights.TaskCompletionBaseReward);
                if (fasterThanAverage)
                {
                    AddRewardByCategory("Task.SpeedBonus", weights.TaskCompletionSpeedBonus);
                }

                if (efficiency >= 0.92f)
                {
                    AddRewardByCategory("Task.EfficiencyBonus", weights.TaskCompletionEfficiencyBonus);
                }

                runningAverageTaskTime = Mathf.Lerp(runningAverageTaskTime, Mathf.Max(0.01f, taskDuration), 0.15f);
                cumulativeTaskDuration += taskDuration;
                cumulativeTaskEfficiency += efficiency;
                completedTaskCounter++;
                CompletedTaskCount++;
            }

            taskStartTime = 0f;

            Action<RobotAgent, IWarehouseTask> callback = taskCompletedCallback;
            taskCompletedCallback = null;
            activeDeliveryZone = null;

            callback?.Invoke(this, completedTask);
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
            EpisodeCollisionCount++;

            if (!useLearningDecisions)
            {
                if (IsObstacleCollision(collision.gameObject.layer))
                {
                    LogDebug($"Collided with obstacle: {collision.gameObject.name}");
                }

                return;
            }

            if (collision.gameObject.GetComponentInParent<RobotAgent>() != null)
            {
                AddRewardByCategory("Penalty.RobotCollision", -GetWeights().CollisionWithRobotPenalty);
                return;
            }

            if (IsObstacleCollision(collision.gameObject.layer))
            {
                AddRewardByCategory("Penalty.ObstacleCollision", -GetWeights().CollisionWithObstaclePenalty);
                LogDebug($"Collided with obstacle: {collision.gameObject.name}");
            }
        }

        private void AccumulateDistanceTraveled()
        {
            Vector3 currentPosition = transform.position;
            float delta = Vector3.Distance(previousPosition, currentPosition);

            if (delta > 0.0001f)
            {
                EpisodeDistanceTraveled += delta;
                taskPathDistance += delta;
            }

            previousPosition = currentPosition;
        }

        private void OnCollisionStay(Collision collision)
        {
            if (IsObstacleCollision(collision.gameObject.layer))
            {
                agent.speed = Mathf.Min(agent.speed, 0.2f);
            }
        }

        private bool IsObstacleCollision(int otherLayer) => (obstacleLayers.value & (1 << otherLayer)) != 0;

        private Vector3 GetCurrentLearningTarget()
        {
            if (agent != null && agent.hasPath)
            {
                return agent.destination;
            }

            if (activeWarehouseTask?.TargetShelf != null)
            {
                return activeWarehouseTask.TargetShelf.transform.position;
            }

            return transform.position + transform.forward * decisionStepDistance;
        }

        private void PopulateNearbyRobots()
        {
            nearbyRobotBuffer.Clear();

            // Optimization strategy:
            // Use a reusable NonAlloc physics buffer to avoid per-frame heap allocations when
            // collecting nearby robot observations (important for 10+ concurrent agents).
            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                nearbyRobotDetectionRadius,
                nearbyRobotColliders,
                robotDetectionLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = nearbyRobotColliders[i];
                RobotAgent other = hit.GetComponentInParent<RobotAgent>();
                if (other == null || other == this)
                {
                    continue;
                }

                nearbyRobotBuffer.Add(other);
            }

            nearbyRobotBuffer.Sort((lhs, rhs) =>
                Vector3.SqrMagnitude(lhs.transform.position - transform.position)
                    .CompareTo(Vector3.SqrMagnitude(rhs.transform.position - transform.position)));
        }

        private void AddObstacleDistanceObservations(VectorSensor sensor)
        {
            Vector3 origin = transform.position + Vector3.up * 0.3f;
            AddDirectionalObstacleDistance(sensor, origin, transform.forward);
            AddDirectionalObstacleDistance(sensor, origin, -transform.forward);
            AddDirectionalObstacleDistance(sensor, origin, -transform.right);
            AddDirectionalObstacleDistance(sensor, origin, transform.right);
        }

        private void AddDirectionalObstacleDistance(VectorSensor sensor, Vector3 origin, Vector3 direction)
        {
            bool hit = Physics.SphereCast(
                origin,
                obstacleProbeRadius,
                direction,
                out RaycastHit hitInfo,
                obstacleProbeDistance,
                obstacleLayers,
                QueryTriggerInteraction.Ignore);

            float normalizedDistance = hit
                ? Mathf.Clamp01(hitInfo.distance / Mathf.Max(0.1f, obstacleProbeDistance))
                : 1f;
            sensor.AddObservation(normalizedDistance);
        }

        private void ApplyLearningRewards(Vector2 moveDirection)
        {
            RewardWeights weights = GetWeights();
            float currentDistance = Vector3.Distance(transform.position, GetCurrentLearningTarget());
            float towardTargetDelta = previousDistanceToTarget - currentDistance;
            if (towardTargetDelta > 0f)
            {
                AddRewardByCategory("Movement.ProgressTowardTarget", towardTargetDelta * weights.ProgressTowardTargetRewardPerUnit);
            }

            AddReward(-timePenaltyPerStep);

            float efficiencyDelta = EpisodeDistanceTraveled > 0.01f
                ? Mathf.Max(0f, (agent.velocity.magnitude * Time.deltaTime) - Mathf.Max(0f, towardTargetDelta))
                : 0f;
            if (efficiencyDelta > 0.001f)
            {
                AddRewardByCategory("Penalty.PathInefficiency", -weights.PathInefficiencyPenaltyPerUnit * efficiencyDelta);
            }

            if (towardTargetDelta < -0.001f && moveDirection.sqrMagnitude > 0.05f)
            {
                AddRewardByCategory("Penalty.WrongDirection", -weights.WrongDirectionPenalty);
            }

            Vector3 targetDirection = (GetCurrentLearningTarget() - transform.position).normalized;
            Vector3 velocityDirection = agent.velocity.sqrMagnitude > 0.01f ? agent.velocity.normalized : Vector3.zero;
            if (velocityDirection != Vector3.zero && Vector3.Dot(targetDirection, velocityDirection) > 0.85f)
            {
                AddRewardByCategory("Movement.EfficientPathfinding", weights.EfficientPathfindingRewardPerMove);
            }

            float velocityDelta = Mathf.Abs(agent.velocity.magnitude - previousVelocity);
            if (agent.velocity.magnitude > 0.05f && velocityDelta < 0.2f)
            {
                AddRewardByCategory("Movement.SmoothMovement", weights.SmoothMovementReward);
            }

            PopulateNearbyRobots();
            if (nearbyRobotBuffer.Count >= 2 && towardTargetDelta > 0.01f && coordinationRewardCooldown <= 0f)
            {
                AddRewardByCategory("Coordination.AvoidCongestion", weights.AvoidingCongestionReward);
                AddRewardByCategory("Coordination.CooperativePathfinding", weights.CooperativePathfindingReward);
                coordinationRewardCooldown = 0.5f;
            }

            coordinationRewardCooldown = Mathf.Max(0f, coordinationRewardCooldown - Time.deltaTime);

            if (moveDirection.sqrMagnitude < 0.01f || agent.velocity.magnitude < idleSpeedThreshold)
            {
                idleTime += Time.deltaTime;
                if (idleTime >= Mathf.Max(idleTimeoutSeconds, 5f))
                {
                    AddRewardByCategory("Penalty.ExcessiveIdle", -weights.ExcessiveIdlePenalty);
                    idleTime = 0f;
                }
            }
            else
            {
                idleTime = 0f;
            }

            previousDistanceToTarget = currentDistance;
            previousVelocity = agent.velocity.magnitude;

            trendTimer += Time.deltaTime;
            if (trendTimer >= 1f)
            {
                float currentReward = GetCumulativeReward();
                RewardTrendPerSecond = currentReward - lastTrendReward;
                lastTrendReward = currentReward;
                trendTimer = 0f;
            }
        }

        public Dictionary<string, float> GetRewardBreakdownSnapshot()
        {
            return new Dictionary<string, float>(rewardBreakdown);
        }

        private RewardWeights GetWeights()
        {
            if (rewardTuner == null)
            {
                rewardTuner = FindObjectOfType<RewardTuner>();
            }

            return rewardTuner != null ? rewardTuner.CurrentWeights : RewardWeights.CreateDefault();
        }

        private void AddRewardByCategory(string category, float amount)
        {
            AddReward(amount);
            if (rewardBreakdown.TryGetValue(category, out float value))
            {
                rewardBreakdown[category] = value + amount;
            }
            else
            {
                rewardBreakdown[category] = amount;
            }
        }

        private void ApplyLearningMovement(Vector2 moveDirection, float speedScale)
        {
            Vector3 localDirection = new Vector3(moveDirection.x, 0f, moveDirection.y);
            if (localDirection.sqrMagnitude < 0.001f)
            {
                return;
            }

            Vector3 worldDirection = transform.TransformDirection(localDirection.normalized);
            Vector3 destination = transform.position + (worldDirection * decisionStepDistance);

            if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, decisionStepDistance, NavMesh.AllAreas))
            {
                shouldPerformPickOnArrival = false;
                AssignedNodeId = -1;
                agent.SetDestination(navHit.position);
                statusTimer = 0f;
                SetStatus(RobotStatus.Moving);
            }

            agent.speed = Mathf.Max(0.1f, maxSpeed * speedScale);
        }

        private void SetStatus(RobotStatus newStatus)
        {
            Status = newStatus;
        }

        private void LogDebug(string message)
        {
            if (!debugLogging)
            {
                return;
            }

            Debug.Log($"[{name}] {message}");
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

            if (!visualizePath || agent == null || !agent.hasPath || agent.path == null)
            {
                return;
            }

            // Optional debug-only path rendering to inspect pathfinding efficiency and detours.
            Gizmos.color = pathVisualizationColor;
            Vector3[] corners = agent.path.corners;
            for (int i = 0; i < corners.Length - 1; i++)
            {
                Gizmos.DrawLine(corners[i] + Vector3.up * 0.1f, corners[i + 1] + Vector3.up * 0.1f);
            }
        }
#endif
    }
}
