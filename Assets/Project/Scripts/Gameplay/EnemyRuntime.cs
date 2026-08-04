using System;
using System.Collections.Generic;
using Project.Scripts.AI;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Utility;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    /// <summary>
    /// Applies an EnemyData definition to a spawned enemy and resolves its death.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyRuntime : MonoBehaviour, IEntityDamageSource,
        ISkillFacing, IProjectileAccuracy
    {
        private IItemStackExplosionService _itemStackExplosion;
        private EntityBus _entityBus;
        private IAttackService _attackService;
        private IProjectileService _projectileService;
        private TransientHealth _health;
        private Vector2 _lastPosition;
        private Vector2 _facingDirection;

        public EnemyData Data { get; private set; }
        public EntityDamageSource DamageSource => EntityDamageSource.Enemy;
        public float ProjectileAccuracy => Data != null
            ? Mathf.Clamp01(Data.accuracy)
            : 1f;
        public Vector2 FacingDirection => _facingDirection;
        public Vector3 ResolveLaunchOrigin(Vector2 offset)
        {
            Vector2 facing = _facingDirection.normalized;
            if (facing.sqrMagnitude <= Mathf.Epsilon)
                return transform.position + (Vector3)offset;

            Vector2 perpendicular = new(-facing.y, facing.x);
            return transform.position +
                   (Vector3)(facing * offset.x + perpendicular * offset.y);
        }
        public System.Collections.Generic.IReadOnlyList<EntityTag> DamageTags =>
            Data?.damageTags ?? Array.Empty<EntityTag>();

        [Inject]
        public void Construct(
            IItemStackExplosionService itemStackExplosion,
            EntityBus entityBus,
            IAttackService attackService,
            IProjectileService projectileService)
        {
            _itemStackExplosion = itemStackExplosion;
            _entityBus = entityBus;
            _attackService = attackService;
            _projectileService = projectileService;
        }

        public void Initialize(EnemyData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            _lastPosition = transform.position;
            _facingDirection = Vector2.zero;

            if (_health != null)
                _health.Died -= OnDied;

            _health = GetComponent<TransientHealth>() ??
                gameObject.AddComponent<TransientHealth>();
            _health.Initialize(data.hp, data.defense);
            _health.Died += OnDied;

            AiNodeRunner runner = GetComponent<AiNodeRunner>() ??
                gameObject.AddComponent<AiNodeRunner>();
            runner.Blackboard.Set(AiKeys.EnemyData, data);
            runner.Blackboard.Set(AiKeys.Attack, Mathf.Max(0, data.attack));
            runner.Blackboard.Set(
                AiKeys.MovementSpeed,
                Mathf.Max(0f, data.movementSpeed));
            runner.Blackboard.Set(
                AiKeys.SprintMultiplier,
                Mathf.Max(1f, data.sprintMultiplier));
            runner.Blackboard.Set(
                AiKeys.Accuracy,
                Mathf.Clamp01(data.accuracy));
            runner.Configure(data.behaviourTree as AiNodeData);

            SkillRuntime skillRuntime = GetComponent<SkillRuntime>() ??
                gameObject.AddComponent<SkillRuntime>();
            skillRuntime.Initialize(
                _attackService,
                projectileService: _projectileService);

            if (data.behaviourTree != null &&
                data.behaviourTree is not AiNodeData)
            {
                Debug.LogError(
                    $"{data.name} uses an unsupported behaviour tree type.",
                    data);
            }
        }

        private void LateUpdate()
        {
            Vector2 position = transform.position;
            Vector2 movement = position - _lastPosition;
            if (movement.sqrMagnitude > 0.000001f)
                _facingDirection = movement.normalized;
            _lastPosition = position;
        }

        private void OnDied()
        {
            _health.Died -= OnDied;
            SpawnDrops();
            _entityBus?.RaiseEnemyDefeated(
                Data,
                transform.position,
                Data.experienceValue,
                _health.LastDamageContext?.Attacker);
        }

        private void SpawnDrops()
        {
            if (_itemStackExplosion == null || Data?.drops == null)
                return;

            int luck = ResolveKillerLuck();
            float dropChanceMultiplier =
                1f + Mathf.Max(0, luck - 5) * 0.02f;
            List<ItemStackExplosionEntry> stacks = new();
            foreach (DropData drop in Data.drops)
            {
                if (drop?.item == null)
                    continue;

                for (int roll = 0; roll < drop.rolls; roll++)
                {
                    float effectiveDropChance =
                        Mathf.Clamp01(drop.dropChance * dropChanceMultiplier);
                    if (UnityEngine.Random.value >= effectiveDropChance)
                        continue;

                    int remaining = drop.RollAmount();
                    int stackLimit = Mathf.Max(1, drop.item.maxStack);
                    while (remaining > 0)
                    {
                        int count = Mathf.Min(remaining, stackLimit);
                        stacks.Add(new ItemStackExplosionEntry(
                            drop.item,
                            count,
                            ItemRarityUtility.Generate(luck)));
                        remaining -= count;
                    }
                }
            }

            _itemStackExplosion.Explode(
                stacks,
                transform.position,
                Data.dropExplosionImpulse,
                Data.dropExplosionVariation);
        }

        private int ResolveKillerLuck()
        {
            if (_health?.LastDamageContext is not AttackContext context)
                return 5;

            PlayerDataController player =
                context.Attacker.GetComponentInParent<PlayerDataController>();
            return player?.Luck ?? 5;
        }

        private void OnDestroy()
        {
            if (_health != null)
                _health.Died -= OnDied;
        }
    }
}
