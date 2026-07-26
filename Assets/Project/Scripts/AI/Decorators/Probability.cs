using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Decorators
{
    [Serializable, AiNode(Name = "Probability", Group = "Decorators")]
    public sealed class Probability : DecoratorNode
    {
        [SerializeField, Range(0f, 1f), InputPort("Probability")]
        private float probability = 0.5f;

        [NonSerialized]
        private bool _allowed;

        protected override void OnEnter()
        {
            _allowed = UnityEngine.Random.value <= probability;
        }

        protected override NodeState OnTick()
        {
            if (!_allowed || Child == null)
                return NodeState.Failure;

            return Child.Evaluate();
        }
    }
}
