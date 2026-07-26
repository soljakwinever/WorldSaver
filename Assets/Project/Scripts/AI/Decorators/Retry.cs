using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Decorators
{
    [Serializable, AiNode(Name = "Retry", Group = "Decorators")]
    public sealed class Retry : DecoratorNode
    {
        [SerializeField, Min(0), InputPort("Retries")]
        private int retries = 1;

        [NonSerialized]
        private int _failedAttempts;

        protected override void OnEnter()
        {
            _failedAttempts = 0;
        }

        protected override NodeState OnTick()
        {
            if (Child == null)
                return NodeState.Failure;

            NodeState result = Child.Evaluate();
            if (result != NodeState.Failure)
                return result;

            if (_failedAttempts >= retries)
                return NodeState.Failure;

            _failedAttempts++;
            return NodeState.Running;
        }
    }
}
