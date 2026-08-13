using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PersistentInventory))]
    [RequireComponent(typeof(PersistentHealth))]
    public sealed class TownCore : MonoBehaviour, ITownCore, IEntityComponent,
        IPersistentComponent, IOfflineSimulatable, IInteractable
    {
        public const ushort TypeId = 14;
        private const ushort CurrentVersion = 1;
        private const float ResourceSearchRadiusBonus = 3f;

        [Header("Identity")]
        [SerializeField] private string townName = "New Village";
        [SerializeField] private string windowTitle = "Town Core";
        [SerializeField] private string interactionPrompt = "Visit town shrine";
        [SerializeField, Min(0f)] private float spawnPointOffset = 1.25f;

        [Header("Area")]
        [SerializeField, Min(0f)] private float townRadius = 12f;
        [SerializeField, Min(0f)] private float resourceRadius = 30f;
        [SerializeField] private LayerMask buildingLayerMask = ~0;
        [SerializeField, Min(1)] private long buildingRefreshTicks = 10;

        [Header("Population")]
        [SerializeField, Min(0)] private int baseMaxPopulation = 10;
        [SerializeField] private bool grantsProceduralStarterFood;
        [SerializeField] private ItemData starterFoodItem;
        [SerializeField, Min(0)] private int starterFoodPerResident = 2;

        [Header("Mana")]
        [SerializeField, Min(0f)] private float manaPool;
        [SerializeField, Min(0f)] private float baseMaxMana = 100f;
        [SerializeField, Min(0f)] private float passiveManaPerTick = 0.01f;
        [SerializeField, Min(1)] private long ticksPerOffering = 10;

        [Header("Progression")]
        [SerializeField] private TownEffect[] availableEffects =
            Array.Empty<TownEffect>();
        [SerializeField] private TownUpgradeDefinition[] upgrades =
            Array.Empty<TownUpgradeDefinition>();

        private readonly HashSet<string> _residentIds =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _activeEffectIds =
            new(StringComparer.Ordinal);
        private readonly List<GameObject> _buildings = new();
        private readonly Collider2D[] _buildingResults = new Collider2D[128];

        private PersistentInventory _inventory;
        private PersistentHealth _health;
        private TownStockpile _stockpile;
        private TownJobBoard _jobBoard;
        private IWorldClock _clock;
        private IComponentWindowService _windowService;
        private long _lastTick;
        private long _offeringProgressTicks;
        private long _nextBuildingRefreshTick;
        private bool _tickInitialized;
        private int _upgradeLevel;
        private int _baseMaximumHealth;

        public IPersistentEntity PersistentEntity { get; set; }
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;
        public string TownName => townName;

        /// <summary>Applies deterministic identity supplied by procedural generation before save restoration.</summary>
        public void SetGeneratedName(string generatedName)
        {
            if (!string.IsNullOrWhiteSpace(generatedName))
                townName = NormalizeName(generatedName);
        }
        public int Population => _residentIds.Count;
        public int UpgradeLevel => _upgradeLevel;
        public float ManaPool => manaPool;
        public Vector3 Position => transform.position;
        public IReadOnlyList<GameObject> Buildings => _buildings;
        public IInventory OfferingInventory => ResolveInventory();
        public IInventory StockpileInventory
        {
            get
            {
                ResolveComponents();
                return _stockpile;
            }
        }
        public ITownJobBoard JobBoard
        {
            get
            {
                ResolveComponents();
                return _jobBoard;
            }
        }
        public IReadOnlyCollection<string> ResidentIds => _residentIds;
        public IReadOnlyCollection<string> ActiveEffectIds => _activeEffectIds;
        public IReadOnlyList<TownEffect> AvailableEffects => availableEffects;
        public Vector3 SpawnPoint => Position + Vector3.down * spawnPointOffset;
        public bool IsAvailable =>
            isActiveAndEnabled && _health != null && _health.Health > 0;
        public TownEffect CurrentEffect
        {
            get
            {
                foreach (TownEffect effect in availableEffects)
                {
                    if (effect != null &&
                        _activeEffectIds.Contains(effect.PersistentId))
                        return effect;
                }

                return null;
            }
        }

        public float TownRadius =>
            townRadius + SumUpgrade(x => x.TownRadiusIncrease);
        public float ResourceRadius =>
            resourceRadius + SumUpgrade(x => x.ResourceRadiusIncrease) +
            ResourceSearchRadiusBonus;
        public int MaxPopulation =>
            baseMaxPopulation + SumUpgrade(x => x.PopulationCapacityIncrease);
        public float MaxMana =>
            baseMaxMana + SumUpgrade(x => x.ManaCapacityIncrease);

        [Inject]
        public void Construct(
            IWorldClock clock,
            IComponentWindowService windowService)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _windowService = windowService ??
                throw new ArgumentNullException(nameof(windowService));
        }

        public void Initialize(
            string initialName,
            float initialTownRadius,
            float initialResourceRadius,
            int maximumPopulation,
            float maximumMana,
            float manaPerTick,
            long offeringIntervalTicks,
            long structureRefreshTicks,
            LayerMask structureLayerMask,
            TownEffect[] effects,
            TownUpgradeDefinition[] upgradeDefinitions,
            string title = "Town Core",
            string prompt = "Visit town shrine",
            float playerSpawnOffset = 1.25f,
            bool grantStarterFood = false,
            ItemData configuredStarterFood = null,
            int configuredStarterFoodPerResident = 0)
        {
            townName = NormalizeName(initialName);
            windowTitle = string.IsNullOrWhiteSpace(title)
                ? "Town Core"
                : title.Trim();
            interactionPrompt = string.IsNullOrWhiteSpace(prompt)
                ? "Visit town shrine"
                : prompt.Trim();
            spawnPointOffset = Mathf.Max(0f, playerSpawnOffset);
            townRadius = Mathf.Max(0f, initialTownRadius);
            resourceRadius = Mathf.Max(townRadius, initialResourceRadius);
            baseMaxPopulation = Math.Max(0, maximumPopulation);
            grantsProceduralStarterFood = grantStarterFood;
            starterFoodItem = configuredStarterFood;
            starterFoodPerResident = Math.Max(
                0,
                configuredStarterFoodPerResident);
            baseMaxMana = Mathf.Max(0f, maximumMana);
            passiveManaPerTick = Mathf.Max(0f, manaPerTick);
            ticksPerOffering = Math.Max(1, offeringIntervalTicks);
            buildingRefreshTicks = Math.Max(1, structureRefreshTicks);
            buildingLayerMask = structureLayerMask;
            availableEffects = effects ?? Array.Empty<TownEffect>();
            upgrades = upgradeDefinitions ?? Array.Empty<TownUpgradeDefinition>();
            manaPool = Mathf.Clamp(manaPool, 0f, MaxMana);
            ValidateEffectIds();
            ApplyHealthUpgrade();
        }

        public bool CanInteract(InteractionContext context)
        {
            return context.interactionType == InteractionType.Direct &&
                   context.user != null &&
                   _windowService != null &&
                   context.user.GetComponentInParent<PersistentInventory>() !=
                   null;
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context))
                return;

            IInventory playerInventory =
                context.user.GetComponentInParent<PersistentInventory>();
            PlayerDataController player =
                context.user.GetComponentInParent<PlayerDataController>();
            player?.RegisterTownVisit(this);

            _windowService.Open(new ComponentWindowRequest(
                windowTitle,
                new Vector2(820f, 560f),
                new TownCoreWindowSection(
                    this,
                    playerInventory,
                    player)));
        }

        public string GetInteractionPrompt(InteractionContext context) =>
            context.interactionType == InteractionType.Direct
                ? interactionPrompt
                : string.Empty;

        public Vector3 GetPosition() => Position;

        private void OnEnable()
        {
            ResolveComponents();
            TownCoreRegistry.Register(this);
        }

        private void OnDisable()
        {
            TownCoreRegistry.Unregister(this);
        }

        private void Update()
        {
            if (_clock == null)
                return;

            long now = _clock.CurrentTick;
            if (!_tickInitialized)
            {
                _lastTick = now;
                _tickInitialized = true;
            }
            else if (now > _lastTick)
            {
                Simulate(_lastTick, now);
            }

            if (now >= _nextBuildingRefreshTick)
            {
                RefreshBuildings();
                _nextBuildingRefreshTick = checked(now + buildingRefreshTicks);
            }
        }

        public void SimulateOffline(
            long fromTick,
            long toTick,
            OfflineSimulationPolicy policy)
        {
            if (policy == OfflineSimulationPolicy.None || toTick <= fromTick)
                return;

            long start = _tickInitialized ? _lastTick : fromTick;
            _tickInitialized = true;
            Simulate(start, toTick);
        }

        private void Simulate(long fromTick, long toTick)
        {
            if (toTick <= fromTick)
                return;

            long remainingTicks = toTick - fromTick;
            while (remainingTicks > 0)
            {
                long activeEffectTicks = RunEffects(remainingTicks);
                if (activeEffectTicks > 0)
                {
                    remainingTicks -= activeEffectTicks;
                    continue;
                }

                long replenishmentTicks =
                    GetReplenishmentBatch(remainingTicks);
                ReplenishMana(replenishmentTicks);
                remainingTicks -= replenishmentTicks;
            }

            _lastTick = toTick;
        }

        private void ReplenishMana(long elapsedTicks)
        {
            manaPool = Mathf.Min(
                MaxMana,
                manaPool + elapsedTicks * passiveManaPerTick);

            _offeringProgressTicks =
                checked(_offeringProgressTicks + elapsedTicks);
            long offerings = _offeringProgressTicks / ticksPerOffering;
            _offeringProgressTicks %= ticksPerOffering;
            ConsumeOfferings(offerings);
        }

        private long GetReplenishmentBatch(long maximumTicks)
        {
            float nextManaCost = float.PositiveInfinity;
            foreach (TownEffect effect in availableEffects)
            {
                if (effect == null ||
                    !_activeEffectIds.Contains(effect.PersistentId) ||
                    effect.ManaCostPerTick <= manaPool ||
                    effect.ManaCostPerTick > MaxMana)
                {
                    continue;
                }

                nextManaCost = Mathf.Min(
                    nextManaCost,
                    effect.ManaCostPerTick);
            }

            long batch = maximumTicks;
            if (!float.IsPositiveInfinity(nextManaCost) &&
                passiveManaPerTick > 0f)
            {
                double missingMana = nextManaCost - manaPool;
                long ticksUntilAffordable = Math.Max(
                    1L,
                    (long)Math.Ceiling(missingMana / passiveManaPerTick));
                batch = Math.Min(batch, ticksUntilAffordable);
            }

            if (!float.IsPositiveInfinity(nextManaCost) &&
                manaPool < MaxMana &&
                HasUsableOffering())
            {
                long ticksUntilOffering =
                    ticksPerOffering - _offeringProgressTicks;
                batch = Math.Min(batch, Math.Max(1L, ticksUntilOffering));
            }

            return Math.Max(1L, batch);
        }

        private bool HasUsableOffering()
        {
            foreach (IItemStack stack in ResolveInventory().Stacks)
            {
                if (stack.Count > 0 &&
                    stack.Item != null &&
                    stack.Item.GetMagicValue(stack.Rarity) > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private void ConsumeOfferings(long maximumItems)
        {
            if (maximumItems <= 0 || manaPool >= MaxMana)
                return;

            PersistentInventory inventory = ResolveInventory();
            var snapshot = new List<IItemStack>(inventory.Stacks);
            foreach (IItemStack stack in snapshot)
            {
                float value = stack.Item.GetMagicValue(stack.Rarity);
                if (value <= 0f)
                    continue;

                int count = (int)Math.Min(maximumItems, stack.Count);
                int useful = Mathf.Min(
                    count,
                    Mathf.CeilToInt((MaxMana - manaPool) / value));
                if (useful <= 0)
                    break;
                if (!inventory.TryRemove(stack.Item, useful, stack.Rarity))
                    continue;

                manaPool = Mathf.Min(MaxMana, manaPool + useful * value);
                maximumItems -= useful;
                if (maximumItems <= 0 || manaPool >= MaxMana)
                    break;
            }
        }

        private long RunEffects(long elapsed)
        {
            long activeTicks = 0;
            foreach (TownEffect effect in availableEffects)
            {
                if (effect == null ||
                    !_activeEffectIds.Contains(effect.PersistentId))
                    continue;

                TownEffectExecution execution =
                    effect.Apply(this, elapsed, manaPool);
                if (!execution.PerformedWork)
                    continue;

                if (execution.ActiveTicks > elapsed ||
                    float.IsNaN(execution.ManaSpent) ||
                    float.IsInfinity(execution.ManaSpent) ||
                    execution.ManaSpent < 0f ||
                    execution.ManaSpent > manaPool + 0.0001f)
                {
                    Debug.LogError(
                        $"Town effect '{effect.name}' returned an invalid execution.",
                        effect);
                    continue;
                }

                manaPool = Mathf.Max(
                    0f,
                    manaPool - execution.ManaSpent);
                activeTicks = Math.Max(activeTicks, execution.ActiveTicks);
            }

            return activeTicks;
        }

        public void SetName(string value)
        {
            townName = NormalizeName(value);
        }

        public bool TryRegisterResident(string persistentId)
        {
            if (string.IsNullOrWhiteSpace(persistentId) ||
                _residentIds.Contains(persistentId) ||
                Population >= MaxPopulation)
                return false;

            if (!_residentIds.Add(persistentId))
                return false;

            if (grantsProceduralStarterFood && starterFoodItem != null &&
                starterFoodPerResident > 0)
            {
                ResolveComponents();
                if (_stockpile == null ||
                    !_stockpile.TryAdd(
                        starterFoodItem,
                        starterFoodPerResident,
                        out int remainder) ||
                    remainder != 0)
                {
                    Debug.LogWarning(
                        $"Town '{townName}' could not store all starter food " +
                        $"for resident '{persistentId}'.",
                        this);
                }
            }
            return true;
        }

        public bool UnregisterResident(string persistentId)
        {
            return !string.IsNullOrWhiteSpace(persistentId) &&
                   _residentIds.Remove(persistentId);
        }

        public bool TryIssueJob(TownJobRequest request, out long jobId)
        {
            ResolveComponents();
            if (_jobBoard == null)
            {
                jobId = 0;
                return false;
            }
            return _jobBoard.TryIssue(request, out jobId);
        }

        public bool SetEffectActive(string effectId, bool active)
        {
            if (string.IsNullOrWhiteSpace(effectId) ||
                FindEffect(effectId) == null)
                return false;

            return active
                ? _activeEffectIds.Add(effectId)
                : _activeEffectIds.Remove(effectId);
        }

        public bool IsEffectActive(string effectId) =>
            effectId != null && _activeEffectIds.Contains(effectId);

        public bool TryUpgrade()
        {
            if (_upgradeLevel >= (upgrades?.Length ?? 0))
                return false;

            TownUpgradeDefinition upgrade = upgrades[_upgradeLevel];
            if (upgrade == null)
                return false;

            var changes = new List<InventoryChange>();
            foreach (TownUpgradeDefinition.ItemCost cost in upgrade.Costs)
            {
                if (cost.item == null || cost.count <= 0)
                    return false;
                changes.Add(new InventoryChange(
                    cost.item,
                    -cost.count,
                    cost.rarity));
            }

            PersistentInventory inventory = ResolveInventory();
            if (changes.Count > 0 &&
                !inventory.TryApplyChanges(changes))
                return false;

            _upgradeLevel++;
            manaPool = Mathf.Min(manaPool, MaxMana);
            ApplyHealthUpgrade();
            RefreshBuildings();
            return true;
        }

        public bool ContainsTownPosition(Vector3 worldPosition) =>
            IsWithinRadius(worldPosition, TownRadius);

        public bool ContainsResourcePosition(Vector3 worldPosition) =>
            IsWithinRadius(worldPosition, ResourceRadius);

        public bool TryFastTravel(Transform traveller)
        {
            if (traveller == null || _health == null || _health.Health <= 0)
                return false;

            traveller.position = Position;
            return true;
        }

        public void RefreshBuildings()
        {
            _buildings.Clear();
            int count = Physics2D.OverlapCircleNonAlloc(
                Position,
                TownRadius,
                _buildingResults,
                buildingLayerMask);
            var seen = new HashSet<GameObject>();

            for (int i = 0; i < count; i++)
            {
                Collider2D collider = _buildingResults[i];
                if (collider == null || collider.transform.IsChildOf(transform) ||
                    transform.IsChildOf(collider.transform))
                    continue;

                IPersistentEntity entity =
                    collider.GetComponentInParent<IPersistentEntity>();
                Component entityComponent = entity as Component;
                GameObject building = entityComponent != null
                    ? entityComponent.gameObject
                    : collider.attachedRigidbody != null
                        ? collider.attachedRigidbody.gameObject
                        : collider.gameObject;
                if (seen.Add(building))
                    _buildings.Add(building);
            }
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(townName ?? string.Empty);
            writer.Write(manaPool);
            writer.Write(_upgradeLevel);
            writer.Write(_lastTick);
            writer.Write(_offeringProgressTicks);
            WriteStrings(writer, _residentIds);
            WriteStrings(writer, _activeEffectIds);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion != CurrentVersion)
                throw new InvalidDataException(
                    $"Unsupported Town Core state version {savedVersion}.");

            townName = NormalizeName(reader.ReadString());
            manaPool = reader.ReadSingle();
            _upgradeLevel = reader.ReadInt32();
            _lastTick = reader.ReadInt64();
            _offeringProgressTicks = reader.ReadInt64();

            if (float.IsNaN(manaPool) || float.IsInfinity(manaPool) ||
                manaPool < 0f ||
                _upgradeLevel < 0 ||
                _upgradeLevel > (upgrades?.Length ?? 0) ||
                _lastTick < 0 || _offeringProgressTicks < 0 ||
                _offeringProgressTicks >= ticksPerOffering)
            {
                throw new InvalidDataException("Town Core state is invalid.");
            }

            ReadStrings(reader, _residentIds);
            ReadStrings(reader, _activeEffectIds);
            if (_residentIds.Count > MaxPopulation)
                throw new InvalidDataException(
                    "Town Core population exceeds its capacity.");

            _tickInitialized = _lastTick != 0;
            manaPool = Mathf.Min(manaPool, MaxMana);
            ApplyHealthUpgrade();
        }

        public bool IsAtBaseline()
        {
            return townName == "New Village" &&
                   manaPool == 0f &&
                   _upgradeLevel == 0 &&
                   _lastTick == 0 &&
                   _offeringProgressTicks == 0 &&
                   _residentIds.Count == 0 &&
                   _activeEffectIds.Count == 0;
        }

        private PersistentInventory ResolveInventory()
        {
            if (_inventory == null)
                _inventory = GetComponent<PersistentInventory>();
            if (_inventory == null)
                throw new InvalidOperationException(
                    "TownCore requires a PersistentInventory.");
            return _inventory;
        }

        private void ResolveComponents()
        {
            ResolveInventory();
            if (_health == null)
                _health = GetComponent<PersistentHealth>();
            if (_health != null && _baseMaximumHealth <= 0)
                _baseMaximumHealth = _health.MaxHealth;
            _stockpile ??= GetComponent<TownStockpile>();
            _jobBoard ??= GetComponent<TownJobBoard>();
        }

        private void ApplyHealthUpgrade()
        {
            ResolveComponents();
            if (_health == null || _baseMaximumHealth <= 0)
                return;

            int bonus = SumUpgrade(x => x.MaximumHealthIncrease);
            _health.SetMaxHealth(checked(_baseMaximumHealth + bonus));
        }

        private float SumUpgrade(Func<TownUpgradeDefinition, float> selector)
        {
            float sum = 0f;
            int count = Math.Min(_upgradeLevel, upgrades?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                if (upgrades[i] != null)
                    sum += selector(upgrades[i]);
            }
            return sum;
        }

        private int SumUpgrade(Func<TownUpgradeDefinition, int> selector)
        {
            int sum = 0;
            int count = Math.Min(_upgradeLevel, upgrades?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                if (upgrades[i] != null)
                    sum = checked(sum + selector(upgrades[i]));
            }
            return sum;
        }

        private TownEffect FindEffect(string id)
        {
            foreach (TownEffect effect in availableEffects)
            {
                if (effect != null &&
                    string.Equals(
                        effect.PersistentId,
                        id,
                        StringComparison.Ordinal))
                    return effect;
            }
            return null;
        }

        private bool IsWithinRadius(Vector3 position, float radius)
        {
            Vector2 delta = position - Position;
            return delta.sqrMagnitude <= radius * radius;
        }

        private void ValidateEffectIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TownEffect effect in availableEffects)
            {
                if (effect == null)
                    continue;
                if (string.IsNullOrWhiteSpace(effect.PersistentId))
                    throw new InvalidOperationException(
                        "Every TownEffect requires a persistent ID.");
                if (!ids.Add(effect.PersistentId))
                    throw new InvalidOperationException(
                        $"Duplicate TownEffect ID '{effect.PersistentId}'.");
            }
        }

        private static string NormalizeName(string value) =>
            string.IsNullOrWhiteSpace(value) ? "New Village" : value.Trim();

        private static void WriteStrings(
            BinaryWriter writer,
            IEnumerable<string> values)
        {
            var ordered = new List<string>(values);
            ordered.Sort(StringComparer.Ordinal);
            writer.Write(ordered.Count);
            foreach (string value in ordered)
                writer.Write(value);
        }

        private static void ReadStrings(
            BinaryReader reader,
            HashSet<string> destination)
        {
            destination.Clear();
            int count = reader.ReadInt32();
            if (count < 0 || count > 100000)
                throw new InvalidDataException("Invalid Town Core string count.");
            for (int i = 0; i < count; i++)
            {
                string value = reader.ReadString();
                if (string.IsNullOrWhiteSpace(value) ||
                    !destination.Add(value))
                    throw new InvalidDataException(
                        "Town Core contains an invalid or duplicate identifier.");
            }
        }

        private void OnValidate()
        {
            townName = NormalizeName(townName);
            if (string.IsNullOrWhiteSpace(windowTitle))
                windowTitle = "Town Core";
            if (string.IsNullOrWhiteSpace(interactionPrompt))
                interactionPrompt = "Visit town shrine";
            spawnPointOffset = Mathf.Max(0f, spawnPointOffset);
            townRadius = Mathf.Max(0f, townRadius);
            resourceRadius = Mathf.Max(townRadius, resourceRadius);
            baseMaxPopulation = Math.Max(0, baseMaxPopulation);
            baseMaxMana = Mathf.Max(0f, baseMaxMana);
            passiveManaPerTick = Mathf.Max(0f, passiveManaPerTick);
            ticksPerOffering = Math.Max(1, ticksPerOffering);
            buildingRefreshTicks = Math.Max(1, buildingRefreshTicks);
            availableEffects ??= Array.Empty<TownEffect>();
            upgrades ??= Array.Empty<TownUpgradeDefinition>();
            manaPool = Mathf.Clamp(manaPool, 0f, MaxMana);
        }
    }
}
