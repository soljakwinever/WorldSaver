using System;
using Project.Scripts.AI.GraphEditor;

namespace Project.Scripts.AI.Decorators
{
    [Serializable, AiNode(Name = "Succeeder", Group = "Decorators")]
    public sealed class Succeeder : DecoratorNode
    {
        protected override NodeState Decorate(NodeState currentState)
        {
            return currentState == NodeState.Running
                ? NodeState.Running
                : NodeState.Success;
        }
    }
}
