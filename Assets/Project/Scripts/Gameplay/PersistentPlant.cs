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
    public sealed class PersistentPlant : MonoBehaviour, IPersistentComponent,
        IOfflineSimulatable, IInteractable, IEntityComponent
    {
        public const ushort TypeId = 0x504C; // PL
        private const ushort Version = 1;
        private PlantData _data;
        private IPlantTileContext _tileContext;
        private PersistentHealth _health;
        private SpriteRenderer _renderer;
        private IItemStackExplosionService _itemStackExplosion;
        private IWorldClock _clock;
        private ITimeController _time;
        private WorldData _worldData;
        private float _growthHours;
        private long _lastSimulatedTick;
        private ItemData.Rarity _seedRarity;

        public IPersistentEntity PersistentEntity { get; set; }
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => Version;
        public bool IsMature => _data != null && _growthHours >= _data.hoursToMature;
        public float WaterLevel => _tileContext?.GetPlantWater(Cell) ?? 0f;
        private Vector3Int Cell => Vector3Int.FloorToInt(transform.position);

        [Inject]
        public void Construct(IItemStackExplosionService itemStackExplosion,
            IWorldClock clock,
            ITimeController time, WorldData worldData)
        {
            _itemStackExplosion = itemStackExplosion;
            _clock = clock;
            _time = time;
            _worldData = worldData;
        }

        public void Initialize(PlantData data, IPlantTileContext tileContext)
        {
            _data = data; _tileContext = tileContext;
            _health = GetComponent<PersistentHealth>();
            _renderer = GetComponentInParent<SpriteRenderer>();
            _lastSimulatedTick = _clock?.CurrentTick ?? 0;
            RefreshSprite();
        }

        private void Update()
        {
            if (_clock == null || _data == null) return;
            SimulateTo(_clock.CurrentTick);
        }

        public void SetSeedRarity(ItemData.Rarity rarity)
        {
            if (!Enum.IsDefined(typeof(ItemData.Rarity), rarity)) throw new ArgumentOutOfRangeException(nameof(rarity));
            _seedRarity = rarity;
        }

        public bool CanPlantHere()
        {
            return _tileContext != null && _tileContext.TryGetPlantingTile(Cell, out TileData tile) &&
                   _data != null && _data.CanPlantOn(tile);
        }

        public float Water(float points) =>
            _tileContext?.AddPlantWater(Cell, points, _data.maximumWaterPoints) ?? 0f;

        public void SimulateOffline(long fromTick, long toTick, OfflineSimulationPolicy policy)
        {
            if (policy == OfflineSimulationPolicy.CatchUp || policy == OfflineSimulationPolicy.Derived)
            { _lastSimulatedTick = Math.Max(_lastSimulatedTick, fromTick); SimulateTo(toTick); }
        }

        private void SimulateTo(long tick)
        {
            if (tick <= _lastSimulatedTick || _data == null) return;
            float ticksPerHour = Mathf.Max(1f,
                (_worldData?.minutesPerDay ?? 24f) * 60f / 24f /
                (_clock is WorldClock worldClock ? worldClock.SecondsPerTick : 1f));
            float hours = (tick - _lastSimulatedTick) / ticksPerHour;
            _lastSimulatedTick = tick;
            bool validSeason = _time != null && _data.CanGrowIn(_time.Season);
            float wateredHours = hours;
            if (_data.waterPointsPerHour > 0f)
            {
                float consumed = _tileContext?.ConsumePlantWater(Cell, hours * _data.waterPointsPerHour) ?? 0f;
                wateredHours = consumed / _data.waterPointsPerHour;
            }
            float dryHours = Mathf.Max(0f, hours - wateredHours);
            if (validSeason) _growthHours = Mathf.Min(_data.hoursToMature, _growthHours + wateredHours);
            float damage = dryHours * _data.healthLostPerDryHour;
            if (!validSeason) damage += hours * _data.healthLostPerInvalidSeasonHour;
            if (damage > 0f && _health != null)
            {
                _health.SetHealth(_health.Health - Mathf.CeilToInt(damage));
                if (_health.Health <= 0 && PersistentEntity != null)
                { PersistentEntity.RemoveFromWorld(); return; }
            }
            RefreshSprite();
        }

        public bool CanInteract(InteractionContext context) =>
            context.interactionType == InteractionType.Direct &&
            context.user != null &&
            IsMature &&
            _itemStackExplosion != null;
        public Vector3 GetPosition() => transform.position;
        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context)) return;
            List<ItemStackExplosionEntry> drops = new();
            foreach (PlantData.HarvestYield yield in _data.yields)
            {
                if (yield.item != null)
                {
                    int minimum = Mathf.Max(1, yield.minimumQuantity);
                    int maximum = Mathf.Max(minimum, yield.maximumQuantity);
                    drops.Add(new ItemStackExplosionEntry(
                        yield.item,
                        UnityEngine.Random.Range(minimum, maximum + 1),
                        _seedRarity));
                }
            }

            _itemStackExplosion.Explode(drops, transform.position);
            if (_data.multipleHarvests)
            { _growthHours = _data.hoursToMature * _data.progressAfterHarvest; RefreshSprite(); }
            else
                PersistentEntity?.RemoveFromWorld();
        }

        public string GetInteractionPrompt(InteractionContext context) =>
            context.interactionType == InteractionType.Direct && IsMature
                ? $"Harvest {_data.name}"
                : string.Empty;

        public void WriteState(BinaryWriter writer)
        { writer.Write(_growthHours); writer.Write(_lastSimulatedTick); writer.Write((byte)_seedRarity); }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (savedVersion != Version) throw new InvalidDataException($"Unsupported plant version {savedVersion}.");
            _growthHours = reader.ReadSingle(); _lastSimulatedTick = reader.ReadInt64(); _seedRarity = (ItemData.Rarity)reader.ReadByte();
            if (_growthHours < 0f || !Enum.IsDefined(typeof(ItemData.Rarity), _seedRarity)) throw new InvalidDataException("Invalid plant state.");
            RefreshSprite();
        }

        public bool IsAtBaseline() => _growthHours == 0f && _lastSimulatedTick == 0 && _seedRarity == ItemData.Rarity.Common;

        private void RefreshSprite()
        {
            if (_renderer == null || _data == null) return;
            if (WaterLevel <= 0f && _data.wiltedSprite != null) { _renderer.sprite = _data.wiltedSprite; return; }
            float progress = _growthHours / Mathf.Max(0.01f, _data.hoursToMature);
            Sprite selected = null; float threshold = float.NegativeInfinity;
            foreach (PlantData.GrowthStage stage in _data.growthStages)
                if (stage.sprite != null && stage.progress <= progress && stage.progress >= threshold)
                { selected = stage.sprite; threshold = stage.progress; }
            if (selected != null) _renderer.sprite = selected;
        }
    }
}
