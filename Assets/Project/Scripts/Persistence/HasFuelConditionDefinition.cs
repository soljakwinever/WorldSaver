using System;
using System.Collections.Generic;
using Project.Scripts.Interface;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(fileName = "Has Fuel Condition", menuName = "World/Conditions/Has Fuel")]
    public sealed class HasFuelConditionDefinition : EntityConditionDefinition
    {
        protected override IOperationCondition BuildCondition(
            GameObject host,
            HashSet<EntityConditionDefinition> buildPath)
        {
            InventoryFuelConsumer fuel = host.GetComponent<InventoryFuelConsumer>();
            return fuel != null
                ? fuel
                : throw new InvalidOperationException(
                    "HasFuel requires an InventoryFuelConsumer on the same host.");
        }
    }
}
