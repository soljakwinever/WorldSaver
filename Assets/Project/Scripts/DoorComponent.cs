using System;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using Project.Scripts.Bus;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public enum DoorAccessPolicy : byte
    {
        Owner,
        Village,
        Faction,
        Neutral,
        Enemy
    }

    /// <summary>
    /// Persistent tile-backed door. Both tile states remain room boundaries;
    /// their TileBase assets control whether the door blocks movement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DoorComponent : MonoBehaviour, IInteractable,
        IEntityComponent, IPersistentComponent, IPersistenceInteractionGate,
        IEntityRemovalHandler
    {
        private static readonly System.Collections.Generic.Dictionary<
            Vector2Int,
            DoorComponent> DoorsByCell = new();

        public const ushort TypeId = 13;
        private const ushort CurrentVersion = 4;

        private Chunk _chunk;
        private Grid _grid;
        private Transform _doorTransform;
        private TileData _closedTile;
        private TileData _openTile;
        private int _legacyClosedTileId;
        private int _legacyOpenTileId;
        private string _openPrompt;
        private string _closePrompt;
        private bool _startsOpen;
        private bool _isOpen;
        private bool _persistenceReady;
        private Vector3Int _cell;
        private bool _hasPersistedCell;
        private EntityPersistenceKind _persistenceKind;
        private bool _allowLegacyMigration;
        private DoorAccessPolicy _accessPolicy;
        private string _ownerId;
        private string _villageId;
        private string _factionId;
        private string _spawnOwnerId;
        private string _spawnVillageId;
        private string _spawnFactionId;
        private bool _locked;
        private int _lockpickDifficulty;
        private int _breakHealth;
        private int _startingBreakHealth;
        private bool _startsLocked;
        private bool _accessBypassed;
        private readonly System.Collections.Generic.HashSet<string>
            _automaticUsers = new(StringComparer.Ordinal);
        private bool _openedAutomatically;
        private MapSignalBus _mapSignals;

        public IPersistentEntity PersistentEntity { get; set; }
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;
        public bool IsOpen => _isOpen;
        public bool IsLocked => _locked;
        public DoorAccessPolicy AccessPolicy => _accessPolicy;
        public string OwnerId => _ownerId;
        public string VillageId => _villageId;
        public string FactionId => _factionId;

        internal DoorPathingState CapturePathingState() => new(
            true,
            _isOpen,
            _locked,
            _accessBypassed,
            _accessPolicy,
            _ownerId,
            _villageId,
            _factionId);

        [Inject]
        public void Construct(Grid grid, MapSignalBus mapSignals)
        {
            _grid = grid;
            _mapSignals = mapSignals;
        }

        public void Initialize(
            Chunk chunk,
            Transform doorTransform,
            TileData closedTile,
            TileData openTile,
            int legacyClosedTileId,
            int legacyOpenTileId,
            EntityPersistenceKind persistenceKind,
            bool startsOpen,
            string openPrompt,
            string closePrompt,
            DoorAccessPolicy accessPolicy = DoorAccessPolicy.Neutral,
            AccessIdentity accessIdentity = default,
            bool startsLocked = false,
            int lockpickDifficulty = 1,
            int breakHealth = 25)
        {
            UnregisterPathingHint();
            _chunk = chunk;
            _allowLegacyMigration =
                chunk != null && !chunk.IsPersistenceRestoreCompleted;
            _doorTransform = doorTransform != null ? doorTransform : transform;
            _closedTile = closedTile;
            _openTile = openTile;
            _legacyClosedTileId = legacyClosedTileId;
            _legacyOpenTileId = legacyOpenTileId;
            _persistenceKind = persistenceKind;
            _startsOpen = startsOpen;
            _isOpen = startsOpen;
            _cell = PositionToCell(GetPosition());
            _openPrompt = string.IsNullOrWhiteSpace(openPrompt)
                ? "Open door"
                : openPrompt;
            _closePrompt = string.IsNullOrWhiteSpace(closePrompt)
                ? "Close door"
                : closePrompt;
            _accessPolicy = accessPolicy;
            _spawnOwnerId = Normalize(accessIdentity.OwnerId);
            _spawnVillageId = Normalize(accessIdentity.VillageId);
            _spawnFactionId = Normalize(accessIdentity.FactionId);
            _ownerId = _spawnOwnerId;
            _villageId = _spawnVillageId;
            _factionId = _spawnFactionId;
            _locked = startsLocked;
            _startsLocked = startsLocked;
            _lockpickDifficulty = Mathf.Max(1, lockpickDifficulty);
            _breakHealth = Mathf.Max(1, breakHealth);
            _startingBreakHealth = _breakHealth;

            ValidateConfiguration();
        }

        public Vector3 GetPosition() =>
            _doorTransform != null ? _doorTransform.position : transform.position;

        public bool CanInteract(InteractionContext context) =>
            context.interactionType == InteractionType.Direct &&
            context.user != null &&
            _persistenceReady &&
            HasValidConfiguration();

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context))
                return;

            bool nextOpen = !_isOpen;
            if (nextOpen && _locked)
                return;
            if (!nextOpen && _automaticUsers.Count > 0)
                return;
            if (ApplyState(nextOpen))
            {
                _isOpen = nextOpen;
                NotifyPathingChanged();
            }
        }

        public string GetInteractionPrompt(InteractionContext context)
        {
            if (context.interactionType != InteractionType.Direct)
                return string.Empty;

            return _isOpen ? _closePrompt : _openPrompt;
        }

        public void SetPersistenceReady(bool ready)
        {
            _persistenceReady = ready;
            if (!ready)
            {
                UnregisterPathingHint();
                return;
            }

            if (!_hasPersistedCell &&
                _allowLegacyMigration &&
                _chunk != null &&
                _chunk.TryClaimLegacyEntityTileOverride(
                    _isOpen
                        ? GetLegacyTileId(_openTile, _legacyOpenTileId)
                        : GetLegacyTileId(_closedTile, _legacyClosedTileId),
                    _isOpen
                        ? GetLegacyTileId(_closedTile, _legacyClosedTileId)
                        : GetLegacyTileId(_openTile, _legacyOpenTileId),
                    out Vector3Int legacyCell))
            {
                _cell = legacyCell;
            }
            else if (!_hasPersistedCell)
            {
                _cell = PositionToCell(GetPosition());
            }

            _hasPersistedCell = true;
            SetTransformToCell(_cell);
            ApplyState(_isOpen);
            RegisterPathingHint();
        }

        public void OnRemovedFromWorld()
        {
            UnregisterPathingHint();
            if (_chunk == null)
                return;

            _cell = PositionToCell(GetPosition());
            _chunk.TryClearTile(_cell, PersistentTileLayer.Wall);
            _chunk.TryClearTransientWallTile(_cell);
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            Vector3Int cell = PositionToCell(GetPosition());
            writer.Write(cell.x);
            writer.Write(cell.y);
            writer.Write(_isOpen);
            writer.Write(_locked);
            writer.Write(_breakHealth);
            writer.Write(_accessBypassed);
            writer.Write(_ownerId ?? string.Empty);
            writer.Write(_villageId ?? string.Empty);
            writer.Write(_factionId ?? string.Empty);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion == 1)
            {
                _isOpen = reader.ReadBoolean();
                _hasPersistedCell = false;
            }
            else if (savedVersion >= 2 && savedVersion <= CurrentVersion)
            {
                _cell = new Vector3Int(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    0);
                _isOpen = reader.ReadBoolean();
                if (savedVersion >= 3)
                {
                    _locked = reader.ReadBoolean();
                    _breakHealth = Mathf.Max(0, reader.ReadInt32());
                    _accessBypassed = reader.ReadBoolean();
                }
                if (savedVersion >= 4)
                {
                    _ownerId = RestoreIdentity(
                        reader.ReadString(), _spawnOwnerId);
                    _villageId = RestoreIdentity(
                        reader.ReadString(), _spawnVillageId);
                    _factionId = RestoreIdentity(
                        reader.ReadString(), _spawnFactionId);
                }
                _hasPersistedCell = true;
                SetTransformToCell(_cell);
            }
            else
            {
                throw new InvalidDataException(
                    $"Unsupported door state version {savedVersion}.");
            }
            if (_persistenceReady)
                ApplyState(_isOpen);
        }

        public bool IsAtBaseline() =>
            _persistenceKind != EntityPersistenceKind.RuntimeSpawned &&
            _isOpen == _startsOpen &&
            _locked == _startsLocked &&
            _breakHealth == _startingBreakHealth &&
            !_accessBypassed &&
            PositionToCell(GetPosition()) == _cell;

        private bool ApplyState(bool open)
        {
            if (!HasValidConfiguration())
                return false;

            _cell = PositionToCell(GetPosition());
            TileData tile = open ? _openTile : _closedTile;
            return _chunk.TrySetTransientWallTile(
                _cell,
                tile,
                tile.Color);
        }

        public bool AllowsFreeTraversal(PathFindingQuery query)
        {
            if (_locked)
                return false;
            if (_accessBypassed)
                return true;
            DoorAccessPolicy relationship = GetRelationship(query);
            return relationship <= _accessPolicy;
        }

        public bool TryOpenFor(PathFindingQuery query)
        {
            if (_isOpen)
                return true;
            if (_locked || !AllowsFreeTraversal(query))
                return false;
            if (!ApplyState(true))
                return false;

            _isOpen = true;
            NotifyPathingChanged();
            return true;
        }

        public bool TryAcquireAutomaticTraversal(
            string actorId,
            PathFindingQuery query)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                return false;
            if (_automaticUsers.Contains(actorId))
                return true;

            bool wasOpen = _isOpen;
            if (!wasOpen && !TryOpenFor(query))
                return false;
            if (_automaticUsers.Count == 0)
                _openedAutomatically = !wasOpen;
            _automaticUsers.Add(actorId);
            return true;
        }

        public void ReleaseAutomaticTraversal(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId) ||
                !_automaticUsers.Remove(actorId) ||
                _automaticUsers.Count > 0)
            {
                return;
            }

            if (_openedAutomatically && _isOpen && HasValidConfiguration() &&
                ApplyState(false))
            {
                _isOpen = false;
                NotifyPathingChanged();
            }
            _openedAutomatically = false;
        }

        public Vector2Int WorldCell => new(_cell.x, _cell.y);

        public bool TryLockpick(int skill)
        {
            if (!_locked)
                return true;
            if (skill < _lockpickDifficulty)
                return false;

            _locked = false;
            _accessBypassed = true;
            NotifyPathingChanged();
            return true;
        }

        public bool TryBreak(int damage)
        {
            if (damage <= 0)
                return false;

            _breakHealth -= damage;
            if (_breakHealth > 0)
                return false;

            _locked = false;
            _accessBypassed = true;
            if (!ApplyState(true))
                return false;
            _isOpen = true;
            NotifyPathingChanged();
            return true;
        }

        public static bool TryGetAt(
            Vector2Int worldCell,
            out DoorComponent door)
        {
            return DoorsByCell.TryGetValue(worldCell, out door) &&
                   door != null &&
                   door.isActiveAndEnabled;
        }

        private DoorAccessPolicy GetRelationship(PathFindingQuery query)
        {
            if (!string.IsNullOrEmpty(_ownerId) &&
                string.Equals(
                    _ownerId,
                    query.ActorId,
                    StringComparison.Ordinal))
                return DoorAccessPolicy.Owner;
            if (!string.IsNullOrEmpty(_villageId) &&
                string.Equals(
                    _villageId,
                    query.VillageId,
                    StringComparison.Ordinal))
                return DoorAccessPolicy.Village;
            if (!string.IsNullOrEmpty(_factionId) &&
                string.Equals(
                    _factionId,
                    query.FactionId,
                    StringComparison.Ordinal))
                return DoorAccessPolicy.Faction;
            if (string.IsNullOrEmpty(query.FactionId) ||
                string.IsNullOrEmpty(_factionId))
                return DoorAccessPolicy.Neutral;
            return DoorAccessPolicy.Enemy;
        }

        private void RegisterPathingHint()
        {
            DoorsByCell[new Vector2Int(_cell.x, _cell.y)] = this;
            NotifyPathingChanged();
        }

        private void UnregisterPathingHint()
        {
            Vector2Int cell = new(_cell.x, _cell.y);
            if (DoorsByCell.TryGetValue(cell, out DoorComponent current) &&
                current == this)
            {
                DoorsByCell.Remove(cell);
                _mapSignals?.RaiseNavigationCellChanged(cell);
            }
        }

        private void NotifyPathingChanged()
        {
            _mapSignals?.RaiseNavigationCellChanged(
                new Vector2Int(_cell.x, _cell.y));
        }

        private void OnDisable()
        {
            _automaticUsers.Clear();
            _openedAutomatically = false;
            UnregisterPathingHint();
        }

        private void OnEnable()
        {
            if (_persistenceReady && _hasPersistedCell)
                RegisterPathingHint();
        }

        private static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        private static string RestoreIdentity(string saved, string spawned)
        {
            string persisted = Normalize(saved);
            return !string.IsNullOrEmpty(persisted)
                ? persisted
                : Normalize(spawned);
        }

        private Vector3Int PositionToCell(Vector3 position)
        {
            Vector3Int cell = _grid != null
                ? _grid.WorldToCell(position)
                : Vector3Int.FloorToInt(position);
            cell.z = 0;
            return cell;
        }

        private void SetTransformToCell(Vector3Int cell)
        {
            if (_doorTransform == null)
                return;

            Vector3 position = _grid != null
                ? _grid.CellToWorld(cell)
                : new Vector3(cell.x, cell.y, 0f);
            position.z = _doorTransform.position.z;
            _doorTransform.position = position;
        }

        private bool HasValidConfiguration() =>
            _chunk != null &&
            IsDoorTile(_closedTile) &&
            IsDoorTile(_openTile);

        private void ValidateConfiguration()
        {
            if (_chunk == null)
                Debug.LogError("Door component requires its owning chunk.", this);

            if (!IsDoorTile(_closedTile) || !IsDoorTile(_openTile))
            {
                Debug.LogError(
                    "Door open and closed tiles must both be wall tiles with " +
                    "EnclosesRoom enabled and a visual assigned.",
                    this);
            }
        }

        private static bool IsDoorTile(TileData tile) =>
            tile != null &&
            tile.IsWall &&
            tile.EnclosesRoom &&
            tile.HasVisual;

        private static int GetLegacyTileId(TileData tile, int fallbackId) =>
            tile != null && tile.TileId > 0 ? tile.TileId : fallbackId;
    }

    internal readonly struct DoorPathingState
    {
        public readonly bool HasDoor;
        private readonly bool _isOpen;
        private readonly bool _locked;
        private readonly bool _accessBypassed;
        private readonly DoorAccessPolicy _accessPolicy;
        private readonly string _ownerId;
        private readonly string _villageId;
        private readonly string _factionId;

        public DoorPathingState(bool hasDoor, bool isOpen, bool locked,
            bool accessBypassed, DoorAccessPolicy accessPolicy,
            string ownerId, string villageId, string factionId)
        {
            HasDoor = hasDoor;
            _isOpen = isOpen;
            _locked = locked;
            _accessBypassed = accessBypassed;
            _accessPolicy = accessPolicy;
            _ownerId = ownerId ?? string.Empty;
            _villageId = villageId ?? string.Empty;
            _factionId = factionId ?? string.Empty;
        }

        public bool Allows(PathFindingQuery query)
        {
            if (_isOpen || _accessBypassed) return true;
            if (_locked) return false;
            DoorAccessPolicy relationship;
            if (!string.IsNullOrEmpty(_ownerId) &&
                string.Equals(_ownerId, query.ActorId,
                    StringComparison.Ordinal))
                relationship = DoorAccessPolicy.Owner;
            else if (!string.IsNullOrEmpty(_villageId) &&
                     string.Equals(_villageId, query.VillageId,
                         StringComparison.Ordinal))
                relationship = DoorAccessPolicy.Village;
            else if (!string.IsNullOrEmpty(_factionId) &&
                     string.Equals(_factionId, query.FactionId,
                         StringComparison.Ordinal))
                relationship = DoorAccessPolicy.Faction;
            else if (string.IsNullOrEmpty(query.FactionId) ||
                     string.IsNullOrEmpty(_factionId))
                relationship = DoorAccessPolicy.Neutral;
            else
                relationship = DoorAccessPolicy.Enemy;
            return relationship <= _accessPolicy;
        }
    }
}
