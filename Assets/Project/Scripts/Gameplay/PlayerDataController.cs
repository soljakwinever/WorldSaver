using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [RequireComponent(typeof(PersistentInventory))]
    [RequireComponent(typeof(PersistentHealth))]
    [RequireComponent(typeof(PersistentTransform))]
    public sealed class PlayerDataController : MonoBehaviour, IHasHealth, IHasNeeds, IHasStats,
        IPersistentComponent
    {
        public const ushort TypeId = 10;
        private const ushort CurrentComponentVersion = 1;
        private const ushort CurrentFileVersion = 1;
        private const uint FileMagic = 0x43535750; // PWSC

        [Header("Character Save")]
        [SerializeField] private string worldId = "default";
        [SerializeField] private string characterId = "player";
        [SerializeField, Min(1f)] private float autoSaveInterval = 30f;

        [Header("Needs")]
        [SerializeField, Range(0f, 1f)] private float hunger = 1f;
        [SerializeField, Range(0f, 1f)] private float energy = 1f;
        [SerializeField, Min(0f)] private float energyDrainRate = 0.005f;
        [SerializeField, Min(0f)] private float hungerEnergyRegenerationRate = 0.0175f;
        [SerializeField, Min(0f)] private float hungerDrainRate = 0.0025f;

        [Inject] private WorldData _worldData;

        private PersistentHealth _health;
        private float _energyDrainMultiplier = 1f;
        private float _nextAutoSaveTime;
        private bool _loaded;

        public int Health => _health.Health;
        public float Hunger { get => hunger; set => hunger = Mathf.Clamp01(value); }
        public float Energy { get => energy; set => energy = Mathf.Clamp01(value); }
        public float EnergyDrainRate { get => energyDrainRate; set => energyDrainRate = Mathf.Max(0f, value); }
        public float HungerEnergyRegenerationRate
        {
            get => hungerEnergyRegenerationRate;
            set => hungerEnergyRegenerationRate = Mathf.Max(0f, value);
        }
        public float HungerDrainRate { get => hungerDrainRate; set => hungerDrainRate = Mathf.Max(0f, value); }
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentComponentVersion;
        public string CharacterFilePath => GetCharacterFilePath();

        private void Awake()
        {
            _health = GetComponent<PersistentHealth>();
            if (_health == null)
                _health = gameObject.AddComponent<PersistentHealth>();
            if (GetComponent<PersistentTransform>() == null)
                gameObject.AddComponent<PersistentTransform>();
        }

        private void Start()
        {
            TryLoad();
            _loaded = true;
            _nextAutoSaveTime = Time.unscaledTime + autoSaveInterval;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (hunger > 0f && energy < 1f)
            {
                hunger = Mathf.Clamp01(hunger -
                    hungerDrainRate * _worldData.playerSettings.hungerRate * deltaTime);
                energy = Mathf.Clamp01(energy + hungerEnergyRegenerationRate * deltaTime);
            }

            energy = Mathf.Clamp01(energy -
                energyDrainRate * _energyDrainMultiplier *
                _worldData.playerSettings.energyRate * deltaTime);

            if (_loaded && Time.unscaledTime >= _nextAutoSaveTime)
            {
                TrySave();
                _nextAutoSaveTime = Time.unscaledTime + autoSaveInterval;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && _loaded)
                TrySave();
        }

        private void OnApplicationQuit()
        {
            if (_loaded)
                TrySave();
        }

        private void OnDisable()
        {
            if (_loaded)
                TrySave();
        }

        public void TakeDamage(int damage) => _health.TakeDamage(damage);
        public void Heal(int amount) => _health.Heal(amount);

        public void SetWalking(bool moving)
        {
            _energyDrainMultiplier = moving
                ? _worldData.playerSettings.movementEnergyMulpiplier
                : 1f;
        }

        public void ConfigureSaveIdentity(string newWorldId, string newCharacterId)
        {
            if (_loaded)
                throw new InvalidOperationException(
                    "The character save identity must be configured before the player starts.");

            worldId = SanitizePathSegment(newWorldId, "default");
            characterId = SanitizePathSegment(newCharacterId, "player");
        }

        public void Save()
        {
            string path = GetCharacterFilePath();
            string directory = Path.GetDirectoryName(path);
            string tempPath = path + ".tmp";
            string backupPath = path + ".bak";

            Directory.CreateDirectory(directory);

            try
            {
                using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write,
                           FileShare.None, 4096, FileOptions.WriteThrough))
                using (BinaryWriter writer = new(stream))
                {
                    writer.Write(FileMagic);
                    writer.Write(CurrentFileVersion);

                    Dictionary<ushort, IPersistentComponent> components =
                        GetPersistentComponents();
                    writer.Write(components.Count);

                    foreach (IPersistentComponent component in components.Values)
                    {
                        using MemoryStream payload = new();
                        using (BinaryWriter payloadWriter = new(payload, System.Text.Encoding.UTF8, true))
                            component.WriteState(payloadWriter);

                        byte[] data = payload.ToArray();
                        writer.Write(component.PersistentTypeId);
                        writer.Write(component.PersistentVersion);
                        writer.Write(data.Length);
                        writer.Write(data);
                    }

                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                ReplaceFile(tempPath, path, backupPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        public bool Load()
        {
            string path = GetCharacterFilePath();
            if (!File.Exists(path))
                return false;

            Dictionary<ushort, IPersistentComponent> components = GetPersistentComponents();

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader reader = new(stream);

            if (reader.ReadUInt32() != FileMagic)
                throw new InvalidDataException("Not a WorldSaver character file.");

            ushort fileVersion = reader.ReadUInt16();
            if (fileVersion == 0 || fileVersion > CurrentFileVersion)
                throw new InvalidDataException($"Unsupported character file version {fileVersion}.");

            int componentCount = reader.ReadInt32();
            if (componentCount < 0 || componentCount > 256)
                throw new InvalidDataException($"Invalid character component count {componentCount}.");

            for (int i = 0; i < componentCount; i++)
            {
                ushort typeId = reader.ReadUInt16();
                ushort componentVersion = reader.ReadUInt16();
                int length = reader.ReadInt32();
                if (length < 0 || length > stream.Length - stream.Position)
                    throw new InvalidDataException($"Invalid payload length {length} for component {typeId}.");

                byte[] data = reader.ReadBytes(length);
                if (!components.TryGetValue(typeId, out IPersistentComponent component))
                    continue;

                using MemoryStream payload = new(data, writable: false);
                using BinaryReader payloadReader = new(payload);
                component.ReadState(payloadReader, componentVersion);
                if (payload.Position != payload.Length)
                    throw new InvalidDataException($"Component {typeId} did not consume its complete payload.");
            }

            return true;
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(hunger);
            writer.Write(energy);
            writer.Write(energyDrainRate);
            writer.Write(hungerEnergyRegenerationRate);
            writer.Write(hungerDrainRate);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion != CurrentComponentVersion)
                throw new InvalidDataException($"Unsupported player state version {savedVersion}.");

            float restoredHunger = reader.ReadSingle();
            float restoredEnergy = reader.ReadSingle();
            float restoredEnergyDrain = reader.ReadSingle();
            float restoredRegeneration = reader.ReadSingle();
            float restoredHungerDrain = reader.ReadSingle();

            if (!IsUnitValue(restoredHunger) || !IsUnitValue(restoredEnergy) ||
                !IsNonNegativeFinite(restoredEnergyDrain) ||
                !IsNonNegativeFinite(restoredRegeneration) ||
                !IsNonNegativeFinite(restoredHungerDrain))
                throw new InvalidDataException("Saved player needs contain invalid values.");

            hunger = restoredHunger;
            energy = restoredEnergy;
            energyDrainRate = restoredEnergyDrain;
            hungerEnergyRegenerationRate = restoredRegeneration;
            hungerDrainRate = restoredHungerDrain;
        }

        public bool IsAtBaseline()
        {
            return Mathf.Approximately(hunger, 1f) &&
                   Mathf.Approximately(energy, 1f);
        }

        private Dictionary<ushort, IPersistentComponent> GetPersistentComponents()
        {
            Dictionary<ushort, IPersistentComponent> result = new();
            foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is IPersistentComponent component &&
                    !result.TryAdd(component.PersistentTypeId, component))
                    throw new InvalidOperationException(
                        $"Duplicate player persistence component type {component.PersistentTypeId}.");
            }

            return result;
        }

        private string GetCharacterFilePath()
        {
            return Path.Combine(Application.persistentDataPath, "Worlds",
                SanitizePathSegment(worldId, "default"), "characters",
                SanitizePathSegment(characterId, "player") + ".wsc");
        }

        private void TrySave()
        {
            try
            {
                Save();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void TryLoad()
        {
            try
            {
                Load();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private static void ReplaceFile(string tempPath, string path, string backupPath)
        {
            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            try
            {
                File.Replace(tempPath, path, backupPath);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(path, backupPath, overwrite: true);
                File.Delete(path);
                File.Move(tempPath, path);
            }
        }

        private static string SanitizePathSegment(string value, string fallback)
        {
            string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            return result;
        }

        private static bool IsUnitValue(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;

        private static bool IsNonNegativeFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

        private void OnValidate()
        {
            hunger = Mathf.Clamp01(hunger);
            energy = Mathf.Clamp01(energy);
            energyDrainRate = Mathf.Max(0f, energyDrainRate);
            hungerEnergyRegenerationRate = Mathf.Max(0f, hungerEnergyRegenerationRate);
            hungerDrainRate = Mathf.Max(0f, hungerDrainRate);
            autoSaveInterval = Mathf.Max(1f, autoSaveInterval);
        }
    }
}
