using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Decorators
{
    [Serializable, AiNode(Name = "Repeater", Group = "Decorators")]
    public sealed class Repeater : DecoratorNode
    {
        [SerializeField, InputPort("Repeat Count"), Tooltip("-1 repeats forever.")]
        private int repeatCount = -1;

        [NonSerialized]
        private int _completed;

        protected override void OnEnter()
        {
            _completed = 0;
        }

        protected override NodeState OnTick()
        {
            if (Child == null)
                return NodeState.Failure;
            if (repeatCount == 0)
                return NodeState.Success;

            NodeState result = Child.Evaluate();
            if (result != NodeState.Success)
                return result;

            _completed++;
            return repeatCount < 0 || _completed < repeatCount
                ? NodeState.Running
                : NodeState.Success;
        }
    }
}
