using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Decorators
{
    [Serializable, AiNode(Name = "Force State", Group = "Decorators")]
    public sealed class ForceState : DecoratorNode
    {
        [SerializeField, InputPort("State")]
        private NodeState forcedState = NodeState.Success;

        protected override NodeState Decorate(NodeState currentState)
        {
            return currentState == NodeState.Running
                ? NodeState.Running
                : forcedState;
        }
    }
}
