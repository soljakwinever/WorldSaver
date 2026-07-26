using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Decorators
{
    [Serializable, AiNode(Name = "Timeout", Group = "Decorators")]
    public sealed class Timeout : DecoratorNode
    {
        [SerializeField, Min(0f), InputPort("Duration")]
        private float duration = 1f;

        [NonSerialized]
        private float _elapsed;

        protected override void OnEnter()
        {
            _elapsed = 0f;
        }

        protected override NodeState OnTick()
        {
            if (Child == null)
                return NodeState.Failure;

            _elapsed += Time.deltaTime;
            if (_elapsed > duration)
            {
                Child.Abort();
                return NodeState.Failure;
            }

            return Child.Evaluate();
        }
    }
}
