using System;
using Project.Scripts.Bus;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public sealed class PersistentEntityRemovalBridge :
        IInitializable,
        IDisposable
    {
        private readonly EntityBus _entityBus;

        public PersistentEntityRemovalBridge(EntityBus entityBus)
        {
            _entityBus = entityBus;
        }

        public void Initialize()
        {
            PersistentEntity.RemovedFromWorld += OnRemovedFromWorld;
        }

        public void Dispose()
        {
            PersistentEntity.RemovedFromWorld -= OnRemovedFromWorld;
        }

        private void OnRemovedFromWorld(
            NodeData nodeData,
            NodeId entityId,
            Vector3 position)
        {
            _entityBus.RaiseEntityRemoved(nodeData, entityId, position);
        }
    }
}
