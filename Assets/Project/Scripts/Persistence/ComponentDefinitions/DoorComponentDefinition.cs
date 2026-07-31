using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class DoorComponentData : ComponentDefinitionData
    {
        public TileData closedTile;
        public TileData openTile;
        public bool startsOpen;
        public string openPrompt = "Open door";
        public string closePrompt = "Close door";
        public bool hideNodeSprite = true;
        [Header("AI Access")]
        [Tooltip("Most distant relationship allowed through freely. Owner is most restrictive; Enemy allows everyone.")]
        public DoorAccessPolicy accessPolicy = DoorAccessPolicy.Neutral;
        public bool startsLocked;
        [Min(1)] public int lockpickDifficulty = 1;
        [Min(1)] public int breakHealth = 25;
        [Min(1)] public int legacyClosedTileId = 13;
        [Min(1)] public int legacyOpenTileId = 14;
    }

    [CreateAssetMenu(
        fileName = "Door Component Definition",
        menuName = "World/Components/Door")]
    public sealed class DoorComponentDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(DoorComponentData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (DoorComponentData)data;
            DoorComponent component = host.GetComponent<DoorComponent>();
            if (component == null)
                component = container.InstantiateComponent<DoorComponent>(host);
            else
                container.Inject(component);

            Node node = context.Node as Node;
            if (configuration.hideNodeSprite && node != null &&
                node.TryGetComponent(out SpriteRenderer renderer))
            {
                renderer.enabled = false;
            }

            component.Initialize(
                context.Chunk as Chunk,
                node != null ? node.transform : host.transform,
                configuration.closedTile,
                configuration.openTile,
                configuration.legacyClosedTileId,
                configuration.legacyOpenTileId,
                context.PersistenceKind,
                configuration.startsOpen,
                configuration.openPrompt,
                configuration.closePrompt,
                configuration.accessPolicy,
                context.AccessIdentity,
                configuration.startsLocked,
                configuration.lockpickDifficulty,
                configuration.breakHealth);
        }
    }
}
