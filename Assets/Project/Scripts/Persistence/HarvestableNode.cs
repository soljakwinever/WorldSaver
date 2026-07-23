using Project.Scripts.Core;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    // Minimal vertical slice: harvesting a deterministic procedural entity
    // creates a tombstone. No component payload is required.
    [RequireComponent(typeof(PersistentEntity))]
    public sealed class HarvestableNode : MonoBehaviour, IPersistenceInteractionGate
    {
        private PersistentEntity _persistentEntity;
        private bool _persistenceReady;

        private void Awake()
        {
            _persistentEntity = GetComponent<PersistentEntity>();
        }

        public bool TryHarvest()
        {
            if (!_persistenceReady)
                return false;

            _persistentEntity.RemoveFromWorld();
            return true;
        }

        public void SetPersistenceReady(bool ready)
        {
            _persistenceReady = ready;
        }
    }
}
