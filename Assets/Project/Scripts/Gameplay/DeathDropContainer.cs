using System;
using System.Collections.Generic;
using Project.Scripts.Core;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class DeathDropContainer : MonoBehaviour, IInteractable
    {
        private Inventory _inventory;
        private IWorldClock _clock;
        private IComponentWindowService _windowService;
        private long _createdTick;
        private long _lifetimeTicks;

        public IInventory Inventory => _inventory;
        public bool IsExpired { get; private set; }
        public long ExpiresAtTick => checked(_createdTick + _lifetimeTicks);

        public static DeathDropContainer Create(
            Vector3 position,
            IReadOnlyList<IItemStack> stacks,
            long lifetimeTicks,
            IWorldClock clock,
            IComponentWindowService windowService)
        {
            if (stacks == null)
                throw new ArgumentNullException(nameof(stacks));

            var host = new GameObject("Player Death Drop");
            host.transform.position = position;
            var collider = host.AddComponent<CircleCollider2D>();
            collider.radius = 0.45f;
            collider.isTrigger = true;

            var renderer = host.AddComponent<SpriteRenderer>();
            if (stacks.Count > 0)
                renderer.sprite = stacks[0].Item.sprite;

            DeathDropContainer container =
                host.AddComponent<DeathDropContainer>();
            container.Initialize(stacks, lifetimeTicks, clock, windowService);
            return container;
        }

        public void Initialize(
            IReadOnlyList<IItemStack> stacks,
            long lifetimeTicks,
            IWorldClock clock,
            IComponentWindowService windowService)
        {
            if (stacks == null)
                throw new ArgumentNullException(nameof(stacks));
            if (lifetimeTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(lifetimeTicks));

            _inventory = new Inventory(Math.Max(1, stacks.Count));
            foreach (IItemStack stack in stacks)
            {
                if (!_inventory.TryAdd(stack, out int remainder) ||
                    remainder != 0)
                {
                    throw new InvalidOperationException(
                        "The death drop container could not hold every dropped item.");
                }
            }

            _clock = clock;
            _windowService = windowService;
            _createdTick = clock?.CurrentTick ?? 0;
            _lifetimeTicks = lifetimeTicks;
        }

        private void Update()
        {
            EvaluateLifetime(_clock?.CurrentTick ?? _createdTick);
        }

        public bool EvaluateLifetime(long currentTick)
        {
            if (IsExpired)
                return true;

            if (_inventory == null ||
                _inventory.OccupiedSlots == 0 ||
                currentTick >= ExpiresAtTick)
            {
                Expire();
            }

            return IsExpired;
        }

        public bool CanInteract(InteractionContext context)
        {
            return !IsExpired &&
                   context.interactionType == InteractionType.Direct &&
                   context.user != null &&
                   _windowService != null &&
                   context.user.GetComponentInParent<PersistentInventory>() !=
                   null;
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context))
                return;

            IInventory player =
                context.user.GetComponentInParent<PersistentInventory>();
            _windowService.Open(new ComponentWindowRequest(
                "Dropped Items",
                new Vector2(480f, 360f),
                new DeathDropWindowSection(this, player)));
        }

        public string GetInteractionPrompt(InteractionContext context) =>
            context.interactionType == InteractionType.Direct && !IsExpired
                ? "Recover dropped items"
                : string.Empty;

        public Vector3 GetPosition() => transform.position;

        internal void ExpireIfEmpty()
        {
            if (_inventory == null || _inventory.OccupiedSlots == 0)
                Expire();
        }

        private void Expire()
        {
            if (IsExpired)
                return;

            IsExpired = true;
            _windowService?.Close();
            Destroy(gameObject);
        }
    }

    public sealed class DeathDropWindowSection : IComponentWindowSection
    {
        private readonly DeathDropContainer _container;
        private readonly IInventory _playerInventory;
        private Vector2 _scroll;

        public DeathDropWindowSection(
            DeathDropContainer container,
            IInventory playerInventory)
        {
            _container = container ??
                throw new ArgumentNullException(nameof(container));
            _playerInventory = playerInventory ??
                throw new ArgumentNullException(nameof(playerInventory));
        }

        public void Draw(ComponentWindowContext context)
        {
            if (_container.IsExpired)
            {
                context.Close();
                return;
            }

            var stacks =
                new List<IItemStack>(_container.Inventory.Stacks);
            if (stacks.Count == 0)
            {
                _container.ExpireIfEmpty();
                context.Close();
                return;
            }

            GUILayout.Label("Click a stack to recover it.");
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (IItemStack stack in stacks)
            {
                string label = $"{stack.Item.name} x{stack.Count}";
                if (GUILayout.Button(label, GUILayout.Height(42f)))
                {
                    if (TownCoreWindowSection.TryTransfer(
                            _container.Inventory,
                            _playerInventory,
                            stack))
                    {
                        context.StatusMessage = $"Recovered {label}.";
                        _container.ExpireIfEmpty();
                        if (_container.IsExpired)
                            context.Close();
                    }
                    else
                    {
                        context.StatusMessage =
                            "Your inventory does not have enough space.";
                    }
                    break;
                }
            }
            GUILayout.EndScrollView();
        }
    }
}
