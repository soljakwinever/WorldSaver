using System;
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
    public sealed class EnemyRuntime : MonoBehaviour
    {
        private const float GoldenAngle = 2.39996323f;

        private IItemStackPickupPool _pickupPool;
        private EntityBus _entityBus;
        private TransientHealth _health;

        public EnemyData Data { get; private set; }

        [Inject]
        public void Construct(
            IItemStackPickupPool pickupPool,
            EntityBus entityBus)
        {
            _pickupPool = pickupPool;
            _entityBus = entityBus;
        }

        public void Initialize(EnemyData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));

            if (_health != null)
                _health.Died -= OnDied;

            _health = GetComponent<TransientHealth>() ??
                gameObject.AddComponent<TransientHealth>();
            _health.Initialize(data.hp, data.defense);
            _health.Died += OnDied;

            AiNodeRunner runner = GetComponent<AiNodeRunner>() ??
                gameObject.AddComponent<AiNodeRunner>();
            runner.Configure(data.behaviourTree as AiNodeData);

            if (data.behaviourTree != null &&
                data.behaviourTree is not AiNodeData)
            {
                Debug.LogError(
                    $"{data.name} uses an unsupported behaviour tree type.",
                    data);
            }
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
            if (_pickupPool == null || Data?.drops == null)
                return;

            int luck = ResolveKillerLuck();
            float dropChanceMultiplier =
                1f + Mathf.Max(0, luck - 5) * 0.02f;
            int spawnedStackCount = 0;
            float startingAngle =
                UnityEngine.Random.value * Mathf.PI * 2f;
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
                        float angle =
                            startingAngle + spawnedStackCount * GoldenAngle;
                        float variation = UnityEngine.Random.Range(
                            1f - Data.dropExplosionVariation,
                            1f + Data.dropExplosionVariation);
                        Vector2 impulse = new Vector2(
                            Mathf.Cos(angle),
                            Mathf.Sin(angle)) *
                            (Data.dropExplosionImpulse * variation);
                        _pickupPool.Spawn(
                            drop.item,
                            count,
                            ItemRarityUtility.Generate(luck),
                            transform.position,
                            impulse);
                        spawnedStackCount++;
                        remaining -= count;
                    }
                }
            }
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
