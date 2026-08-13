using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "Town Feature", menuName = "World Generation/Town Feature")]
    public sealed class TownFeatureData : FeatureData
    {
        [Header("Town Entities")]
        public NodeData townCoreEntity;
        public NodeData doorEntity;
        public NodeData bedEntity;
        public NodeData torchEntity;
        public NodeData campfireEntity;
        public NodeData furnaceEntity;
        public NodeData villagerEntity;

        [Header("Town Surfaces")]
        public TileData streetTile;
        [Tooltip("Optional distinct surface beneath the TownCore plaza.")]
        public TileData townCorePlazaTile;
        public TileData buildingFloorTile;
        [Tooltip("Must be a wall tile with Encloses Room enabled.")]
        public TileData buildingWallTile;

        [Header("Alternate Lots")]
        [Range(0f, 1f), Tooltip("Chance that a valid building lot becomes a garden or prop lot instead.")]
        public float alternateLotChance = 0.2f;
        [Tooltip("Entities that may occupy alternate lots, such as gardens, wells, or market stalls.")]
        public NodeData[] alternateLotProps = Array.Empty<NodeData>();
        [Tooltip("Optional ground surface painted beneath alternate-lot props.")]
        public TileData alternateLotTile;

        [Header("Guaranteed Placement")]
        [Tooltip("Create one deterministic instance of this village outside the spawn-platform exclusion radius, with a guaranteed road from the spawn platform, in addition to ordinary random instances.")]
        public bool guaranteeNearWorldSpawn;
        [Min(1), Tooltip("Minimum distance from the spawn platform, measured in world regions.")]
        public int minimumSpawnDistanceRegions = 1;
        [Min(1), Tooltip("Maximum search distance from the spawn platform, measured in world regions.")]
        public int maximumSpawnDistanceRegions = 2;

        [Header("Town Names")]
        public string[] namePrefixes = { "Oak", "River", "Stone", "Green", "High" };
        public string[] nameSuffixes = { "rest", "ford", "haven", "wick", "stead" };

        [Header("Layout")]
        [SerializeReference, ManagedReferenceSelector(typeof(TownLayoutGenerator))]
        public TownLayoutGenerator layoutGenerator = new VillageTownLayoutGenerator();

        public string GenerateTownName(int seed)
        {
            string[] prefixes = namePrefixes ?? Array.Empty<string>();
            string[] suffixes = nameSuffixes ?? Array.Empty<string>();
            string prefix = prefixes.Length > 0
                ? prefixes[PositiveMod(seed, prefixes.Length)]?.Trim()
                : "New";
            string suffix = suffixes.Length > 0
                ? suffixes[PositiveMod(unchecked(seed * 397) ^ 0x51bc3, suffixes.Length)]?.Trim()
                : "Village";
            string result = $"{prefix}{suffix}";
            return string.IsNullOrWhiteSpace(result) ? "New Village" : result;
        }

        private static int PositiveMod(int value, int count) =>
            (int)((uint)value % (uint)count);

        protected override void OnValidate()
        {
            base.OnValidate();
            layoutGenerator ??= new VillageTownLayoutGenerator();
            minimumSpawnDistanceRegions = Mathf.Max(1, minimumSpawnDistanceRegions);
            maximumSpawnDistanceRegions = Mathf.Max(
                minimumSpawnDistanceRegions,
                maximumSpawnDistanceRegions);
        }
    }

    public enum TownCellKind : byte
    {
        None,
        Street,
        TownCorePlaza,
        BuildingFloor,
        AlternateLot,
        BuildingWall,
        Door
    }

    public readonly struct TownEntityPlacement
    {
        public readonly string id;
        public readonly NodeData entity;
        public readonly Vector2Int localCell;
        public readonly string generatedName;
        public readonly string ownerPlacementId;
        public readonly bool usesVillageDoorAccess;

        public TownEntityPlacement(string id, NodeData entity,
            Vector2Int localCell, string generatedName = null,
            string ownerPlacementId = null,
            bool usesVillageDoorAccess = false)
        {
            this.id = id;
            this.entity = entity;
            this.localCell = localCell;
            this.generatedName = generatedName;
            this.ownerPlacementId = ownerPlacementId;
            this.usesVillageDoorAccess = usesVillageDoorAccess;
        }
    }

    public sealed class TownLayout
    {
        private readonly Dictionary<Vector2Int, TownCellKind> cells = new();
        private readonly List<TownEntityPlacement> entities = new();

        public IReadOnlyList<TownEntityPlacement> Entities => entities;
        public void SetCell(Vector2Int cell, TownCellKind kind) => cells[cell] = kind;
        public TownCellKind GetCell(Vector2Int cell) =>
            cells.TryGetValue(cell, out TownCellKind kind) ? kind : TownCellKind.None;
        public void AddEntity(TownEntityPlacement placement) => entities.Add(placement);
    }

    [Serializable]
    public abstract class TownLayoutGenerator
    {
        public abstract TownLayout Generate(TownFeatureData town, int seed, int radius);

        protected static int Range(int seed, int salt, int minimum, int maximumInclusive)
        {
            if (maximumInclusive <= minimum) return minimum;
            uint value = unchecked((uint)(seed * 486187739 + salt * 16777619));
            value ^= value >> 16;
            return minimum + (int)(value % (uint)(maximumInclusive - minimum + 1));
        }
    }

    /// <summary>A plaza with trunk, branch, and twig streets plus roadside lots.</summary>
    [Serializable]
    public sealed class VillageTownLayoutGenerator : TownLayoutGenerator
    {
        [Min(1), Tooltip("Width of the entrance trunk and longest streets.")]
        public int streetWidth = 2;
        [Min(1), Tooltip("Width of secondary branch streets.")]
        public int branchStreetWidth = 1;
        [Min(2)] public int minimumStreetPoints = 4;
        [Min(2)] public int maximumStreetPoints = 7;
        [Min(1)] public int townCorePlazaRadius = 4;
        [Min(1)] public int minimumBuildings = 3;
        [Min(1)] public int maximumBuildings = 8;
        public Vector2Int minimumBuildingSize = new(7, 6);
        public Vector2Int maximumBuildingSize = new(11, 9);
        [Range(0f, 1f)] public float extraRoomChance = 0.65f;

        public override TownLayout Generate(TownFeatureData town, int seed, int radius)
        {
            var layout = new TownLayout();
            int usableRadius = Mathf.Max(10, radius - 2);
            int plazaRadius = Mathf.Clamp(townCorePlazaRadius, 2, usableRadius / 3);
            var streets = new List<StreetSegment>();

            // The east-facing trunk meets the guaranteed external road.
            AddStreet(layout, streets, Vector2.zero, Vector2.right * usableRadius, streetWidth);
            int pointCount = Range(seed, 17, minimumStreetPoints, Mathf.Max(minimumStreetPoints, maximumStreetPoints));
            for (int point = 1; point < pointCount; point++)
            {
                float baseAngle = point * Mathf.PI * 2f / pointCount;
                float jitter = (Range(seed, 31 + point, -1000, 1000) / 1000f) * 0.28f;
                float angle = baseAngle + jitter;
                Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
                float length = Mathf.Lerp(
                    usableRadius * 0.58f,
                    usableRadius,
                    Range(seed, 51 + point, 0, 1000) / 1000f);
                Vector2 start = direction * Mathf.Max(1, plazaRadius - 1);
                Vector2 end = direction * length;
                AddStreet(layout, streets, start, end, streetWidth);

                // A short angled twig makes the network read like branches
                // and leaves instead of a radial + or X.
                float side = Range(seed, 71 + point, 0, 1) == 0 ? -1f : 1f;
                Vector2 twigStart = Vector2.Lerp(start, end, 0.58f);
                float twigAngle = angle + side * Mathf.Lerp(0.45f, 0.9f,
                    Range(seed, 81 + point, 0, 1000) / 1000f);
                Vector2 twigEnd = twigStart + new Vector2(
                    Mathf.Cos(twigAngle), Mathf.Sin(twigAngle)) * length * 0.38f;
                if (twigEnd.magnitude > usableRadius)
                    twigEnd = twigEnd.normalized * usableRadius;
                AddStreet(layout, streets, twigStart, twigEnd, branchStreetWidth);
            }

            PaintCircle(layout, Vector2Int.zero, plazaRadius, TownCellKind.TownCorePlaza);

            int requested = Range(seed, 11, minimumBuildings, Mathf.Max(minimumBuildings, maximumBuildings));
            int placed = 0;
            int attempts = Mathf.Max(requested * 10, streets.Count * 4);
            for (int attempt = 0; attempt < attempts && placed < requested; attempt++)
            {
                StreetSegment street = streets[attempt % streets.Count];
                float t = Mathf.Lerp(0.3f, 0.88f, Range(seed, 1000 + attempt, 0, 1000) / 1000f);
                Vector2 roadPoint = Vector2.Lerp(street.start, street.end, t);
                Vector2 direction = (street.end - street.start).normalized;
                Vector2 normal = new(-direction.y, direction.x);
                if (Range(seed, 1100 + attempt, 0, 1) == 0) normal = -normal;
                int width = Range(seed, 1200 + attempt, minimumBuildingSize.x, maximumBuildingSize.x);
                int height = Range(seed, 1300 + attempt, minimumBuildingSize.y, maximumBuildingSize.y);
                float clearance = street.width * 0.5f + Mathf.Max(width, height) * 0.5f + 2f;
                Vector2Int center = Vector2Int.RoundToInt(roadPoint + normal * clearance);
                RectInt bounds = new(center.x - width / 2, center.y - height / 2, width, height);
                if (!CanUseLot(layout, bounds, usableRadius))
                    continue;

                bool alternate = town.alternateLotProps != null && town.alternateLotProps.Length > 0 &&
                                 Range(seed, 1400 + attempt, 0, 999) < town.alternateLotChance * 1000f;
                if (alternate)
                    AddAlternateLot(layout, town, seed, placed, bounds);
                else
                    AddBuilding(layout, town, seed, placed, bounds, roadPoint);
                placed++;
            }

            string townName = town.GenerateTownName(seed);
            if (town.townCoreEntity != null)
                layout.AddEntity(new TownEntityPlacement("TownCore", town.townCoreEntity, Vector2Int.zero, townName));
            if (town.campfireEntity != null)
                layout.AddEntity(new TownEntityPlacement(
                    "TownCampfire", town.campfireEntity,
                    new Vector2Int(-Mathf.Max(2, plazaRadius - 1), 0)));
            if (town.furnaceEntity != null)
                layout.AddEntity(new TownEntityPlacement(
                    "TownFurnace", town.furnaceEntity,
                    new Vector2Int(0, Mathf.Max(2, plazaRadius - 1))));
            return layout;
        }

        private void AddBuilding(TownLayout layout, TownFeatureData town, int seed, int index, RectInt bounds, Vector2 roadPoint)
        {
            foreach (Vector2Int cell in bounds.allPositionsWithin)
            {
                bool boundary = cell.x == bounds.xMin || cell.x == bounds.xMax - 1 ||
                                cell.y == bounds.yMin || cell.y == bounds.yMax - 1;
                layout.SetCell(cell, boundary ? TownCellKind.BuildingWall : TownCellKind.BuildingFloor);
            }

            Vector2 center = bounds.center;
            Vector2 towardStreet = roadPoint - center;
            Vector2Int door;
            Vector2Int outward;
            if (Mathf.Abs(towardStreet.x) > Mathf.Abs(towardStreet.y))
            {
                outward = towardStreet.x < 0f ? Vector2Int.left : Vector2Int.right;
                door = new Vector2Int(
                    outward.x < 0 ? bounds.xMin : bounds.xMax - 1,
                    Mathf.Clamp(Mathf.RoundToInt(roadPoint.y), bounds.yMin + 1, bounds.yMax - 2));
            }
            else
            {
                outward = towardStreet.y < 0f ? Vector2Int.down : Vector2Int.up;
                door = new Vector2Int(
                    Mathf.Clamp(Mathf.RoundToInt(roadPoint.x), bounds.xMin + 1, bounds.xMax - 2),
                    outward.y < 0 ? bounds.yMin : bounds.yMax - 1);
            }
            AddDoor(layout, town, $"Building{index}:Door", door);
            Vector2Int walkway = door + outward;
            for (int step = 0; step < 8 && layout.GetCell(walkway) == TownCellKind.None; step++)
            {
                layout.SetCell(walkway, TownCellKind.Street);
                walkway += outward;
            }

            string villagerId = $"Building{index}:Villager";
            if (town.bedEntity != null)
                layout.AddEntity(new TownEntityPlacement(
                    $"Building{index}:Bed", town.bedEntity,
                    new Vector2Int(bounds.xMin + 1, bounds.yMin + 1),
                    ownerPlacementId: town.villagerEntity != null
                        ? villagerId : null));
            if (town.torchEntity != null)
                layout.AddEntity(new TownEntityPlacement(
                    $"Building{index}:Torch", town.torchEntity,
                    new Vector2Int(bounds.xMax - 2, bounds.yMax - 2)));
            if (town.villagerEntity != null && town.bedEntity != null)
                layout.AddEntity(new TownEntityPlacement(
                    villagerId, town.villagerEntity,
                    new Vector2Int(bounds.xMin + 2, bounds.yMin + 1)));

            if (Range(seed, 300 + index, 0, 999) >= extraRoomChance * 1000f || bounds.width < 8)
                return;
            int dividerX = bounds.center.x.RoundToInt();
            int internalDoorY = bounds.center.y.RoundToInt();
            for (int y = bounds.yMin + 1; y < bounds.yMax - 1; y++)
                layout.SetCell(new Vector2Int(dividerX, y), TownCellKind.BuildingWall);
            AddDoor(layout, town, $"Building{index}:RoomDoor", new Vector2Int(dividerX, internalDoorY));
        }

        private static bool CanUseLot(TownLayout layout, RectInt bounds, int radius)
        {
            RectInt padded = new(bounds.xMin - 1, bounds.yMin - 1, bounds.width + 2, bounds.height + 2);
            foreach (Vector2Int cell in padded.allPositionsWithin)
            {
                if (cell.sqrMagnitude > radius * radius || layout.GetCell(cell) != TownCellKind.None)
                    return false;
            }
            return true;
        }

        private static void AddAlternateLot(TownLayout layout, TownFeatureData town, int seed, int index, RectInt bounds)
        {
            foreach (Vector2Int cell in bounds.allPositionsWithin)
                layout.SetCell(cell, TownCellKind.AlternateLot);
            NodeData entity = town.alternateLotProps[
                Range(seed, 1600 + index, 0, town.alternateLotProps.Length - 1)];
            if (entity != null)
                layout.AddEntity(new TownEntityPlacement($"AlternateLot{index}", entity, Vector2Int.RoundToInt(bounds.center)));
        }

        private static void AddStreet(TownLayout layout, List<StreetSegment> streets, Vector2 start, Vector2 end, int width)
        {
            width = Mathf.Max(1, width);
            streets.Add(new StreetSegment(start, end, width));
            float length = Vector2.Distance(start, end);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length * 2f));
            for (int step = 0; step <= steps; step++)
            {
                Vector2Int center = Vector2Int.RoundToInt(Vector2.Lerp(start, end, (float)step / steps));
                int minimum = -width / 2;
                int maximum = minimum + width - 1;
                for (int y = minimum; y <= maximum; y++)
                for (int x = minimum; x <= maximum; x++)
                    layout.SetCell(center + new Vector2Int(x, y), TownCellKind.Street);
            }
        }

        private static void PaintCircle(TownLayout layout, Vector2Int center, int radius, TownCellKind kind)
        {
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y <= radius * radius)
                    layout.SetCell(center + new Vector2Int(x, y), kind);
            }
        }

        private readonly struct StreetSegment
        {
            public readonly Vector2 start;
            public readonly Vector2 end;
            public readonly int width;
            public StreetSegment(Vector2 start, Vector2 end, int width)
            {
                this.start = start;
                this.end = end;
                this.width = width;
            }
        }

        private static void AddDoor(TownLayout layout, TownFeatureData town, string id, Vector2Int cell)
        {
            layout.SetCell(cell, TownCellKind.Door);
            if (town.doorEntity != null)
                layout.AddEntity(new TownEntityPlacement(
                    id, town.doorEntity, cell,
                    usesVillageDoorAccess: true));
        }
    }

    internal static class TownLayoutNumberExtensions
    {
        public static int RoundToInt(this float value) => Mathf.RoundToInt(value);
    }
}
