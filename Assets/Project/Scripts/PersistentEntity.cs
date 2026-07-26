using System.Collections.Generic;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Core
{
    public sealed class PersistentEntity : MonoBehaviour, IPersistentEntity
    {
        [SerializeField] private int archetypeId;

        private NodeId _id;
        private EntityPersistenceKind _persistenceKind;
        private ChunkPersistenceRoot _owner;
        private PersistentComponentHost _componentHost;

        public NodeId Id => _id;
        public int ArchetypeId => archetypeId;
        public EntityPersistenceKind PersistenceKind => _persistenceKind;
        public PersistentComponentHost ComponentHost => _componentHost;
        public bool CanRemoveFromWorld => _owner != null;

        public void Initialize(
            NodeId id,
            EntityPersistenceKind persistenceKind,
            int runtimeArchetypeId = 0)
        {
            _id = id;
            _persistenceKind = persistenceKind;
            archetypeId = runtimeArchetypeId;
            _owner = null;
        }

        public void SetComponentHost(PersistentComponentHost componentHost)
        {
            _componentHost = componentHost;

            if (_componentHost != null)
                _componentHost.Initialize(this);
        }

        public void SetOwner(ChunkPersistenceRoot owner)
        {
            _owner = owner;
        }

        public void ClearOwner(ChunkPersistenceRoot expectedOwner)
        {
            if (_owner == expectedOwner)
                _owner = null;
        }

        public void RemoveFromWorld()
        {
            if (_owner == null)
            {
                Debug.LogError($"Persistent entity {Id} has no chunk owner.", this);
                return;
            }

            _owner.NotifyEntityRemoved(this);
            gameObject.SetActive(false);
        }

        public IEnumerable<IPersistentComponent> GetPersistentComponents()
        {
            if (_componentHost == null)
            {
                _componentHost = GetComponentInChildren<PersistentComponentHost>(
                    includeInactive: true);
            }

            return _componentHost != null
                ? _componentHost.GetPersistentComponents()
                : System.Array.Empty<IPersistentComponent>();
        }

        public PersistentEntityRecord CapturePersistentState(long currentTick)
        {
            return EntityStateUtility.Capture(this, currentTick);
        }

        public void RestorePersistentState(PersistentEntityRecord record)
        {
            EntityStateUtility.Restore(this, record);
        }

        public void SuppressFromPersistentRestore()
        {
            gameObject.SetActive(false);
        }

        public void SetPersistenceReady(bool ready)
        {
            MonoBehaviour[] behaviours =
                GetComponentsInChildren<MonoBehaviour>(includeInactive: true);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IPersistenceInteractionGate gate)
                    gate.SetPersistenceReady(ready);
            }
        }
    }
}
