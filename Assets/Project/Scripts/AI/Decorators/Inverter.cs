using System;
using Project.Scripts.AI.GraphEditor;

namespace Project.Scripts.AI.Decorators
{
    [Serializable, AiNode(nameof(Inverter), nameof(Decorators))]
    public class Inverter : DecoratorNode
    {
        protected override NodeState OnTick()
        {
            var childState = Child.Evaluate();
            
            if(childState == NodeState.Running)
                return NodeState.Running;

            return Decorate(childState);
        }

        protected override NodeState Decorate(NodeState currentState)
        {
            return currentState == NodeState.Success ? NodeState.Failure : NodeState.Success;
        }
    }
}