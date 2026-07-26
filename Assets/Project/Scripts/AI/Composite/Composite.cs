using System;
using System.Collections.Generic;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.AI.Composite
{
    [Serializable]
    public abstract class Composite : AiNode
    {
        [SerializeReference, ManagedReferenceSelector(typeof(AiNode))]
        [OutputPort("Children")]
        protected List<AiNode> children = new List<AiNode>();

        protected Composite()
        {
        }

        protected Composite(List<AiNode> children)
        {
            this.children = children ?? throw new ArgumentNullException(
                nameof(children));
        }

        protected override IEnumerable<AiNode> Children => children;

        protected void AbortChildrenExcept(int activeIndex)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (i != activeIndex)
                    children[i]?.Abort();
            }
        }
    }
}
