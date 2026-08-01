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
    public sealed class DestroyNodeToolAction :
        ToolAction,
        IRequiresToolProximity
    {
        [SerializeField, Min(0f)] private float interactionRange = 1.75f;

        [SerializeField]
        [Tooltip("Specific node assets this action can destroy.")]
        private NodeData[] targetNodes = Array.Empty<NodeData>();

        [SerializeField]
        [Tooltip("Node tags this action can destroy.")]
        private EntityTag[] targetTags = Array.Empty<EntityTag>();

        public override bool CanPerform(ToolActionContext context)
        {
            return TryGetTarget(context, out _, out _);
        }

        public bool IsWithinToolRange(ToolActionContext context)
        {
            if (!TryGetTarget(context, out _, out Node node))
                return false;

            return ((Vector2)context.User.transform.position -
                    (Vector2)node.transform.position).sqrMagnitude <=
                   interactionRange * interactionRange;
        }

        public override bool Perform(ToolActionContext context)
        {
            if (!TryGetTarget(
                    context,
                    out PersistentEntity entity,
                    out Node node))
                return false;

            NodeData nodeData = node.NodeData;
            Vector3 dropPosition = node.transform.position;

            EntityDamageReceiver receiver =
                node.GetComponent<EntityDamageReceiver>();
            if (receiver != null)
            {
                return receiver.TakeDamage(
                    CreateAttackContext(context)) > 0;
            }

            entity.RemoveFromWorld();
            TrySpawnDrop(context, nodeData, dropPosition);
            return true;
        }

        private bool TryGetTarget(
            ToolActionContext context,
            out PersistentEntity entity,
            out Node node)
        {
            entity = null;
            node = null;
            if (context.User == null)
                return false;

            // Nodes are pooled and positioned through their transforms while
            // Physics2D auto-sync is disabled. Ensure their colliders are at
            // their current world positions before performing the query.
            Physics2D.SyncTransforms();
            Collider2D[] colliders =
                Physics2D.OverlapPointAll(context.TargetPosition);
            foreach (Collider2D candidate in colliders)
            {
                Node candidateNode = candidate.GetComponentInParent<Node>();
                if (candidateNode == null || !CanDestroy(candidateNode.NodeData))
                    continue;

                PersistentEntity persistentEntity =
                    candidateNode.GetComponent<PersistentEntity>();
                if (persistentEntity == null ||
                    !persistentEntity.CanRemoveFromWorld)
                {
                    continue;
                }

                EntityDamageReceiver receiver =
                    candidateNode.GetComponent<EntityDamageReceiver>();
                if (receiver != null &&
                    !receiver.CanReceiveDamage(
                        CreateAttackContext(context)))
                {
                    continue;
                }

                entity = persistentEntity;
                node = candidateNode;
                return true;
            }

            return false;
        }

        private static AttackContext CreateAttackContext(
            ToolActionContext context)
        {
            return new AttackContext(
                context.User,
                context.Tool,
                Mathf.Max(0, context.Tool?.Power ?? 0),
                EntityDamageSource.Tool,
                context.Tool?.DamageTags);
        }

        private static void TrySpawnDrop(
            ToolActionContext context,
            NodeData nodeData,
            Vector3 position)
        {
            if (context.SpawnItemDrop == null ||
                nodeData?.droppedItem == null ||
                nodeData.dropChance <= 0f ||
                (nodeData.dropChance < 1f &&
                 UnityEngine.Random.value >= nodeData.dropChance))
            {
                return;
            }

            context.SpawnItemDrop(nodeData.droppedItem, position);
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
