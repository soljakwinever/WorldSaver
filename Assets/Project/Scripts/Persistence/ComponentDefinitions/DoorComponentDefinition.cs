using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(
        fileName = "Door Component Definition",
        menuName = "World/Components/Door")]
    public sealed class DoorComponentDefinition : NodeComponentDefinition
    {
        [SerializeField] private TileData closedTile;
        [SerializeField] private TileData openTile;
        [SerializeField] private bool startsOpen;
        [SerializeField] private string openPrompt = "Open door";
        [SerializeField] private string closePrompt = "Close door";
        [SerializeField] private bool hideNodeSprite = true;
        [SerializeField, Min(1)] private int legacyClosedTileId = 13;
        [SerializeField, Min(1)] private int legacyOpenTileId = 14;

        public override void Install(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context)
        {
            DoorComponent component = host.GetComponent<DoorComponent>();
            if (component == null)
                component = container.InstantiateComponent<DoorComponent>(host);
            else
                container.Inject(component);

            Node node = context.Node as Node;
            if (hideNodeSprite && node != null &&
                node.TryGetComponent(out SpriteRenderer renderer))
            {
                renderer.enabled = false;
            }

            component.Initialize(
                context.Chunk as Chunk,
                node != null ? node.transform : host.transform,
                closedTile,
                openTile,
                legacyClosedTileId,
                legacyOpenTileId,
                context.PersistenceKind,
                startsOpen,
                openPrompt,
                closePrompt);
        }
    }
}
