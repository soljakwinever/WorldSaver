using System;
using System.Collections.Generic;
using Project.Scripts.AI.GraphEditor;

namespace Project.Scripts.AI.Composite
{
    [Serializable]
    [AiNode(Name = "Sequence", Group = "Composites")]
    public class SequenceNode : Composite
    {
        public SequenceNode()
        {
        }
        
        public SequenceNode(List<AiNode> children) : base(children)
        {
        }
        
        protected override NodeState OnTick()
        {
            bool anyChildRunning = false;

            foreach (AiNode child in children)
            {
                switch (child.Evaluate())
                {
                    case NodeState.Failure:
                        state = NodeState.Failure;
                        return state;
                    case NodeState.Success:
                        continue;
                    case NodeState.Running:
                        anyChildRunning = true;
                        continue;
                }
            }
            
            state = anyChildRunning ? NodeState.Running : NodeState.Success;
            return state;
        }
    }
}
