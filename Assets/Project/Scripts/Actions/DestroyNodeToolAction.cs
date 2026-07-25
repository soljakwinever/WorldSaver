using System;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>
    /// Removes persistent nodes that match an explicit node asset or entity tag.
    /// </summary>
    [CreateAssetMenu(
        fileName = "New Destroy Node Tool Action",
        menuName = "Data/Tool Actions/Destroy Node")]
    public sealed class DestroyNodeToolAction : ToolAction
    {
        [SerializeField]
        [Tooltip("Specific node assets this action can destroy.")]
        private NodeData[] targetNodes = Array.Empty<NodeData>();

        [SerializeField]
        [Tooltip("Node tags this action can destroy.")]
        private EntityTag[] targetTags = Array.Empty<EntityTag>();

        public override bool CanPerform(ToolActionContext context)
        {
            return TryGetTarget(context, out _);
        }

        public override bool Perform(ToolActionContext context)
        {
            if (!TryGetTarget(context, out PersistentEntity entity))
                return false;

            entity.RemoveFromWorld();
            return true;
        }

        private bool TryGetTarget(
            ToolActionContext context,
            out PersistentEntity entity)
        {
            entity = null;
            if (context.User == null)
                return false;

            Collider2D[] colliders =
                Physics2D.OverlapPointAll(context.TargetPosition);
            foreach (Collider2D candidate in colliders)
            {
                Node node = candidate.GetComponentInParent<Node>();
                if (node == null || !CanDestroy(node.NodeData))
                    continue;

                PersistentEntity persistentEntity =
                    node.GetComponent<PersistentEntity>();
                if (persistentEntity == null ||
                    !persistentEntity.CanRemoveFromWorld)
                {
                    continue;
                }

                entity = persistentEntity;
                return true;
            }

            return false;
        }

        private bool CanDestroy(NodeData nodeData)
        {
            if (nodeData == null)
                return false;
            bool filtersNodes = targetNodes != null && targetNodes.Length > 0;
            bool filtersTags = targetTags != null && targetTags.Length > 0;
            if (!filtersNodes && !filtersTags)
                return true;

            if (filtersNodes)
            {
                foreach (NodeData targetNode in targetNodes)
                {
                    if (targetNode == nodeData)
                        return true;
                }
            }

            // Node and tag lists are alternatives: a match in either is enough.
            if (filtersTags)
            {
                foreach (EntityTag targetTag in targetTags)
                {
                    if (nodeData.HasTag(targetTag))
                        return true;
                }
            }

            return false;
        }
    }
}
