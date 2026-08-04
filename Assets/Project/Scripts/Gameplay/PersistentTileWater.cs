using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class PersistentTileWater : MonoBehaviour, IPersistentComponent,
        IOfflineSimulatable
    {
        public const ushort TypeId = 0x5754; // WT
        private const ushort Version = 1;
        private readonly Dictionary<int, float> _water = new();
        private Vector2Int _chunkPosition;
        private IPlantTileContext _tileContext;
        private long _lastHourlyTick;
        private float _ticksPerHour = 60f;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => Version;
        public void Initialize(
            Vector2Int chunkPosition,
            IPlantTileContext tileContext = null,
            long currentTick = 0,
            float ticksPerHour = 60f)
        {
            _chunkPosition = chunkPosition;
            _tileContext = tileContext;
            _lastHourlyTick = currentTick;
            _ticksPerHour = Mathf.Max(1f, ticksPerHour);
            _water.Clear();
        }

        public float GetWater(Vector3Int worldCell) =>
            TryGetKey(worldCell, out int key) && _water.TryGetValue(key, out float value)
                ? value : 0f;

        public float AddWater(Vector3Int worldCell, float amount, float maximum)
        {
            if (amount < 0f) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!TryGetKey(worldCell, out int key)) return 0f;
            float value = Mathf.Clamp(GetWater(worldCell) + amount, 0f, maximum);
            if (value > 0f) _water[key] = value; else _water.Remove(key);
            RefreshColor(worldCell);
            return value;
        }

        public float ConsumeWater(Vector3Int worldCell, float amount)
        {
            float available = GetWater(worldCell);
            float consumed = Mathf.Min(Mathf.Max(0f, amount), available);
            if (TryGetKey(worldCell, out int key))
            {
                float remaining = available - consumed;
                if (remaining > 0f) _water[key] = remaining; else _water.Remove(key);
                RefreshColor(worldCell);
            }
            return consumed;
        }

        public void WriteState(BinaryWriter writer)
        {
            writer.Write(_water.Count);
            List<int> keys = new(_water.Keys);
            keys.Sort();
            foreach (int key in keys)
            { writer.Write((byte)(key & 0xff)); writer.Write((byte)(key >> 8)); writer.Write(_water[key]); }
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (savedVersion != Version) throw new InvalidDataException($"Unsupported tile-water version {savedVersion}.");
            _water.Clear();
            int count = reader.ReadInt32();
            if (count < 0 || count > 1024) throw new InvalidDataException("Invalid watered-tile count.");
            for (int i = 0; i < count; i++)
            {
                int x = reader.ReadByte(); int y = reader.ReadByte(); float value = reader.ReadSingle();
                if (x >= 32 || y >= 32 || float.IsNaN(value) || value <= 0f) throw new InvalidDataException("Invalid watered tile.");
                _water[(y << 8) | x] = value;
            }
            RefreshAllColors();
        }

        public bool IsAtBaseline() => _water.Count == 0;

        public void Tick(long currentTick)
        {
            if (currentTick <= _lastHourlyTick) return;
            long hours = (long)((currentTick - _lastHourlyTick) / _ticksPerHour);
            if (hours <= 0) return;
            _lastHourlyTick += (long)(hours * _ticksPerHour);
            ApplyHourlyMoisture(hours);
        }

        public void SimulateOffline(long fromTick, long toTick,
            Project.Scripts.DataTypes.SaveData.OfflineSimulationPolicy policy)
        {
            if (policy == Project.Scripts.DataTypes.SaveData.OfflineSimulationPolicy.None ||
                policy == Project.Scripts.DataTypes.SaveData.OfflineSimulationPolicy.Regional ||
                toTick <= fromTick) return;
            long hours = (long)((toTick - fromTick) / _ticksPerHour);
            if (hours > 0) ApplyHourlyMoisture(hours);
            _lastHourlyTick = Math.Max(_lastHourlyTick, toTick);
        }

        public void RefreshAllColors()
        {
            if (_tileContext == null) return;
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    RefreshColor(ToWorldCell(x, y));
        }

        private void ApplyHourlyMoisture(long hours)
        {
            if (_tileContext == null || hours <= 0) return;
            bool wateringWeather = _tileContext.DoesWeatherWaterPlants(
                ToWorldCell(16, 16));
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    int key = (y << 8) | x;
                    Vector3Int cell = ToWorldCell(x, y);
                    if (!_tileContext.TryGetPlantingTile(cell, out TileData tile) ||
                        tile == null || !tile.usesMoistureTint) continue;
                    _water.TryGetValue(key, out float current);
                    float rain = wateringWeather
                        ? tile.rainWaterPointsPerHour * hours
                        : 0f;
                    float remaining = Mathf.Clamp(
                        current + rain - tile.waterPointsLostPerHour * hours,
                        0f,
                        tile.maximumWaterPoints);
                    if (remaining > 0f) _water[key] = remaining; else _water.Remove(key);
                    RefreshColor(cell);
                }
            }
        }

        private void RefreshColor(Vector3Int worldCell)
        {
            if (_tileContext == null ||
                !_tileContext.TryGetPlantingTile(worldCell, out TileData tile) ||
                tile == null || !tile.usesMoistureTint) return;
            _tileContext.SetPlantWaterColor(worldCell,
                tile.GetMoistureColor(GetWater(worldCell)));
        }

        private Vector3Int ToWorldCell(int localX, int localY) => new(
            _chunkPosition.x * 32 + localX,
            _chunkPosition.y * 32 + localY,
            0);

        private bool TryGetKey(Vector3Int worldCell, out int key)
        {
            int x = worldCell.x - _chunkPosition.x * 32;
            int y = worldCell.y - _chunkPosition.y * 32;
            if ((uint)x >= 32 || (uint)y >= 32) { key = 0; return false; }
            key = (y << 8) | x; return true;
        }
    }
}
