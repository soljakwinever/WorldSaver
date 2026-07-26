using System;
using System.Collections.Generic;
using Project.Scripts.AI.GraphEditor;

namespace Project.Scripts.AI.Composite
{
    [Serializable]
    [AiNode(Name = "Selector", Group = "Composites")]
    public class Selector : Composite
    {
        public Selector()
        {
        }

        public Selector(List<AiNode> children) : base(children)
        {
        }

        protected override NodeState OnTick()
        {
            foreach (AiNode child in children)
            {
                NodeState result = child.Evaluate();
                
                if(result == NodeState.Success)
                    return NodeState.Success;
                
                if(result == NodeState.Running) 
                    return NodeState.Running;
            }
            
            return NodeState.Failure;
        }
    }
}
