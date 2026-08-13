using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [AiNode("Is Object Nearby", "Sensors"), Serializable]
    public class DetectNearby : AiNode
    {
        [InputPort("Distance"), SerializeField]
        private float distance;
        
        [InputPort("Layer Mask"), SerializeField]
        private LayerMask layerMask;

        [InputPort("Tag"), SerializeField] private string tag;
        
        [NonSerialized]
        private readonly Collider2D[] _results = new Collider2D[16];
            
        protected override NodeState OnTick()
        {
            if (!Blackboard.TryGet(AiKeys.Self, out GameObject self) ||
                self == null)
                throw new InvalidOperationException();

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
                    if (candidateObject == null || !candidateObject.activeInHierarchy)
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
            if (string.IsNullOrWhiteSpace(tag) || candidate.CompareTag(tag))
                return true;

            // Existing hostile trees search for the Player tag. Villager ECS
            // bridges deliberately retain the NPC tag but opt into the same
            // enemy target group through this marker.
            if (!string.Equals(tag, "Player", StringComparison.Ordinal))
                return false;

            MonoBehaviour[] behaviours =
                candidate.GetComponentsInParent<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
                if (behaviour is Project.Scripts.Interface.IEnemyTarget)
                    return true;
            return false;
        }
    }
}
