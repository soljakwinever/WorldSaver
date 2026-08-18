using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.Core;
using Project.Scripts.Bus;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BedComponent : MonoBehaviour, IInteractable,
        IEntityComponent, IPersistentComponent, IEntityRemovalHandler
    {
        public const ushort TypeId = 0x4244; // BD
        private const ushort Version = 1;
        private static readonly HashSet<BedComponent> Active = new();

        private string _ownerVillagerId = string.Empty;
        private string _initialOwnerVillagerId = string.Empty;
        private VillagerEntityBridge _occupant;
        private float _healthPerSecond = 2f;
        private float _energyPerSecond = 8f;
        private string _prompt = "Sleep until morning";
        private IIndoorLocationService _rooms;
        private ITimeSkipController _time;
        private IWorldClock _worldClock;
        private PlayerDataController _player;
        private PlayerBus _playerBus;

        public static IReadOnlyCollection<BedComponent> All => Active;
        public IPersistentEntity PersistentEntity { get; set; }
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => Version;
        public string OwnerVillagerId => _ownerVillagerId;
        public VillagerEntityBridge Occupant => _occupant;
        public bool IsOwned => !string.IsNullOrEmpty(_ownerVillagerId);
        public bool IsOccupied => _occupant != null;
        public float HealthPerSecond => _healthPerSecond;
        public float EnergyPerSecond => _energyPerSecond;

        [Inject]
        public void Construct(IIndoorLocationService rooms, ITimeSkipController time,
            IWorldClock worldClock, PlayerDataController player,
            PlayerBus playerBus)
        {
            _rooms = rooms;
            _time = time;
            _worldClock = worldClock;
            _player = player;
            _playerBus = playerBus;
        }

        public void Initialize(float healthPerSecond, float energyPerSecond,
            string prompt, string initialOwnerVillagerId = null)
        {
            _healthPerSecond = Mathf.Max(0f, healthPerSecond);
            _energyPerSecond = Mathf.Max(0f, energyPerSecond);
            _prompt = string.IsNullOrWhiteSpace(prompt)
                ? "Sleep until morning" : prompt.Trim();
            _initialOwnerVillagerId = string.IsNullOrWhiteSpace(initialOwnerVillagerId)
                ? string.Empty : initialOwnerVillagerId.Trim();
            _ownerVillagerId = _initialOwnerVillagerId;
        }

        private void OnEnable() => Active.Add(this);

        private void OnDisable()
        {
            Active.Remove(this);
            _occupant?.NotifyBedRemoved(this);
            _occupant = null;
        }

        public Vector3 GetPosition() => transform.position;

        public bool IsIndoors
        {
            get
            {
                if (_rooms == null) return false;
                return _rooms.IsWorldPositionIndoors(transform.position);
            }
        }

        public string TownId
        {
            get
            {
                foreach (TownCore town in TownCoreRegistry.All)
                    if (town != null && town.IsAvailable &&
                        town.ContainsTownPosition(transform.position))
                        return town.PersistentEntity?.Id.ToString() ?? string.Empty;
                return string.Empty;
            }
        }

        public bool TryAssign(VillagerEntityBridge villager, out string reason)
        {
            reason = string.Empty;
            if (villager == null)
            {
                ClearOwner();
                return true;
            }
            if (!IsIndoors)
            {
                reason = "Beds must be indoors before they can be assigned.";
                return false;
            }
            string id = villager.PersistentEntity?.Id.ToString();
            if (string.IsNullOrEmpty(id) || !string.Equals(TownId, villager.TownId,
                    StringComparison.Ordinal))
            {
                reason = "The villager and bed must belong to the same town.";
                return false;
            }
            foreach (BedComponent bed in Active)
                if (bed != null && bed != this &&
                    string.Equals(bed._ownerVillagerId, id, StringComparison.Ordinal))
                    bed.ClearOwner();
            _ownerVillagerId = id;
            return true;
        }

        public void ClearOwner()
        {
            _occupant?.WakeFromBed();
            _occupant = null;
            _ownerVillagerId = string.Empty;
        }

        public bool TryOccupy(VillagerEntityBridge villager)
        {
            if (villager == null || _occupant != null && _occupant != villager ||
                !IsIndoors) return false;
            string id = villager.PersistentEntity?.Id.ToString();
            if (!string.Equals(id, _ownerVillagerId, StringComparison.Ordinal))
                return false;
            _occupant = villager;
            return true;
        }

        public void Release(VillagerEntityBridge villager)
        {
            if (_occupant == villager) _occupant = null;
        }

        public bool CanInteract(InteractionContext context) =>
            context.interactionType == InteractionType.Direct &&
            context.user != null && !IsOwned && !IsOccupied && _time != null;

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context)) return;
            _player?.Heal(_player.MaxHealth);
            if (_player != null) _player.Energy = 1f;
            foreach (VillagerEntityBridge villager in VillagerEntityBridge.All)
                villager?.ApplySkippedNightRecovery();
            _time.AdvanceToNextMorning(7);
            _playerBus?.RaiseSlept(transform.position);
            _worldClock?.Save();
        }

        public string GetInteractionPrompt(InteractionContext context)
        {
            if (context.interactionType != InteractionType.Direct) return string.Empty;
            if (IsOwned) return "Assigned bed";
            if (IsOccupied) return "Bed is occupied";
            return _prompt;
        }

        public void WriteState(BinaryWriter writer) =>
            writer.Write(_ownerVillagerId ?? string.Empty);

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (savedVersion != Version)
                throw new InvalidDataException($"Unsupported bed version {savedVersion}.");
            _ownerVillagerId = reader.ReadString();
        }

        public bool IsAtBaseline() => string.Equals(_ownerVillagerId,
            _initialOwnerVillagerId, StringComparison.Ordinal);
        public void OnRemovedFromWorld() => ClearOwner();
    }
}
