using System;
using Project.Scripts.AI.GraphEditor;
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

                if (!string.IsNullOrWhiteSpace(tag) &&
                    !candidateObject.CompareTag(tag))
                    continue;

                float candidateDistanceSquared =
                    (candidateObject.transform.position -
                     self.transform.position).sqrMagnitude;
                if (candidateDistanceSquared >= nearestDistanceSquared)
                    continue;

                nearest = candidateObject;
                nearestDistanceSquared = candidateDistanceSquared;
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
    }
}
