using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Interface
{
    [CreateAssetMenu(fileName = "Any Condition", menuName = "World/Conditions/Any")]
    public sealed class AnyConditionDefinition : CompositeConditionDefinition
    {
        protected override IOperationCondition BuildCondition(
            GameObject host,
            HashSet<EntityConditionDefinition> buildPath)
        {
            return new AnyCondition(BuildChildren(host, buildPath));
        }
    }
}
