using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts
{
    public sealed class VillagerWorldAdapter : IVillagerWorldAdapter
    {
        public bool TryBuild(ItemData buildingItem, Vector3 position, out string reason)
        {
            if (buildingItem == null)
            {
                reason = "The build order has no buildable item.";
                return false;
            }

            Chunkloader loader = Object.FindFirstObjectByType<Chunkloader>();
            if (loader == null ||
                !loader.TryGetLoadedChunk(Vector3Int.FloorToInt(position), out Chunk chunk))
            {
                reason = "The building position is unavailable.";
                return false;
            }

            if (buildingItem.TryGetActionData(out PlacePersistentNodeItemActionData node) &&
                node.node != null)
            {
                if (!chunk.TrySpawnRuntimeEntity(node.node, position, out _))
                { reason = "The building position is unavailable."; return false; }
                reason = string.Empty;
                return true;
            }

            if (buildingItem.TryGetActionData(out PlaceTileItemActionData tile) &&
                tile.tile != null)
            {
                Vector3Int cell = Vector3Int.FloorToInt(position);
                if (!chunk.TryPlaceTile(cell, tile.layer, tile.tile))
                { reason = "The tile position is unavailable."; return false; }
                reason = string.Empty;
                return true;
            }

            reason = "The build order has no supported placement data.";
            return false;
        }
    }
}
