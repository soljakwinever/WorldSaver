using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    /// <summary>
    /// Splits item quantities into valid stacks and launches them outwards from
    /// one point. Shared defaults are configured once in GameDataInstaller.
    /// </summary>
    public sealed class ItemStackExplosionService : IItemStackExplosionService
    {
        private const float GoldenAngle = 2.39996323f;

        private readonly IItemStackPickupPool _pickupPool;
        private readonly ItemStackExplosionSettings _settings;

        public ItemStackExplosionService(
            IItemStackPickupPool pickupPool,
            ItemStackExplosionSettings settings)
        {
            _pickupPool = pickupPool ??
                          throw new ArgumentNullException(nameof(pickupPool));
            _settings = settings ?? new ItemStackExplosionSettings();
        }

        public void Explode(
            IReadOnlyList<ItemStackExplosionEntry> stacks,
            Vector3 position)
        {
            Explode(
                stacks,
                position,
                _settings.defaultImpulse,
                _settings.defaultImpulseVariation);
        }

        public void Explode(
            IReadOnlyList<ItemStackExplosionEntry> stacks,
            Vector3 position,
            float impulse,
            float impulseVariation)
        {
            if (stacks == null || stacks.Count == 0)
                return;

            impulse = Mathf.Max(0f, impulse);
            impulseVariation = Mathf.Clamp01(impulseVariation);
            float startingAngle = UnityEngine.Random.value * Mathf.PI * 2f;
            int spawnedStackCount = 0;

            foreach (ItemStackExplosionEntry stack in stacks)
            {
                if (stack.Item == null || stack.Count <= 0)
                    continue;

                int remaining = stack.Count;
                int stackLimit = Mathf.Max(1, stack.Item.maxStack);
                while (remaining > 0)
                {
                    int count = Mathf.Min(remaining, stackLimit);
                    float angle = startingAngle + spawnedStackCount * GoldenAngle;
                    float variation = UnityEngine.Random.Range(
                        1f - impulseVariation,
                        1f + impulseVariation);
                    Vector2 launchImpulse = new(
                        Mathf.Cos(angle) * impulse * variation,
                        Mathf.Sin(angle) * impulse * variation);
                    _pickupPool.Spawn(
                        stack.Item,
                        count,
                        stack.Rarity,
                        position,
                        launchImpulse,
                        stack.Durability);
                    remaining -= count;
                    spawnedStackCount++;
                }
            }
        }
    }
}
