using System;
using Project.Scripts.AI.GraphEditor;

namespace Project.Scripts.AI.Decorators
{
    [Serializable, AiNode(Name = "Failer", Group = "Decorators")]
    public sealed class Failer : DecoratorNode
    {
        protected override NodeState Decorate(NodeState currentState)
        {
            return currentState == NodeState.Running
                ? NodeState.Running
                : NodeState.Failure;
        }
    }
}
