using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public enum PlayerStat
    {
        Strength,
        Constitution,
        Dexterity,
        Wisdom,
        Intelligence,
        Luck
    }

    [RequireComponent(typeof(PersistentInventory))]
    [RequireComponent(typeof(PersistentHealth))]
    [RequireComponent(typeof(PersistentTransform))]
    [RequireComponent(typeof(PlayerToolbarController))]
    public sealed class PlayerDataController : MonoBehaviour, IHasHealth, IHasNeeds, IHasStats,
        IPersistentComponent
    {
        public const ushort TypeId = 10;
        private const ushort CurrentComponentVersion = 2;
        private const ushort CurrentFileVersion = 1;
        private const uint FileMagic = 0x43535750; // PWSC
        private const int BaseStat = 5;
        private const int StatPointsPerLevel = 5;
        private const int BaseHealth = 100;
        private const int BaseEnergy = 100;
        private const int BaseMana = 50;

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

        [Header("Progression")]
        [SerializeField, Min(1)] private int level = 1;
        [SerializeField, Min(0)] private int experience;
        [SerializeField, Min(0)] private int unspentStatPoints;

        [Header("Attributes")]
        [SerializeField, Min(1)] private int strength = BaseStat;
        [SerializeField, Min(1)] private int constitution = BaseStat;
        [SerializeField, Min(1)] private int dexterity = BaseStat;
        [SerializeField, Min(1)] private int wisdom = BaseStat;
        [SerializeField, Min(1)] private int intelligence = BaseStat;
        [SerializeField, Min(1)] private int luck = BaseStat;
        [SerializeField, Range(0f, 1f)] private float mana = 1f;

        [Inject] private WorldData _worldData;

        private PersistentHealth _health;
        private PlayerBus _playerBus;
        private EntityBus _entityBus;
        private float _energyDrainMultiplier = 1f;
        private float _nextAutoSaveTime;
        private bool _loaded;
        private bool _subscribedToEnemyDefeats;

        public int Health => _health.Health;
        public int MaxHealth => _health.MaxHealth;
        public float Hunger { get => hunger; set => hunger = Mathf.Clamp01(value); }
        public float Energy { get => energy; set => energy = Mathf.Clamp01(value); }
        public float Mana { get => mana; set => mana = Mathf.Clamp01(value); }
        public int Level => level;
        public int Experience => experience;
        public int ExperienceToNextLevel => GetExperienceRequired(level);
        public int UnspentStatPoints => unspentStatPoints;
        public int Strength => strength;
        public int Constitution => constitution;
        public int Dexterity => dexterity;
        public int Wisdom => wisdom;
        public int Intelligence => intelligence;
        public int Luck => luck;
        public int MaxEnergy => BaseEnergy + (constitution - BaseStat) * 10;
        public int CurrentEnergy => Mathf.RoundToInt(energy * MaxEnergy);
        public int MaxMana => BaseMana + (wisdom - BaseStat) * 10;
        public int CurrentMana => Mathf.RoundToInt(mana * MaxMana);
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

        [Inject]
        public void Construct(PlayerBus playerBus, EntityBus entityBus)
        {
            UnsubscribeFromEnemyDefeats();
            _playerBus = playerBus;
            _entityBus = entityBus;
            if (isActiveAndEnabled)
                SubscribeToEnemyDefeats();
        }

        private void Awake()
        {
            _health = GetComponent<PersistentHealth>();
            if (_health == null)
                _health = gameObject.AddComponent<PersistentHealth>();
            if (GetComponent<PersistentTransform>() == null)
                gameObject.AddComponent<PersistentTransform>();
            ApplyConstitutionToHealth(healIncrease: false);
        }

        private void Start()
        {
            worldId = PlayerPrefs.GetString(
                "WorldSaver.ActiveWorld",
                worldId);
            TryLoad();
            _loaded = true;
            _nextAutoSaveTime = Time.unscaledTime + autoSaveInterval;
            RaiseProgressionChanged();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (hunger > 0f && energy < 1f)
            {
                hunger = Mathf.Clamp01(hunger -
                    hungerDrainRate * _worldData.playerSettings.hungerRate * deltaTime);
                energy = Mathf.Clamp01(energy +
                    hungerEnergyRegenerationRate *
                    BaseEnergy / (float)MaxEnergy * deltaTime);
            }

            energy = Mathf.Clamp01(energy -
                energyDrainRate * _energyDrainMultiplier *
                BaseEnergy / (float)MaxEnergy *
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
            UnsubscribeFromEnemyDefeats();
            if (_loaded)
                TrySave();
        }

        private void OnEnable()
        {
            SubscribeToEnemyDefeats();
        }

        public void TakeDamage(int damage) => _health.TakeDamage(damage);
        public void Heal(int amount) => _health.Heal(amount);

        public void AddExperience(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0)
                return;

            long newExperience = (long)experience + amount;
            while (newExperience >= GetExperienceRequired(level))
            {
                newExperience -= GetExperienceRequired(level);
                level++;
                unspentStatPoints =
                    checked(unspentStatPoints + StatPointsPerLevel);
                _playerBus?.RaiseLevelUp(level, StatPointsPerLevel);
            }

            experience = (int)Math.Min(newExperience, int.MaxValue);
            RaiseProgressionChanged();
        }

        public bool TrySpendStatPoint(PlayerStat stat)
        {
            if (unspentStatPoints <= 0)
                return false;

            switch (stat)
            {
                case PlayerStat.Strength:
                    strength++;
                    break;
                case PlayerStat.Constitution:
                    constitution++;
                    ApplyConstitutionToHealth(healIncrease: true);
                    break;
                case PlayerStat.Dexterity:
                    dexterity++;
                    break;
                case PlayerStat.Wisdom:
                    wisdom++;
                    break;
                case PlayerStat.Intelligence:
                    intelligence++;
                    break;
                case PlayerStat.Luck:
                    luck++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stat), stat, null);
            }

            unspentStatPoints--;
            _playerBus?.RaiseStatsChanged();
            return true;
        }

        public int GetStat(PlayerStat stat)
        {
            return stat switch
            {
                PlayerStat.Strength => strength,
                PlayerStat.Constitution => constitution,
                PlayerStat.Dexterity => dexterity,
                PlayerStat.Wisdom => wisdom,
                PlayerStat.Intelligence => intelligence,
                PlayerStat.Luck => luck,
                _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null)
            };
        }

        public int GetAttackDamageBonus(PlayerAttackType attackType)
        {
            int governingStat = attackType switch
            {
                PlayerAttackType.Melee => strength,
                PlayerAttackType.Ranged => dexterity,
                PlayerAttackType.Magic => intelligence,
                _ => BaseStat
            };
            return Mathf.Max(0, governingStat - BaseStat);
        }

        public static int GetExperienceRequired(int currentLevel)
        {
            if (currentLevel < 1)
                throw new ArgumentOutOfRangeException(nameof(currentLevel));

            long levelOffset = currentLevel - 1L;
            long required =
                100L + 50L * levelOffset + 25L * levelOffset * levelOffset;
            return (int)Math.Min(required, int.MaxValue);
        }

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
            writer.Write(level);
            writer.Write(experience);
            writer.Write(unspentStatPoints);
            writer.Write(strength);
            writer.Write(constitution);
            writer.Write(dexterity);
            writer.Write(wisdom);
            writer.Write(intelligence);
            writer.Write(luck);
            writer.Write(mana);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion == 0 || savedVersion > CurrentComponentVersion)
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

            if (savedVersion >= 2)
            {
                int restoredLevel = reader.ReadInt32();
                int restoredExperience = reader.ReadInt32();
                int restoredPoints = reader.ReadInt32();
                int restoredStrength = reader.ReadInt32();
                int restoredConstitution = reader.ReadInt32();
                int restoredDexterity = reader.ReadInt32();
                int restoredWisdom = reader.ReadInt32();
                int restoredIntelligence = reader.ReadInt32();
                int restoredLuck = reader.ReadInt32();
                float restoredMana = reader.ReadSingle();

                if (restoredLevel < 1 || restoredExperience < 0 ||
                    restoredExperience >= GetExperienceRequired(restoredLevel) ||
                    restoredPoints < 0 || restoredStrength < 1 ||
                    restoredConstitution < 1 || restoredDexterity < 1 ||
                    restoredWisdom < 1 || restoredIntelligence < 1 ||
                    restoredLuck < 1 || !IsUnitValue(restoredMana))
                {
                    throw new InvalidDataException(
                        "Saved player progression contains invalid values.");
                }

                level = restoredLevel;
                experience = restoredExperience;
                unspentStatPoints = restoredPoints;
                strength = restoredStrength;
                constitution = restoredConstitution;
                dexterity = restoredDexterity;
                wisdom = restoredWisdom;
                intelligence = restoredIntelligence;
                luck = restoredLuck;
                mana = restoredMana;
            }

            ApplyConstitutionToHealth(healIncrease: false);
        }

        public bool IsAtBaseline()
        {
            return Mathf.Approximately(hunger, 1f) &&
                   Mathf.Approximately(energy, 1f) &&
                   Mathf.Approximately(mana, 1f) &&
                   level == 1 &&
                   experience == 0 &&
                   unspentStatPoints == 0 &&
                   strength == BaseStat &&
                   constitution == BaseStat &&
                   dexterity == BaseStat &&
                   wisdom == BaseStat &&
                   intelligence == BaseStat &&
                   luck == BaseStat;
        }

        private void OnEnemyDefeated(
            EnemyData enemy,
            Vector3 position,
            int experienceValue,
            GameObject defeatedBy)
        {
            if (defeatedBy == null ||
                defeatedBy.GetComponentInParent<PlayerDataController>() != this)
                return;
            AddExperience(experienceValue);
        }

        private void SubscribeToEnemyDefeats()
        {
            if (_subscribedToEnemyDefeats || _entityBus == null)
                return;
            _entityBus.EnemyDefeated += OnEnemyDefeated;
            _subscribedToEnemyDefeats = true;
        }

        private void UnsubscribeFromEnemyDefeats()
        {
            if (!_subscribedToEnemyDefeats || _entityBus == null)
                return;
            _entityBus.EnemyDefeated -= OnEnemyDefeated;
            _subscribedToEnemyDefeats = false;
        }

        private void ApplyConstitutionToHealth(bool healIncrease)
        {
            if (_health == null)
                return;
            int maximumHealth =
                BaseHealth + (constitution - BaseStat) * 10;
            _health.SetMaxHealth(
                Mathf.Max(1, maximumHealth),
                healIncrease);
        }

        private void RaiseProgressionChanged()
        {
            _playerBus?.RaiseExperienceChanged(
                level,
                experience,
                ExperienceToNextLevel);
            _playerBus?.RaiseStatsChanged();
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
            level = Mathf.Max(1, level);
            experience = Mathf.Clamp(
                experience,
                0,
                GetExperienceRequired(level) - 1);
            unspentStatPoints = Mathf.Max(0, unspentStatPoints);
            strength = Mathf.Max(1, strength);
            constitution = Mathf.Max(1, constitution);
            dexterity = Mathf.Max(1, dexterity);
            wisdom = Mathf.Max(1, wisdom);
            intelligence = Mathf.Max(1, intelligence);
            luck = Mathf.Max(1, luck);
            mana = Mathf.Clamp01(mana);
            autoSaveInterval = Mathf.Max(1f, autoSaveInterval);
        }
    }
}
