using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Decorators
{
    [Serializable, AiNode(Name = "Blackboard Gate", Group = "Decorators")]
    public sealed class BlackboardGate : DecoratorNode
    {
        [SerializeField, InputPort("Key")]
        private AiKeys.Key key;

        [SerializeField, InputPort("Condition")]
        private BlackboardCondition condition = BlackboardCondition.HasValue;

        [SerializeField, InputPort("Comparison Key")]
        private AiKeys.Key comparisonKey;

        [SerializeField, InputPort("Bool Value")]
        private bool boolValue = true;

        [SerializeField, InputPort("Float Value")]
        private float floatValue;

        [SerializeField, InputPort("Integer Value")]
        private int integerValue;

        [SerializeField, InputPort("Enum Value")]
        private string enumValue;

        [SerializeField, InputPort("Object Value")]
        private UnityEngine.Object objectValue;

        protected override NodeState OnTick()
        {
            if (Child == null)
                return NodeState.Failure;

            return BlackboardConditionEvaluator.Evaluate(
                Blackboard,
                key,
                condition,
                comparisonKey,
                boolValue,
                floatValue,
                integerValue,
                enumValue,
                objectValue)
                ? Child.Evaluate()
                : NodeState.Failure;
        }
    }
}
