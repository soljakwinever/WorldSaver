using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Entities;
using UnityEngine;
using UnityEngine.Tilemaps;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class TownConstructionQueue : MonoBehaviour, IPersistentComponent
    {
        public const ushort TypeId = 0x5443; // TC
        private const ushort Version = 1;
        private const int MaxBlueprints = 10000;
        private readonly List<BlueprintRecord> _records = new();
        private readonly Dictionary<string, GameObject> _ghosts = new();
        private ItemCatalog _catalog;
        private TownCore _town;
        private TownJobBoard _jobs;
        private VillagerResourceDiscovery _resources;
        private float _nextPlanTime;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => Version;
        public IReadOnlyList<BlueprintRecord> Blueprints => _records;

        [Inject]
        public void Construct(ItemCatalog catalog) =>
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        private void Awake() => ResolveComponents();
        private void OnEnable() => RebuildGhosts();

        private void OnDisable()
        {
            foreach (GameObject ghost in _ghosts.Values)
                if (ghost != null) Destroy(ghost);
            _ghosts.Clear();
        }

        private void Update()
        {
            ResolveComponents();
            EnsureGhosts();
            if (Time.time < _nextPlanTime || _town == null || _jobs == null) return;
            _nextPlanTime = Time.time + 0.5f;
            PlanWork();
        }

        public bool TryAdd(ItemData item, Vector3 worldPosition, out string id,
            out string reason)
        {
            id = string.Empty;
            reason = string.Empty;
            ResolveComponents();
            if (item == null || _town == null || !_town.ContainsTownPosition(worldPosition))
            {
                reason = "Blueprints must be placed inside the town.";
                return false;
            }

            bool isTile = item.TryGetActionData(out PlaceTileItemActionData tile);
            bool isNode = item.TryGetActionData(out PlacePersistentNodeItemActionData node);
            if (!isTile && !isNode)
            {
                reason = "This item is not placeable.";
                return false;
            }

            Vector3 position = isTile
                ? new Vector3(Mathf.Floor(worldPosition.x) + 0.5f,
                    Mathf.Floor(worldPosition.y) + 0.5f, 0f)
                : new Vector3(Mathf.Floor(worldPosition.x),
                    Mathf.Floor(worldPosition.y), 0f);
            foreach (BlueprintRecord existing in _records)
                if ((existing.Position - position).sqrMagnitude < 0.01f)
                {
                    reason = "A construction ghost already occupies this cell.";
                    return false;
                }

            id = Guid.NewGuid().ToString("N");
            float configuredWork = isTile
                ? tile.constructionWorkRequired
                : node.constructionWorkRequired;
            float work = configuredWork > 0f ? configuredWork : 1f;
            var record = new BlueprintRecord(id, item.persistentId, position,
                Mathf.Max(0.01f, work), TownJobPriority.Normal);
            _records.Add(record);
            CreateGhost(record);
            return true;
        }

        public bool Cancel(string id)
        {
            BlueprintRecord record = Find(id);
            if (record == null) return false;
            _jobs?.CancelConstructionJobs(id);
            _records.Remove(record);
            RemoveGhost(id);
            return true;
        }

        public bool TryCancelAt(Vector3 worldPosition,
            out BlueprintRecord cancelled)
        {
            cancelled = null;
            Vector2Int clickedCell = Vector2Int.FloorToInt(worldPosition);
            for (int i = _records.Count - 1; i >= 0; i--)
            {
                BlueprintRecord record = _records[i];
                bool hit = false;
                if (_ghosts.TryGetValue(record.Id, out GameObject ghost) &&
                    ghost != null &&
                    ghost.TryGetComponent(out SpriteRenderer renderer) &&
                    renderer.enabled)
                {
                    hit = renderer.bounds.Contains(new Vector3(
                        worldPosition.x, worldPosition.y,
                        renderer.bounds.center.z));
                }

                if (!hit && Vector2Int.FloorToInt(record.Position) != clickedCell)
                    continue;

                cancelled = record;
                return Cancel(record.Id);
            }

            return false;
        }

        public bool SetPriority(string id, TownJobPriority priority)
        {
            BlueprintRecord record = Find(id);
            if (record == null || priority is TownJobPriority.Off or TownJobPriority.Emergency)
                return false;
            record.Priority = priority;
            return true;
        }

        internal bool CompleteBuild(string id)
        {
            BlueprintRecord record = Find(id);
            if (record == null) return false;
            _records.Remove(record);
            RemoveGhost(id);
            return true;
        }

        private void PlanWork()
        {
            List<BlueprintRecord> ordered = new(_records);
            ordered.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            var reservedBuildItems = new Dictionary<ItemData, int>();
            foreach (TownJobView job in _jobs.Jobs)
            {
                if (job.Type != VillagerJobType.Build ||
                    string.IsNullOrEmpty(job.ConstructionId)) continue;
                BlueprintRecord owner = Find(job.ConstructionId);
                if (owner == null || !_catalog.TryGet(owner.ItemId, out ItemData reservedItem))
                    continue;
                reservedBuildItems.TryGetValue(reservedItem, out int count);
                reservedBuildItems[reservedItem] = count + 1;
            }
            foreach (BlueprintRecord record in ordered)
            {
                if (_jobs.HasActiveConstructionJob(record.Id)) continue;
                if (!_catalog.TryGet(record.ItemId, out ItemData item))
                {
                    record.BlockedReason = "The placeable item is missing from the item catalog.";
                    continue;
                }

                reservedBuildItems.TryGetValue(item, out int reserved);
                if (_town.StockpileInventory.GetCount(item) > reserved)
                {
                    if (_jobs.IsTargetInvalid(VillagerJobType.Build, this,
                            record.Position))
                    {
                        record.BlockedReason =
                            "The construction destination is temporarily unreachable.";
                        continue;
                    }
                    if (_jobs.TryIssue(new TownJobRequest(VillagerJobType.Build,
                            record.Position, record.WorkRequired, record.Priority,
                            this, item, null, record.Id), out _))
                    {
                        record.BlockedReason = string.Empty;
                        reservedBuildItems[item] = reserved + 1;
                    }
                    continue;
                }

                var visited = new HashSet<string>(StringComparer.Ordinal);
                if (TryPlanItem(item, record, visited, 0, out string blocked))
                    record.BlockedReason = string.Empty;
                else
                    record.BlockedReason = blocked;
            }
        }

        private bool TryPlanItem(ItemData wanted, BlueprintRecord blueprint,
            HashSet<string> visited, int depth, out string reason)
        {
            reason = string.Empty;
            if (wanted == null || depth > 16 || !visited.Add(wanted.persistentId))
            {
                reason = $"Recipe cycle detected while producing {wanted?.name ?? "an item"}.";
                return false;
            }
            if (_town.StockpileInventory.Contains(wanted)) return true;

            if (TryFindRecipe(wanted, out CraftingBenchComponent bench,
                    out CraftingRecipeData recipe))
            {
                foreach (CraftingRecipeData.RecipeIngredient ingredient in recipe.ingredients)
                {
                    if (ingredient == null) continue;
                    if (ingredient.matchType == CraftingRecipeData.IngredientMatchType.ExactItem)
                    {
                        if (_town.StockpileInventory.Contains(ingredient.itemData,
                                Mathf.Max(1, ingredient.count))) continue;
                        if (!TryPlanItem(ingredient.itemData, blueprint, visited,
                                depth + 1, out reason)) return false;
                        return true;
                    }
                    if (_town.StockpileInventory.Contains(ingredient.tag,
                            Mathf.Max(1, ingredient.count))) continue;
                    ItemData tagged = FindProducibleTaggedItem(ingredient.tag);
                    if (tagged == null)
                    {
                        reason = $"No source is available for ingredient tag {ingredient.tag?.name ?? "Unknown"}.";
                        return false;
                    }
                    if (!TryPlanItem(tagged, blueprint, visited, depth + 1,
                            out reason)) return false;
                    return true;
                }

                string dependencyId = DependencyId(blueprint.Id, "c", wanted.persistentId);
                if (_jobs.IsTargetInvalid(VillagerJobType.Craft, bench,
                        bench.transform.position))
                {
                    reason = $"The crafting station for {wanted.name} is temporarily unreachable.";
                    return false;
                }
                if (!_jobs.HasActiveConstructionJob(dependencyId))
                    _jobs.TryIssue(new TownJobRequest(VillagerJobType.Craft,
                        bench.transform.position, recipe.workRequired,
                        blueprint.Priority, bench, wanted, recipe, dependencyId), out _);
                return true;
            }

            if (_resources != null && _resources.HasSource(wanted))
            {
                string dependencyId = DependencyId(blueprint.Id, "g", wanted.persistentId);
                int required = 1;
                return _resources.TryIssueDemand(
                    wanted,
                    required,
                    blueprint.Priority,
                    dependencyId,
                    out reason);
            }

            reason = $"No town recipe or nearby harvestable source can produce {wanted.name}.";
            return false;
        }

        private bool TryFindRecipe(ItemData output, out CraftingBenchComponent bench,
            out CraftingRecipeData recipe)
        {
            bench = null; recipe = null;
            _town.RefreshBuildings();
            foreach (GameObject building in _town.Buildings)
            {
                CraftingBenchComponent candidate = building != null
                    ? building.GetComponentInChildren<CraftingBenchComponent>(true) : null;
                if (candidate == null) continue;
                foreach (CraftingRecipeData available in candidate.Recipes)
                {
                    if (available == null) continue;
                    foreach (CraftingRecipeData.RecipeOutput produced in available.output)
                        if (produced?.itemData == output)
                        { bench = candidate; recipe = available; return true; }
                }
            }
            return false;
        }

        private ItemData FindProducibleTaggedItem(EntityTag tag)
        {
            if (tag == null) return null;
            List<ItemData> items = new(_catalog.Items);
            items.Sort((a, b) => string.CompareOrdinal(a?.persistentId, b?.persistentId));
            foreach (ItemData item in items)
                if (item != null && item.HasTag(tag) &&
                    (_resources != null && _resources.HasSource(item) ||
                     TryFindRecipe(item, out _, out _)))
                    return item;
            return null;
        }

        private HarvestableObject FindHarvestSource(ItemData item)
        {
            foreach (HarvestableObject source in
                     FindObjectsByType<HarvestableObject>(FindObjectsSortMode.None))
                if (source != null && source.OutputItem == item &&
                    _town.ContainsResourcePosition(source.transform.position) &&
                    !_jobs.HasActiveTarget(
                        source.PersistentEntity is Component persistent
                            ? persistent : source,
                        VillagerJobType.Gather)) return source;
            return null;
        }

        private void ResolveComponents()
        {
            _town ??= GetComponent<TownCore>();
            _jobs ??= GetComponent<TownJobBoard>();
            _resources ??= GetComponent<VillagerResourceDiscovery>();
        }

        private void EnsureGhosts()
        {
            foreach (BlueprintRecord record in _records)
                if (!_ghosts.ContainsKey(record.Id)) CreateGhost(record);
        }

        private void RebuildGhosts()
        {
            ResolveComponents();
            EnsureGhosts();
        }

        private void CreateGhost(BlueprintRecord record)
        {
            if (_ghosts.ContainsKey(record.Id) || _catalog == null ||
                !_catalog.TryGet(record.ItemId, out ItemData item)) return;
            var ghost = new GameObject($"Construction Ghost - {item.name}");
            ghost.transform.position = record.Position;
            ghost.transform.SetParent(transform, true);
            var renderer = ghost.AddComponent<SpriteRenderer>();
            renderer.sprite = ResolvePreview(item);
            renderer.color = new Color(0.35f, 0.85f, 1f, 0.45f);
            renderer.sortingOrder = 100;
            _ghosts[record.Id] = ghost;
        }

        private static Sprite ResolvePreview(ItemData item)
        {
            if (item.TryGetActionData(out PlacePersistentNodeItemActionData node) &&
                node.node != null)
            {
                if (node.node.sprite != null) return node.node.sprite;
                if (node.node.sprites is { Length: > 0 }) return node.node.sprites[0];
            }
            if (item.TryGetActionData(out PlaceTileItemActionData tile) &&
                tile.tile?.TileBase is Tile unityTile && unityTile.sprite != null)
                return unityTile.sprite;
            return item.sprite;
        }

        private BlueprintRecord Find(string id) =>
            _records.Find(record => string.Equals(record.Id, id, StringComparison.Ordinal));

        private static string DependencyId(string blueprintId, string kind, string itemId)
        {
            string owner = blueprintId?.Length > 24
                ? blueprintId.Substring(0, 24) : blueprintId ?? string.Empty;
            string hash = Hash128.Compute(itemId ?? string.Empty).ToString();
            return $"{owner}:{kind}:{hash.Substring(0, 16)}";
        }

        private void RemoveGhost(string id)
        {
            if (!_ghosts.Remove(id, out GameObject ghost) || ghost == null) return;
            Destroy(ghost);
        }

        public void WriteState(BinaryWriter writer)
        {
            writer.Write(_records.Count);
            foreach (BlueprintRecord record in _records)
            {
                writer.Write(record.Id); writer.Write(record.ItemId);
                writer.Write(record.Position.x); writer.Write(record.Position.y);
                writer.Write(record.Position.z); writer.Write(record.WorkRequired);
                writer.Write((byte)record.Priority); writer.Write(record.BlockedReason ?? string.Empty);
            }
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (savedVersion != Version) throw new InvalidDataException(
                $"Unsupported town construction queue version {savedVersion}.");
            _records.Clear();
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxBlueprints)
                throw new InvalidDataException("Invalid construction blueprint count.");
            for (int i = 0; i < count; i++)
                _records.Add(new BlueprintRecord(reader.ReadString(), reader.ReadString(),
                    new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    reader.ReadSingle(), (TownJobPriority)reader.ReadByte())
                    { BlockedReason = reader.ReadString() });
            RebuildGhosts();
        }

        public bool IsAtBaseline() => _records.Count == 0;

        [Serializable]
        public sealed class BlueprintRecord
        {
            public string Id { get; }
            public string ItemId { get; }
            public Vector3 Position { get; }
            public float WorkRequired { get; }
            public TownJobPriority Priority { get; set; }
            public string BlockedReason { get; set; }

            public BlueprintRecord(string id, string itemId, Vector3 position,
                float workRequired, TownJobPriority priority)
            { Id = id; ItemId = itemId; Position = position; WorkRequired = workRequired; Priority = priority; BlockedReason = string.Empty; }
        }
    }
}
