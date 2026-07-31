using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable, AiNode(Name = "Find Town Core Target", Group = "Sensors")]
    public sealed class FindTownCoreTarget : AiNode
    {
        [SerializeField, Min(0f), InputPort("Distance")]
        private float distance = 20f;

        [SerializeField, InputPort("Layer Mask")]
        private LayerMask layerMask = ~0;

        [NonSerialized] private readonly Collider2D[] _results =
            new Collider2D[64];

        protected override NodeState OnTick()
        {
            GameObject self = Blackboard.GetOrDefault(AiKeys.Self);
            if (self == null)
                return NodeState.Failure;

            int count = Physics2D.OverlapCircleNonAlloc(
                self.transform.position,
                distance,
                _results,
                layerMask);
            Transform nearest = null;
            float nearestDistance = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                Collider2D candidate = _results[i];
                if (candidate == null)
                    continue;

                ITownCore town =
                    candidate.GetComponentInChildren<ITownCore>() ??
                    candidate.GetComponentInParent<ITownCore>();
                if (town == null)
                    continue;

                Component component = town as Component;
                if (component == null)
                    continue;

                float sqrDistance =
                    (component.transform.position -
                     self.transform.position).sqrMagnitude;
                if (sqrDistance >= nearestDistance)
                    continue;

                nearestDistance = sqrDistance;
                nearest = component.transform;
            }

            Blackboard.Set(AiKeys.Target, nearest);
            return nearest != null ? NodeState.Success : NodeState.Failure;
        }
    }
}
