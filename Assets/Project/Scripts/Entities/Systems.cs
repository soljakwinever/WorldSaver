using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Project.Scripts.Entities
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct VillagerNeedsSystem : ISystem
    {
        public void OnCreate(ref SystemState state) =>
            state.RequireForUpdate<VillagerTag>();

        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            foreach (var (needs, stats, villagerState) in
                     SystemAPI.Query<RefRW<VillagerNeeds>, RefRO<VillagerStats>, RefRW<VillagerState>>()
                         .WithAll<VillagerTag>())
            {
                if (villagerState.ValueRO.mode == VillagerMode.Dead)
                    continue;

                VillagerNeeds value = needs.ValueRO;
                VillagerStats configuration = stats.ValueRO;
                value.hunger = math.clamp(
                    value.hunger - configuration.hungerDrainPerSecond * deltaTime,
                    0f,
                    configuration.maxHunger);

                bool moving = villagerState.ValueRO.mode == VillagerMode.MovingToTarget;
                bool working = villagerState.ValueRO.mode == VillagerMode.Working;
                bool sleeping = villagerState.ValueRO.mode is
                    VillagerMode.Sleeping or VillagerMode.NightSleeping;
                float energyDelta = sleeping
                    ? configuration.restEnergyPerSecond
                    : -(moving ? configuration.movementEnergyPerSecond : 0f) -
                      (working ? configuration.workEnergyPerSecond : 0f);
                value.energy = math.clamp(
                    value.energy + energyDelta * deltaTime,
                    0f,
                    configuration.maxEnergy);

                if (value.hunger > 0f && value.energy > 0f)
                {
                    value.mana = math.clamp(
                        value.mana + configuration.manaRegenerationPerSecond * deltaTime,
                        0f,
                        configuration.maxMana);
                    if (value.health < configuration.maxHealth)
                        value.pendingHealthDelta +=
                            configuration.healthRegenerationPerSecond * deltaTime;
                }
                else if (value.hunger <= 0f)
                    value.pendingHealthDelta -=
                        configuration.starvationDamagePerSecond * deltaTime;

                int wholeDelta = value.pendingHealthDelta >= 1f
                    ? (int)math.floor(value.pendingHealthDelta)
                    : value.pendingHealthDelta <= -1f
                        ? (int)math.ceil(value.pendingHealthDelta)
                        : 0;
                if (wholeDelta != 0)
                {
                    value.health = math.clamp(
                        value.health + wholeDelta,
                        0,
                        configuration.maxHealth);
                    value.pendingHealthDelta -= wholeDelta;
                }

                if (value.health <= 0)
                    villagerState.ValueRW.mode = VillagerMode.Dead;
                needs.ValueRW = value;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VillagerNeedsSystem))]
    public partial struct VillagerJobAssignmentSystem : ISystem
    {
        private EntityQuery _jobs;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VillagerTag>();
            _jobs = state.GetEntityQuery(
                ComponentType.ReadOnly<TownJobTag>(),
                ComponentType.ReadWrite<TownJob>());
        }

        public void OnUpdate(ref SystemState state)
        {
            EntityManager manager = state.EntityManager;
            NativeArray<Entity> jobEntities =
                _jobs.ToEntityArray(Allocator.Temp);
            NativeArray<TownJob> jobs =
                _jobs.ToComponentDataArray<TownJob>(Allocator.Temp);

            foreach (var (identity, assignment, villagerState, order, transform, villagerEntity) in
                     SystemAPI.Query<RefRO<VillagerIdentity>, RefRO<VillagerAssignment>,
                             RefRW<VillagerState>, RefRW<VillagerOrder>, RefRO<LocalTransform>>()
                         .WithAll<VillagerTag>()
                         .WithEntityAccess())
            {
                if (villagerState.ValueRO.mode != VillagerMode.Idle ||
                    villagerState.ValueRO.activeJobEntity != Entity.Null)
                    continue;

                int best = -1;
                int bestPriority = -1;
                float bestDistance = float.PositiveInfinity;
                long bestId = long.MaxValue;
                for (int i = 0; i < jobs.Length; i++)
                {
                    TownJob candidate = jobs[i];
                    if (candidate.status != TownJobStatus.Queued ||
                        candidate.priority == TownJobPriority.Off ||
                        candidate.townId != identity.ValueRO.townId ||
                        !Allows(assignment.ValueRO.allowedJobs, candidate.type))
                        continue;

                    int priority = (int)candidate.priority +
                                   RoleBonus(assignment.ValueRO.role, candidate.type);
                    float distance = math.distancesq(
                        transform.ValueRO.Position,
                        candidate.targetPosition);
                    if (priority < bestPriority ||
                        priority == bestPriority && distance > bestDistance ||
                        priority == bestPriority && distance == bestDistance &&
                        candidate.jobId >= bestId)
                        continue;

                    best = i;
                    bestPriority = priority;
                    bestDistance = distance;
                    bestId = candidate.jobId;
                }

                if (best < 0)
                    continue;

                TownJob selected = jobs[best];
                selected.status = TownJobStatus.Claimed;
                selected.claimedBy = villagerEntity;
                manager.SetComponentData(jobEntities[best], selected);
                jobs[best] = selected;

                villagerState.ValueRW.activeJobId = selected.jobId;
                villagerState.ValueRW.activeJobEntity = jobEntities[best];
                villagerState.ValueRW.mode = VillagerMode.MovingToTarget;
                order.ValueRW.destination = selected.targetPosition;
                order.ValueRW.workRemaining = math.max(0f, selected.workRequired);
                order.ValueRW.hasOrder = 1;
            }

            jobs.Dispose();
            jobEntities.Dispose();
        }

        public static bool Allows(VillagerJobMask mask, VillagerJobType type)
        {
            VillagerJobMask required = type switch
            {
                VillagerJobType.Gather => VillagerJobMask.Gather,
                VillagerJobType.Hunt => VillagerJobMask.Hunt,
                VillagerJobType.Build => VillagerJobMask.Build,
                VillagerJobType.Craft => VillagerJobMask.Craft,
                VillagerJobType.Defend => VillagerJobMask.Defend,
                VillagerJobType.Farm => VillagerJobMask.Farm,
                _ => VillagerJobMask.None
            };
            return required != VillagerJobMask.None && (mask & required) != 0;
        }

        private static int RoleBonus(VillagerRole role, VillagerJobType type) =>
            role != VillagerRole.Generalist && (int)role == (int)type ? 1 : 0;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VillagerJobAssignmentSystem))]
    public partial struct VillagerWorkSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            ComponentLookup<TownJob> jobs =
                SystemAPI.GetComponentLookup<TownJob>();

            foreach (var (transform, needs, stats, villagerState, order,
                         pathState, waypoints) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<VillagerNeeds>,
                         RefRO<VillagerStats>, RefRW<VillagerState>, RefRW<VillagerOrder>,
                         RefRW<VillagerPathState>, DynamicBuffer<VillagerWaypoint>>()
                         .WithAll<VillagerTag>())
            {
                if (villagerState.ValueRO.mode == VillagerMode.Dead)
                {
                    waypoints.Clear();
                    pathState.ValueRW = default;
                    ReleaseJob(ref jobs, ref villagerState.ValueRW, ref order.ValueRW);
                    continue;
                }

                float critical = math.saturate(stats.ValueRO.criticalNeedFraction);
                bool hungry = needs.ValueRO.hunger <= stats.ValueRO.maxHunger * critical;
                bool exhausted = needs.ValueRO.energy <= stats.ValueRO.maxEnergy * critical;
                if ((hungry || exhausted) && villagerState.ValueRO.activeJobEntity != Entity.Null)
                {
                    waypoints.Clear();
                    pathState.ValueRW = default;
                    ReleaseJob(ref jobs, ref villagerState.ValueRW, ref order.ValueRW);
                    villagerState.ValueRW.mode = hungry
                        ? VillagerMode.Eating
                        : VillagerMode.Sleeping;
                    continue;
                }

                if (villagerState.ValueRO.mode == VillagerMode.Eating)
                {
                    if (!hungry)
                        villagerState.ValueRW.mode = VillagerMode.Idle;
                    continue;
                }
                if (villagerState.ValueRO.mode == VillagerMode.Sleeping)
                {
                    if (needs.ValueRO.energy >= stats.ValueRO.maxEnergy * 0.9f)
                        villagerState.ValueRW.mode = VillagerMode.Idle;
                    continue;
                }

                if (villagerState.ValueRO.mode == VillagerMode.MovingToTarget ||
                    villagerState.ValueRO.mode == VillagerMode.MovingToSleep)
                {
                    if (pathState.ValueRO.requestPending != 0 ||
                        pathState.ValueRO.pathFailed != 0 ||
                        waypoints.Length == 0)
                        continue;

                    while (pathState.ValueRO.waypointIndex < waypoints.Length &&
                           math.distancesq(
                               transform.ValueRO.Position,
                               waypoints[pathState.ValueRO.waypointIndex].value) <= 0.01f)
                    {
                        pathState.ValueRW.waypointIndex++;
                    }

                    if (pathState.ValueRO.waypointIndex >= waypoints.Length)
                    {
                        transform.ValueRW.Position = order.ValueRO.destination;
                        villagerState.ValueRW.mode =
                            villagerState.ValueRO.mode == VillagerMode.MovingToSleep
                                ? VillagerMode.NightSleeping
                                : VillagerMode.Working;
                        continue;
                    }

                    float3 waypoint =
                        waypoints[pathState.ValueRO.waypointIndex].value;
                    float3 offset = waypoint - transform.ValueRO.Position;
                    offset.z = 0f;
                    float distance = math.length(offset);
                    if (distance > 0f)
                    {
                        float movement = math.min(
                            stats.ValueRO.movementSpeed * deltaTime,
                            distance);
                        transform.ValueRW.Position += offset * (movement / distance);
                    }
                }
                else if (villagerState.ValueRO.mode == VillagerMode.Working)
                {
                    order.ValueRW.workRemaining = math.max(
                        0f,
                        order.ValueRO.workRemaining - stats.ValueRO.workRate * deltaTime);
                    if (order.ValueRO.workRemaining <= 0f)
                    {
                        villagerState.ValueRW.mode = VillagerMode.AwaitingWorldCommit;
                        Entity jobEntity = villagerState.ValueRO.activeJobEntity;
                        if (jobEntity != Entity.Null && jobs.HasComponent(jobEntity))
                        {
                            TownJob job = jobs[jobEntity];
                            job.status = TownJobStatus.AwaitingWorldCommit;
                            jobs[jobEntity] = job;
                        }
                    }
                }
            }
        }

        private static void ReleaseJob(
            ref ComponentLookup<TownJob> jobs,
            ref VillagerState state,
            ref VillagerOrder order)
        {
            if (state.activeJobEntity != Entity.Null &&
                jobs.HasComponent(state.activeJobEntity))
            {
                TownJob job = jobs[state.activeJobEntity];
                if (job.status == TownJobStatus.Claimed ||
                    job.status == TownJobStatus.AwaitingWorldCommit)
                {
                    job.status = TownJobStatus.Queued;
                    job.claimedBy = Entity.Null;
                    jobs[state.activeJobEntity] = job;
                }
            }
            state.activeJobEntity = Entity.Null;
            state.activeJobId = 0;
            order.hasOrder = 0;
            order.workRemaining = 0f;
        }
    }
}
