using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Project.Scripts.Entities
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WorkerSimulatorSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnitTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            
            state.Dependency = new WorkerSimulationJob
            {
                deltaTime = deltaTime
            }.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    [WithAll(typeof(UnitTag))]
    public partial struct WorkerSimulationJob : IJobEntity
    {
        public float deltaTime;

        private void Execute(
            ref LocalTransform transform,
            ref UnitNeeds needs,
            ref UnitState state,
            ref UnitOrder order,
            in UnitStats stats
        )
        {
            UpdateNeeds(ref needs, stats);
        }

        private void UpdateNeeds(ref UnitNeeds needs,
            in UnitStats stats)
        {
            needs.food = math.saturate(
                needs.food - stats.hungerRate * deltaTime
                );
        }

        private void UpdateOrder(
            ref LocalTransform transform,
            ref UnitState state,
            ref UnitOrder order,
            in UnitStats stats
        )
        {
            if (order.hasOrder == 0)
            {
                state.value = UnitMode.Idle;
                return;
            }

            switch (state.value)
            {
                case UnitMode.MovingToTarget:
                    
                    break;
            }
        }

        private void MoveTowardDestination(
            ref LocalTransform transform,
            ref UnitState state,
            in UnitOrder order,
            float moveSpeed)
        {
            float3 offset = order.destination - transform.Position;

            offset.z = 0f;

            float distanceSquared = math.lengthsq(offset);
            const float arrivalDistanceSquared = 0.01f;

            if (distanceSquared < arrivalDistanceSquared)
            {
                transform.Position = order.destination;
                state.value = UnitMode.Working;
                return;
            }
            
            float distance = math.sqrt(distanceSquared);
            float movement = math.min(moveSpeed * deltaTime, distance);
            
            transform.Position += offset * (movement / distance);
        }

        private void PerformAction(
            ref UnitState state,
            ref UnitOrder order,
            float workRate)
        {
            order.workRemaining = math.max(0f, order.workRemaining - workRate * deltaTime);

            if (order.workRemaining > 0f)
                return;
            
            order.hasOrder = 0;
            order.target = Entity.Null;
            state.value = UnitMode.Idle;
        }
    }
}