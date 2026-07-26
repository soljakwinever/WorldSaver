using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Interface
{
    [CreateAssetMenu(fileName = "All Condition", menuName = "World/Conditions/All")]
    public sealed class AllConditionDefinition : CompositeConditionDefinition
    {
        protected override IOperationCondition BuildCondition(
            GameObject host,
            HashSet<EntityConditionDefinition> buildPath)
        {
            return new AllCondition(BuildChildren(host, buildPath));
        }
    }
}
