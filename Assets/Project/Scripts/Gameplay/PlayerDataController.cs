using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.Bus;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
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
    [RequireComponent(typeof(PlayerEquipmentController))]
    [RequireComponent(typeof(SkillRuntime))]
    public sealed class PlayerDataController : MonoBehaviour, IHasHealth, IHasNeeds, IHasMana, IHasStats,
        IPersistentComponent, ISkillStamina
    {
        public const ushort TypeId = 10;
        private const ushort CurrentComponentVersion = 5;
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

        [Header("Respawn")]
        [SerializeField] private bool hasSpawnPoint;
        [SerializeField] private Vector3 spawnPoint;
        [SerializeField] private bool hasSpawnTown;
        [SerializeField] private ulong spawnTownId;
        [SerializeField] private List<VisitedTown> visitedTowns = new();

        [Serializable]
        public sealed class VisitedTown
        {
            public ulong PersistentId;
            public string Name;
            public Vector3 SpawnPoint;
        }

        [Header("Death")]
        [SerializeField] private bool resolveDeathInstantly = true;
        [SerializeField, Min(1)] private long deathDropLifetimeTicks = 1800;

        [InjectOptional] private IWorldClock _worldClock;
        [InjectOptional] private IComponentWindowService _windowService;
        [InjectOptional] private IWorldGenerator _worldGenerator;

        private PersistentHealth _health;
        private PlayerBus _playerBus;
        private EntityBus _entityBus;
        private PlayerNeedsController _needsController;
        private PlayerEquipmentController _equipment;
        private float _nextAutoSaveTime;
        private bool _loaded;
        private bool _subscribedToEnemyDefeats;
        private bool _deathInProgress;

        public event Action<PlayerDataController> DeathStarted;
        public event Action<PlayerDataController> Respawned;
        public int Health => _health.Health;
        public int MaxHealth => _health.MaxHealth;
        public float Hunger { get => hunger; set => hunger = Mathf.Clamp01(value); }
        public float Energy { get => energy; set => energy = Mathf.Clamp01(value); }
        public float Mana { get => mana; set => mana = Mathf.Clamp01(value); }
        public int Level => level;
        public int Experience => experience;
        public int ExperienceToNextLevel => GetExperienceRequired(level);
        public int UnspentStatPoints => unspentStatPoints;
        public int Strength => Mathf.Max(
            1, strength + GetEquipmentModifier(EquipmentStat.Strength));
        public int Constitution => Mathf.Max(
            1, constitution +
               GetEquipmentModifier(EquipmentStat.Constitution));
        public int Dexterity => Mathf.Max(
            1, dexterity + GetEquipmentModifier(EquipmentStat.Dexterity));
        public int Wisdom => Mathf.Max(
            1, wisdom + GetEquipmentModifier(EquipmentStat.Wisdom));
        public int Intelligence => Mathf.Max(
            1, intelligence +
               GetEquipmentModifier(EquipmentStat.Intelligence));
        public int Luck => Mathf.Max(
            1, luck + GetEquipmentModifier(EquipmentStat.Luck));
        public int Defense => Mathf.Max(
            0, GetEquipmentModifier(EquipmentStat.Defense));
        public int MaxEnergy => Mathf.Max(
            1,
            BaseEnergy + (Constitution - BaseStat) * 10 +
            GetEquipmentModifier(EquipmentStat.MaximumEnergy));
        public int CurrentEnergy => Mathf.RoundToInt(energy * MaxEnergy);
        public int MaxMana => Mathf.Max(
            1,
            BaseMana + (Wisdom - BaseStat) * 10 +
            GetEquipmentModifier(EquipmentStat.MaximumMana));
        public int CurrentMana => Mathf.RoundToInt(mana * MaxMana);
        public float CurrentStamina => CurrentEnergy;
        public float MaximumStamina => MaxEnergy;
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
        public string PlayerId =>
            SanitizePathSegment(characterId, "player");
        public bool HasSpawnPoint => hasSpawnPoint;
        public Vector3 SpawnPoint => spawnPoint;
        public bool IsDeathInProgress => _deathInProgress;
        public DeathDropContainer LastDeathDrop { get; private set; }

        public void RestoreMana(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            if (amount == 0 || MaxMana <= 0)
                return;

            Mana += (float)amount / MaxMana;
        }

        public bool TrySpendStamina(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (CurrentStamina + 0.0001f < amount)
                return false;
            if (amount > 0f)
                Energy -= amount / MaxEnergy;
            return true;
        }

        [Inject]
        public void Construct(
            PlayerBus playerBus,
            EntityBus entityBus,
            IAttackService attackService = null)
        {
            UnsubscribeFromEnemyDefeats();
            _playerBus = playerBus;
            _entityBus = entityBus;
            SkillRuntime skillRuntime = GetComponent<SkillRuntime>() ??
                                        gameObject.AddComponent<SkillRuntime>();
            if (attackService != null)
                skillRuntime.Initialize(attackService);
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
            _equipment = GetComponent<PlayerEquipmentController>();
            if (_equipment == null)
                _equipment =
                    gameObject.AddComponent<PlayerEquipmentController>();
            ApplyConstitutionToHealth(healIncrease: false);
            SubscribeToHealth();

            // Zenject completes scene injection before Awake. Put every player
            // at the shared world spawn immediately so chunk loading, cameras,
            // and other Start callbacks all observe the same deterministic
            // position. A saved PersistentTransform may restore over this in
            // Start for an existing character.
            if (_worldGenerator != null)
                MoveToRespawnPosition(GetWorldSpawnPosition());
        }

        private void Start()
        {
            worldId = PlayerPrefs.GetString(
                "WorldSaver.ActiveWorld",
                worldId);
            bool restored = TryLoad();
            if (!restored && _worldGenerator != null)
                MoveToRespawnPosition(GetWorldSpawnPosition());
            _loaded = true;
            _nextAutoSaveTime = Time.unscaledTime + autoSaveInterval;
            RaiseProgressionChanged();
        }

        private void Update()
        {
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
            if (_health != null)
                _health.Died -= OnHealthDepleted;
            UnsubscribeFromEnemyDefeats();
            if (_loaded)
                TrySave();
        }

        private void OnEnable()
        {
            SubscribeToHealth();
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
                PlayerStat.Strength => Strength,
                PlayerStat.Constitution => Constitution,
                PlayerStat.Dexterity => Dexterity,
                PlayerStat.Wisdom => Wisdom,
                PlayerStat.Intelligence => Intelligence,
                PlayerStat.Luck => Luck,
                _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null)
            };
        }

        public int GetEquipmentStat(EquipmentStat stat) =>
            GetEquipmentModifier(stat);

        public int GetAttackDamageBonus(PlayerAttackType attackType)
        {
            int governingStat = attackType switch
            {
                PlayerAttackType.Melee => Strength,
                PlayerAttackType.Ranged => Dexterity,
                PlayerAttackType.Magic => Intelligence,
                _ => BaseStat
            };
            EquipmentStat attackStat = attackType switch
            {
                PlayerAttackType.Melee => EquipmentStat.MeleeAttack,
                PlayerAttackType.Ranged => EquipmentStat.RangedAttack,
                PlayerAttackType.Magic => EquipmentStat.MagicAttack,
                _ => EquipmentStat.MeleeAttack
            };
            return Mathf.Max(
                0,
                governingStat - BaseStat +
                GetEquipmentModifier(attackStat));
        }

        public int MitigateDamage(int damage)
        {
            if (damage < 0)
                throw new ArgumentOutOfRangeException(nameof(damage));

            return Mathf.Max(0, damage - Defense);
        }

        internal void NotifyEquipmentChanged()
        {
            ApplyConstitutionToHealth(healIncrease: true);
            _playerBus?.RaiseStatsChanged();
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
            _needsController ??= GetComponent<PlayerNeedsController>();
            _needsController?.SetMoving(moving);
        }

        public void SetSpawnPoint(Vector3 worldPosition)
        {
            if (!IsFinite(worldPosition))
                throw new ArgumentOutOfRangeException(
                    nameof(worldPosition),
                    "Spawn point coordinates must be finite.");

            spawnPoint = worldPosition;
            hasSpawnPoint = true;
            hasSpawnTown = false;
            spawnTownId = 0;
        }

        public void SetSpawnTown(TownCore town)
        {
            if (town == null)
                throw new ArgumentNullException(nameof(town));

            SetSpawnPoint(town.SpawnPoint);
            if (town.PersistentEntity != null)
            {
                spawnTownId = town.PersistentEntity.Id.value;
                hasSpawnTown = true;
            }

            if (_loaded)
                TrySave();
        }

        public bool IsSpawnTown(TownCore town)
        {
            if (!hasSpawnPoint || town == null)
                return false;

            if (hasSpawnTown && town.PersistentEntity != null)
            {
                return spawnTownId ==
                       town.PersistentEntity.Id.value;
            }

            return (spawnPoint - town.SpawnPoint).sqrMagnitude <
                   0.0001f;
        }

        public bool TryGetSpawnPoint(out Vector3 worldPosition)
        {
            worldPosition = spawnPoint;
            return hasSpawnPoint;
        }

        public IReadOnlyList<VisitedTown> VisitedTowns => visitedTowns;

        public void RegisterTownVisit(TownCore town)
        {
            if (town == null || town.PersistentEntity == null)
                return;
            ulong id = town.PersistentEntity.Id.value;
            VisitedTown record = visitedTowns.Find(candidate => candidate.PersistentId == id);
            if (record == null)
            {
                record = new VisitedTown { PersistentId = id };
                visitedTowns.Add(record);
            }
            record.Name = town.TownName;
            record.SpawnPoint = town.SpawnPoint;
            if (_loaded)
                TrySave();
        }

        public bool RespawnAtSpawnPoint()
        {
            if (!hasSpawnPoint)
                return false;

            transform.position = spawnPoint;
            if (TryGetComponent(out Rigidbody2D body))
            {
                body.position = spawnPoint;
                body.linearVelocity = Vector2.zero;
            }
            return true;
        }

        public void CompleteDeath()
        {
            if (!_deathInProgress)
                return;

            Vector3 deathPosition = transform.position;
            DropNonToolbarItems(deathPosition);
            MoveToRespawnPosition(ResolveRespawnPosition());
            hunger = 0.25f;
            energy = 1f;
            _health.SetHealth(Mathf.Min(20, _health.MaxHealth));
            _deathInProgress = false;
            Respawned?.Invoke(this);
        }

        public void ResolveDeathImmediately()
        {
            if (_health.Health > 0)
                _health.SetHealth(0);
            if (_deathInProgress)
                CompleteDeath();
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
            writer.Write(hasSpawnPoint);
            if (hasSpawnPoint)
            {
                writer.Write(spawnPoint.x);
                writer.Write(spawnPoint.y);
                writer.Write(spawnPoint.z);
            }
            writer.Write(hasSpawnTown);
            if (hasSpawnTown)
                writer.Write(spawnTownId);
            writer.Write(visitedTowns.Count);
            foreach (VisitedTown town in visitedTowns)
            {
                writer.Write(town.PersistentId);
                writer.Write(town.Name ?? "Town");
                writer.Write(town.SpawnPoint.x);
                writer.Write(town.SpawnPoint.y);
                writer.Write(town.SpawnPoint.z);
            }
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

            hasSpawnPoint = false;
            spawnPoint = default;
            hasSpawnTown = false;
            spawnTownId = 0;
            if (savedVersion >= 3)
            {
                bool restoredHasSpawnPoint = reader.ReadBoolean();
                if (restoredHasSpawnPoint)
                {
                    Vector3 restoredSpawnPoint = new(
                        reader.ReadSingle(),
                        reader.ReadSingle(),
                        reader.ReadSingle());
                    if (!IsFinite(restoredSpawnPoint))
                    {
                        throw new InvalidDataException(
                            "Saved player spawn point is invalid.");
                    }

                    spawnPoint = restoredSpawnPoint;
                    hasSpawnPoint = true;
                }
            }
            if (savedVersion >= 4)
            {
                hasSpawnTown = reader.ReadBoolean();
                if (hasSpawnTown)
                    spawnTownId = reader.ReadUInt64();
            }
            visitedTowns.Clear();
            if (savedVersion >= 5)
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > 10000)
                    throw new InvalidDataException("Saved visited-town count is invalid.");
                for (int i = 0; i < count; i++)
                {
                    VisitedTown town = new()
                    {
                        PersistentId = reader.ReadUInt64(),
                        Name = reader.ReadString(),
                        SpawnPoint = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
                    };
                    if (!IsFinite(town.SpawnPoint))
                        throw new InvalidDataException("Saved visited-town position is invalid.");
                    visitedTowns.Add(town);
                }
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
                   luck == BaseStat &&
                   !hasSpawnPoint &&
                   !hasSpawnTown &&
                   visitedTowns.Count == 0;
        }

        private void OnHealthDepleted()
        {
            if (_deathInProgress)
                return;

            _deathInProgress = true;
            DeathStarted?.Invoke(this);
            if (resolveDeathInstantly)
                CompleteDeath();
        }

        private void SubscribeToHealth()
        {
            if (_health == null)
                _health = GetComponent<PersistentHealth>();
            if (_health == null)
                return;

            _health.Died -= OnHealthDepleted;
            _health.Died += OnHealthDepleted;
        }

        private void DropNonToolbarItems(Vector3 deathPosition)
        {
            PersistentInventory inventory = GetComponent<PersistentInventory>();
            PlayerToolbarController toolbar =
                GetComponent<PlayerToolbarController>();
            if (inventory == null || inventory.OccupiedSlots == 0)
                return;

            var protectedItems = new HashSet<ItemData>();
            if (toolbar != null)
            {
                foreach (IHotbarAction action in toolbar.HotbarActions)
                {
                    if (action is ItemActionBinding { ItemData: not null } binding)
                        protectedItems.Add(binding.ItemData);
                }
            }

            var droppedStacks = new List<IItemStack>();
            var removals = new List<InventoryChange>();
            foreach (IItemStack stack in inventory.Stacks)
            {
                if (protectedItems.Contains(stack.Item))
                    continue;

                droppedStacks.Add(new ItemStack(
                    stack.Item,
                    stack.Count,
                    stack.Rarity,
                    stack.Durability));
                removals.Add(new InventoryChange(
                    stack.Item,
                    -stack.Count,
                    stack.Rarity,
                    stack.Durability));
            }

            if (droppedStacks.Count == 0 ||
                !inventory.TryApplyChanges(removals))
                return;

            LastDeathDrop = DeathDropContainer.Create(
                deathPosition,
                droppedStacks,
                deathDropLifetimeTicks,
                _worldClock,
                _windowService);
        }

        private Vector3 ResolveRespawnPosition()
        {
            if (!hasSpawnPoint)
                return GetWorldSpawnPosition();

            if (hasSpawnTown &&
                TownCoreRegistry.TryGetAvailable(
                    new NodeId(spawnTownId),
                    out TownCore town))
            {
                // Follow the selected core if its configured spawn offset or
                // transform changed since the player selected it.
                spawnPoint = town.SpawnPoint;
            }

            // The saved coordinate remains authoritative when the core's chunk
            // is not loaded or registry initialization has not completed.
            return spawnPoint;
        }

        private Vector3 GetWorldSpawnPosition()
        {
            if (_worldGenerator == null)
                return Vector3.zero;

            Vector2Int cell = _worldGenerator.WorldSpawnPosition;
            return new Vector3(cell.x, cell.y, transform.position.z);
        }

        private void MoveToRespawnPosition(Vector3 position)
        {
            transform.position = position;
            if (!TryGetComponent(out Rigidbody2D body))
                return;

            body.position = position;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
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
                BaseHealth + (Constitution - BaseStat) * 10 +
                GetEquipmentModifier(EquipmentStat.MaximumHealth);
            _health.SetMaxHealth(
                Mathf.Max(1, maximumHealth),
                healIncrease);
        }

        private int GetEquipmentModifier(EquipmentStat stat)
        {
            _equipment ??= GetComponent<PlayerEquipmentController>();
            int equipment = _equipment?.GetStatModifier(stat) ?? 0;
            SkillRuntime skills = GetComponent<SkillRuntime>();
            return checked(equipment + (skills?.GetStatModifier(stat) ?? 0));
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

        private bool TryLoad()
        {
            try
            {
                return Load();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
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

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

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
            deathDropLifetimeTicks = Math.Max(1, deathDropLifetimeTicks);
            if (hasSpawnPoint && !IsFinite(spawnPoint))
            {
                hasSpawnPoint = false;
                spawnPoint = default;
            }
        }
    }
}
