using System;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class PersistentRespawnData : ComponentDefinitionData
    {
        [Min(0f)] public float minimumDays = 2f;
        [Min(0f)] public float maximumDays = 4f;
        public bool respawnInsideTownInfluence;
    }

    [CreateAssetMenu(fileName = "Persistent Respawn", menuName = "World/Persistence/Persistent Respawn")]
    public sealed class PersistentRespawnDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(PersistentRespawnData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            if (context.PersistenceKind == EntityPersistenceKind.RuntimeSpawned)
                throw new InvalidOperationException(
                    "Persistent respawn requires a generated deterministic entity.");

            PersistentRespawnData configuration = (PersistentRespawnData)data;
            PersistentEntity entity = host.GetComponentInParent<PersistentEntity>();
            WorldData world = container.Resolve<WorldData>();
            WorldClock clock = container.Resolve<WorldClock>();
            PersistentRespawn component = host.GetComponent<PersistentRespawn>() ??
                container.InstantiateComponent<PersistentRespawn>(host);
            component.Initialize(
                entity.Id,
                configuration.minimumDays,
                configuration.maximumDays,
                configuration.respawnInsideTownInfluence,
                Mathf.Max(
                    1f,
                    world.minutesPerDay * 60f / clock.SecondsPerTick));
        }
    }
}
