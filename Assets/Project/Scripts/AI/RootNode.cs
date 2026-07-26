using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.AI
{
    [Serializable]
    public class RootNode : AiNode
    {
        [SerializeReference, ManagedReferenceSelector(typeof(AiNode))]
        private AiNode firstChild;

        public RootNode()
        {
        }

        public RootNode(AiNode firstChild)
        {
            this.firstChild = firstChild;
        }

        protected override IEnumerable<AiNode> Children
        {
            get
            {
                if (firstChild != null)
                    yield return firstChild;
            }
        }
        
        protected override NodeState OnTick()
        {
            if (firstChild == null)
                return state = NodeState.Failure;

            state = firstChild.Evaluate();
            return state;
        }
    }
}
