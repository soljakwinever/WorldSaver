using System;
using System.IO;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    /// <summary>
    /// Persistent tile-backed door. Both tile states remain room boundaries;
    /// their TileBase assets control whether the door blocks movement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DoorComponent : MonoBehaviour, IInteractable,
        IEntityComponent, IPersistentComponent, IPersistenceInteractionGate
    {
        public const ushort TypeId = 13;
        private const ushort CurrentVersion = 2;

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

        public IPersistentEntity PersistentEntity { get; set; }
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;
        public bool IsOpen => _isOpen;

        [Inject]
        public void Construct(Grid grid)
        {
            _grid = grid;
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
            string closePrompt)
        {
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
            if (ApplyState(nextOpen))
                _isOpen = nextOpen;
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
                return;

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
        }

        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            Vector3Int cell = PositionToCell(GetPosition());
            writer.Write(cell.x);
            writer.Write(cell.y);
            writer.Write(_isOpen);
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
            else if (savedVersion == CurrentVersion)
            {
                _cell = new Vector3Int(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    0);
                _isOpen = reader.ReadBoolean();
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
}
