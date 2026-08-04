using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(
        fileName = "Water Tile Tool Action",
        menuName = "Data/Tool Actions/Water Tile")]
    public sealed class WaterTileToolAction : ToolAction, IRequiresToolProximity
    {
        [SerializeField, Min(0f)] private float interactionRange = 1.75f;

        public bool IsWithinToolRange(ToolActionContext context)
        {
            if (context.User == null) return false;
            Vector3Int cell = Vector3Int.FloorToInt(context.TargetPosition);
            Vector2 center = new(cell.x + 0.5f, cell.y + 0.5f);
            return ((Vector2)context.User.transform.position - center).sqrMagnitude <=
                   interactionRange * interactionRange;
        }

        public override bool CanPerform(ToolActionContext context) =>
            TryGetContext(context, out Chunkloader loader, out Vector3Int center,
                out WaterTileToolActionData data, out _) &&
            HasWaterableTile(loader, center, data.wateringRadius);

        public override bool Perform(ToolActionContext context)
        {
            if (!TryGetContext(context, out Chunkloader loader, out Vector3Int center,
                    out WaterTileToolActionData data, out PersistentInventory inventory))
                return false;

            float radius = Mathf.Max(0f, data.wateringRadius);
            int extent = Mathf.CeilToInt(radius);
            float radiusSquared = radius * radius;
            bool wateredAny = false;
            for (int y = -extent; y <= extent; y++)
            for (int x = -extent; x <= extent; x++)
            {
                if (x * x + y * y > radiusSquared)
                    continue;

                Vector3Int cell = new(center.x + x, center.y + y, center.z);
                if (!TryGetWaterableTile(loader, cell, out Chunk chunk, out TileData tile))
                    continue;

                float before = chunk.GetPlantWater(cell);
                float after = chunk.AddPlantWater(
                    cell,
                    tile.maximumWaterPoints - before,
                    tile.maximumWaterPoints);
                wateredAny |= after > before;
            }

            if (!wateredAny)
                return false;

            return inventory.TryConsumeDurability(context.Item, data.waterPerUse);
        }

        private static bool TryGetContext(ToolActionContext context,
            out Chunkloader loader, out Vector3Int center,
            out WaterTileToolActionData data, out PersistentInventory inventory)
        {
            center = Vector3Int.FloorToInt(context.TargetPosition);
            loader = FindAnyObjectByType<Chunkloader>();
            data = null;
            inventory = null;
            if (context.User == null || context.Item == null ||
                !context.Item.TryGetActionData(out data) || data.waterPerUse == 0 ||
                loader == null)
                return false;

            inventory = context.User.GetComponentInParent<PersistentInventory>();
            return inventory != null &&
                   inventory.HasDurability(context.Item, data.waterPerUse);
        }

        private static bool HasWaterableTile(
            Chunkloader loader, Vector3Int center, float configuredRadius)
        {
            float radius = Mathf.Max(0f, configuredRadius);
            int extent = Mathf.CeilToInt(radius);
            float radiusSquared = radius * radius;
            for (int y = -extent; y <= extent; y++)
            for (int x = -extent; x <= extent; x++)
            {
                if (x * x + y * y > radiusSquared)
                    continue;

                Vector3Int cell = new(center.x + x, center.y + y, center.z);
                if (TryGetWaterableTile(loader, cell, out _, out _))
                    return true;
            }

            return false;
        }

        private static bool TryGetWaterableTile(
            Chunkloader loader, Vector3Int cell, out Chunk chunk, out TileData tile)
        {
            tile = null;
            return loader.TryGetLoadedChunk(cell, out chunk) &&
                   chunk.TryGetTileData(cell, PersistentTileLayer.Ground, out tile) &&
                   tile != null && tile.usesMoistureTint &&
                   chunk.GetPlantWater(cell) < tile.maximumWaterPoints;
        }
    }
}
