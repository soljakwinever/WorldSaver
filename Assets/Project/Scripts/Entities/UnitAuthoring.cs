using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Project.Scripts.Entities
{
    public sealed class UnitAuthoring : MonoBehaviour
    {
        [Min(0f)] public float moveSpeed = 3f;

        [Min(0f)] public float workRate = 1f;

        [Min(0f)] public float hungerPerSecond = 0.002f;

        private sealed class Baker : Baker<UnitAuthoring>
        {
            public override void Bake(UnitAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                
                AddComponent<UnitTag>(entity);
                
                const int inventorySize = 6;
                
                AddComponent(entity, new UnitStats()
                {
                    movementSpeed = authoring.moveSpeed,
                    workRate = authoring.workRate,
                    hungerRate = authoring.hungerPerSecond,
                    inventorySize = inventorySize
                });

                AddBuffer<UnitInventoryBuffer>(entity);
                
                AddComponent(entity, new UnitNeeds()
                {
                    food = 1,
                    health = 100,
                    energy = 1
                });
                
                AddComponent(entity, new UnitState()
                {
                    value = UnitMode.Idle
                });
                
                AddComponent(entity, new UnitOrder()
                {
                    target = Entity.Null,
                    destination = float3.zero,
                    workRemaining = 0,
                    hasOrder = 0
                });
            }
        }
    }
}