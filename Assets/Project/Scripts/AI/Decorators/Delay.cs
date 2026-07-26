using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Decorators
{
    [Serializable, AiNode(Name = "Delay", Group = "Decorators")]
    public sealed class Delay : DecoratorNode
    {
        [SerializeField, Min(0f), InputPort("Duration")]
        private float duration;

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
            return _elapsed < duration
                ? NodeState.Running
                : Child.Evaluate();
        }
    }
}
