using System.Collections.Generic;
using System.Linq;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using Project.Scripts.DataTypes.SaveData;
using Zenject;

namespace Project.Scripts.Core
{
    public class PersistentEntity : MonoBehaviour
    {
        [SerializeField] private int archetypeId;

        private NodeId _id;
        private EntityPersistenceKind _persistenceKind;
        
        public NodeId Id => _id;
        public int ArchetypeId => archetypeId;

        private ChunkPersistenceRoot _owner;
        
        [Inject] private Node.Pool nodePool;
        
        public void SetOwner(ChunkPersistenceRoot owner)
        {
            _owner = owner;
        }

        public void RemoveFromWorld()
        {
            _owner.NotifyEntityRemoved(this);
        }
        
        public EntityPersistenceKind PersistenceKind => _persistenceKind;

        public void Initialize(NodeId id, EntityPersistenceKind persistenceKind)
        {
            _id = id;
            _persistenceKind = persistenceKind;
        }
        
        public IEnumerable<IPersistentComponent> GetPersistentComponents()
        {
            MonoBehaviour[] behaviors = GetComponentsInChildren<MonoBehaviour>(true);

            foreach (var behavior in behaviors)
            {
                if(behavior is IPersistentComponent persistent)
                    yield return persistent;
            }
        }
        
        public PersistentEntityRecord CapturePersistentState(long currentTick)
        {
            PersistentEntityRecord record = new()
            {
                id = _id,
                archetypeId = archetypeId,
                components = GetPersistentComponents().ToList(),
            };
        }

        public void SuppressFromPersistentRestore()
        {
            //nodePool.Despawn(this);
        }

        public void RestorePersistentState(PersistentEntityRecord record)
        {
            throw new System.NotImplementedException();
        }

        public void SimulateOffline(long fromTick, long toTick, RuntimeRegion region)
        {
            
        }
    }
}