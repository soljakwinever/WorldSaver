using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [AiNode("Has Offscreen Retreat Request", "Sensors"), Serializable]
    public sealed class HasOffscreenRetreatRequest : AiNode
    {
        protected override NodeState OnTick()
        {
            if (!Blackboard.TryGet(AiKeys.Self, out GameObject self) ||
                self == null)
                return state = NodeState.Failure;
            IOffscreenRetreatState retreat =
                self.GetComponent<IOffscreenRetreatState>();
            if (retreat?.ShouldRetreatOffscreen != true)
                return state = NodeState.Failure;
            Blackboard.Set<Transform>(AiKeys.Target, null);
            return state = NodeState.Success;
        }
    }

    [AiNode("Is Object Nearby", "Sensors"), Serializable]
    public class DetectNearby : AiNode
    {
        [InputPort("Distance"), SerializeField]
        private float distance;
        
        [InputPort("Layer Mask"), SerializeField]
        private LayerMask layerMask;

        [InputPort("Tag"), SerializeField] private string tag;

        [SerializeField, Tooltip("Also detect EnemyRuntime entities using the entity-tag filters below.")]
        private bool includeEnemyEntities;
        [SerializeField] private EntityTag[] requiredEntityTags =
            Array.Empty<EntityTag>();
        [SerializeField] private EntityTag[] excludedEntityTags =
            Array.Empty<EntityTag>();
        
        [NonSerialized]
        private readonly Collider2D[] _results = new Collider2D[16];
            
        protected override NodeState OnTick()
        {
            if (!Blackboard.TryGet(AiKeys.Self, out GameObject self) ||
                self == null)
                throw new InvalidOperationException();
            ISwallowOccupancyState occupancy =
                self.GetComponent<ISwallowOccupancyState>();
            if (occupancy != null && occupancy.IsFull)
            {
                Blackboard.Set<Transform>(AiKeys.Target, null);
                return state = NodeState.Failure;
            }

            float retentionDistance = Mathf.Max(
                Mathf.Max(0f, distance),
                Blackboard.GetOrDefault(AiKeys.TargetRetentionDistance));
            Transform retainedTarget =
                Blackboard.GetOrDefault(AiKeys.Target);
            if (retainedTarget != null &&
                IsMatchingTarget(retainedTarget.gameObject) &&
                (retainedTarget.position - self.transform.position)
                    .sqrMagnitude <= retentionDistance * retentionDistance)
            {
                return state = NodeState.Success;
            }

            int resultCount = Physics2D.OverlapCircleNonAlloc(self.transform.position, distance, _results, layerMask);
            GameObject nearest = null;
            float nearestDistanceSquared = float.PositiveInfinity;

            for(int i = 0; i < resultCount; i++)
            {
                Collider2D candidate = _results[i];
                
                if(candidate == null) continue;
                
                // Ignore colliders belonging to this entity.
                if (candidate.transform == self.transform ||
                    candidate.transform.IsChildOf(self.transform))
                {
                    continue;
                }
                
                GameObject candidateObject =
                    candidate.attachedRigidbody != null
                        ? candidate.attachedRigidbody.gameObject
                        : candidate.gameObject;
                IEntityTagProvider entity = FindEntityProvider(candidateObject);
                if (entity is MonoBehaviour entityBehaviour)
                    candidateObject = entityBehaviour.gameObject;

                if (!IsMatchingTarget(candidateObject))
                    continue;

                float candidateDistanceSquared =
                    (candidateObject.transform.position -
                     self.transform.position).sqrMagnitude;
                if (candidateDistanceSquared >= nearestDistanceSquared)
                    continue;

                nearest = candidateObject;
                nearestDistanceSquared = candidateDistanceSquared;
            }

            if (string.Equals(tag, "Player", StringComparison.Ordinal))
            {
                foreach (IEnemyTarget registered in EnemyTargetRegistry.All)
                {
                    GameObject candidateObject = registered?.TargetObject;
                    if (candidateObject == null ||
                        !candidateObject.activeInHierarchy ||
                        !IsMatchingTarget(candidateObject))
                        continue;
                    float candidateDistanceSquared =
                        (candidateObject.transform.position - self.transform.position).sqrMagnitude;
                    if (candidateDistanceSquared > distance * distance ||
                        candidateDistanceSquared >= nearestDistanceSquared)
                        continue;
                    nearest = candidateObject;
                    nearestDistanceSquared = candidateDistanceSquared;
                }
            }

            if (nearest != null)
            {
                Blackboard.Set<Transform>(
                    AiKeys.Target,
                    nearest.transform);
                return state = NodeState.Success;
            }

            Blackboard.Set<Transform>(AiKeys.Target, null);
            state = NodeState.Failure;
            return state;
        }

        private bool IsMatchingTarget(GameObject candidate)
        {
            if (candidate == null)
                return false;
            MonoBehaviour[] targetBehaviours =
                candidate.GetComponentsInParent<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in targetBehaviours)
                if (behaviour is ITargetableState targetable &&
                    !targetable.CanBeTargeted)
                    return false;
            if (string.IsNullOrWhiteSpace(tag) || candidate.CompareTag(tag))
                return true;

            IEntityTagProvider entity = FindEntityProvider(candidate);
            if (includeEnemyEntities && entity != null)
                return MatchesEntityTags(entity);

            // Existing hostile trees search for the Player tag. Villager ECS
            // bridges deliberately retain the NPC tag but opt into the same
            // enemy target group through this marker.
            if (!string.Equals(tag, "Player", StringComparison.Ordinal))
                return false;

            foreach (MonoBehaviour behaviour in targetBehaviours)
                if (behaviour is Project.Scripts.Interface.IEnemyTarget)
                    return true;
            return false;
        }

        private bool MatchesEntityTags(IEntityTagProvider provider)
        {
            System.Collections.Generic.IReadOnlyList<EntityTag> tags =
                provider?.EntityTags ?? Array.Empty<EntityTag>();
            foreach (EntityTag excluded in excludedEntityTags ??
                         Array.Empty<EntityTag>())
                if (excluded != null && Contains(tags, excluded))
                    return false;
            foreach (EntityTag required in requiredEntityTags ??
                         Array.Empty<EntityTag>())
                if (required != null && !Contains(tags, required))
                    return false;
            return true;
        }

        private static bool Contains(
            System.Collections.Generic.IReadOnlyList<EntityTag> tags,
            EntityTag expected)
        {
            for (int i = 0; i < tags.Count; i++)
                if (tags[i] == expected)
                    return true;
            return false;
        }

        private static IEntityTagProvider FindEntityProvider(GameObject target)
        {
            if (target == null)
                return null;
            foreach (MonoBehaviour behaviour in
                     target.GetComponentsInParent<MonoBehaviour>(true))
                if (behaviour is IEntityTagProvider provider)
                    return provider;
            return null;
        }
    }
}
