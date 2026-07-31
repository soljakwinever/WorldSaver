using System;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    /// <summary>
    /// Supplies door relationship and hazard immunity data to path searches.
    /// IDs are stable strings so snapshots remain safe to evaluate off-thread.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AiPathingAgent : MonoBehaviour, IAiPathingAgent
    {
        [SerializeField] private string actorId;
        [SerializeField] private string villageId;
        [SerializeField] private string factionId;
        [SerializeField] private string[] immunities = Array.Empty<string>();

        public PathFindingQuery CapturePathFindingQuery()
        {
            string[] captured = immunities == null
                ? Array.Empty<string>()
                : (string[])immunities.Clone();
            return new PathFindingQuery(
                Normalize(actorId),
                Normalize(villageId),
                Normalize(factionId),
                captured);
        }

        public void SetIdentity(
            string newActorId,
            string newVillageId,
            string newFactionId)
        {
            actorId = Normalize(newActorId);
            villageId = Normalize(newVillageId);
            factionId = Normalize(newFactionId);
        }

        private void OnValidate()
        {
            actorId = Normalize(actorId);
            villageId = Normalize(villageId);
            factionId = Normalize(factionId);
            if (immunities == null)
            {
                immunities = Array.Empty<string>();
                return;
            }

            for (int i = 0; i < immunities.Length; i++)
                immunities[i] = Normalize(immunities[i]);
        }

        private static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
