using System.Collections.Generic;
using Project.Scripts.Interface;
using UnityEngine;
using Project.Scripts.DataTypes.SaveData;
using EntityId = Project.Scripts.DataTypes.SaveData.EntityId;

namespace Project.Scripts.Core
{
    public class PersistentEntity : MonoBehaviour
    {
        [SerializeField] private int archetypeId;

        private EntityId _id;
        
        public EntityId Id => _id;
        public int ArchetypeId => archetypeId;

        public void Initialize(EntityId id)
        {
            _id = id;
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
    }
}