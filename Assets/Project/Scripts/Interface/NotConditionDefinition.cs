using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Interface
{
    [CreateAssetMenu(fileName = "Not Condition", menuName = "World/Conditions/Not")]
    public sealed class NotConditionDefinition : EntityConditionDefinition
    {
        [SerializeField] private EntityConditionDefinition condition;

        protected override IOperationCondition BuildCondition(
            GameObject host,
            HashSet<EntityConditionDefinition> buildPath)
        {
            if (condition == null)
                throw new InvalidOperationException(
                    $"Not condition '{name}' requires a child.");

            IOperationCondition child = condition.Build(host, buildPath);
            return new NotCondition(child);
        }
    }
}
