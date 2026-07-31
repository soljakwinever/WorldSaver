using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Interface
{
    /// <summary>
    /// Public town contract used by AI, fast travel, villagers, and UI without
    /// coupling those systems to the concrete persistent component.
    /// </summary>
    public interface ITownCore
    {
        string TownName { get; }
        int Population { get; }
        int MaxPopulation { get; }
        float ManaPool { get; }
        float MaxMana { get; }
        float TownRadius { get; }
        float ResourceRadius { get; }
        Vector3 Position { get; }
        IReadOnlyList<GameObject> Buildings { get; }

        bool ContainsTownPosition(Vector3 worldPosition);
        bool ContainsResourcePosition(Vector3 worldPosition);
        bool TryFastTravel(Transform traveller);
    }
}
