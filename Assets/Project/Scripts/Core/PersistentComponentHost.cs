using System.Collections.Generic;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Core
{
    /// <summary>
    /// Marks the hierarchy that contains the persistent components composed for
    /// a single entity.
    /// </summary>
    public sealed class PersistentComponentHost : MonoBehaviour
    {
        public IPersistentEntity persistentEntity;

        public void Initialize(IPersistentEntity persistentEntity)
        {
            this.persistentEntity = persistentEntity;

            MonoBehaviour[] behaviours =
                GetComponentsInChildren<MonoBehaviour>(includeInactive: true);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IEntityComponent component)
                    component.PersistentEntity = persistentEntity;
            }
        }

        public IEnumerable<IPersistentComponent> GetPersistentComponents()
        {
            MonoBehaviour[] behaviours =
                GetComponentsInChildren<MonoBehaviour>(includeInactive: true);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IPersistentComponent component)
                    yield return component;
            }
        }
    }
}
