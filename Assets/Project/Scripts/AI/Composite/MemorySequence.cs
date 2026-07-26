using System;
using System.Collections.Generic;
using Project.Scripts.AI.GraphEditor;

namespace Project.Scripts.AI.Composite
{
    [Serializable]
    [AiNode(Name = "Memory Sequence", Group = "Composites")]
    public class MemorySequence : Composite
    {
        protected int LastNode { get; private set; }

        public MemorySequence()
        {
        }

        public MemorySequence(List<AiNode> children) : base(children)
        {
        }

        protected override NodeState OnTick()
        {
            for (int i = LastNode; i < children.Count; i++)
            {
                NodeState childState = children[i].Evaluate();
                
                switch (childState)
                {
                    case NodeState.Failure:
                        state = NodeState.Failure;
                        LastNode = 0;
                        return state;
                    case NodeState.Running:
                        state = NodeState.Running;
                        LastNode = i;
                        return state;
                    case NodeState.Success:
                        continue;
                    
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(childState),
                            childState,
                            "Unknown node state.");
                }
            }
            
            LastNode = 0;
            state = NodeState.Success;
            return state;
        }

        protected override void OnAbort()
        {
            LastNode = 0;
        }
    }
}
