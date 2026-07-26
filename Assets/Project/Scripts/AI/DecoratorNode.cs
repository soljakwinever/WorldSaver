using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.AI
{
    [Serializable]
    public abstract class DecoratorNode : AiNode
    {
        [SerializeReference, ManagedReferenceSelector(typeof(AiNode))]
        [Project.Scripts.AI.GraphEditor.OutputPort("Child")]
        private AiNode child;

        protected DecoratorNode()
        {
        }

        protected DecoratorNode(AiNode child)
        {
            this.child = child ?? throw new System.ArgumentNullException(nameof(child));
        }

        protected AiNode Child => child;

        protected override IEnumerable<AiNode> Children
        {
            get
            {
                if (child != null)
                    yield return child;
            }
        }

        protected override NodeState OnTick()
        {
            if (child == null)
                return state = NodeState.Failure;

            NodeState childState = child.Evaluate();

            state = Decorate(childState);
            return state;
        }
        
        protected virtual NodeState Decorate(NodeState currentState)
        {
            return currentState;
        }
    }
}
