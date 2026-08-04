using System.Collections.Generic;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    /// <summary>Applies a circular skill hit to damageables, walls, and ground.</summary>
    public sealed class AreaAttackService : MonoBehaviour
    {
        private readonly List<TickDecal> _tickDecals = new();
        private IWorldClock _clock;

        private readonly struct TickDecal
        {
            public readonly GameObject Instance;
            public readonly long ExpiresAt;
            public TickDecal(GameObject instance, long expiresAt)
            {
                Instance = instance;
                ExpiresAt = expiresAt;
            }
        }

        [Inject]
        public void Construct(IWorldClock clock) => _clock = clock;

        public void Execute(
            SkillActionContext context,
            AreaAttackSkillActionData data)
        {
            Vector2 center = context.TargetPosition;
            DamageObjects(center, context, data);
            EditTiles(center, context, data);
            SpawnDecal(center, data);
        }

        private static void DamageObjects(
            Vector2 center,
            SkillActionContext context,
            AreaAttackSkillActionData data)
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                center, data.radius, data.targetLayers);
            HashSet<IDamageable> damaged = new();
            IEntityDamageSource source =
                context.User.GetComponentInParent<IEntityDamageSource>();
            List<EntityTag> tags = new();
            if (source?.DamageTags != null) tags.AddRange(source.DamageTags);
            if (context.Skill?.tags != null) tags.AddRange(context.Skill.tags);
            if (data.damageTags != null) tags.AddRange(data.damageTags);
            if (data.element != null) tags.Add(data.element);

            foreach (Collider2D hit in hits)
            {
                if (hit == null || hit.transform.IsChildOf(context.User.transform))
                    continue;
                IDamageable target = hit.GetComponentInParent<IDamageable>() ??
                                     hit.GetComponentInChildren<IDamageable>();
                if (target == null || !damaged.Add(target))
                    continue;
                GameObject targetObject = target is Component component
                    ? component.gameObject
                    : hit.gameObject;
                context.DealDamage?.Invoke(
                    targetObject,
                    new AttackContext(
                        context.User, null,
                        checked(Mathf.Max(0, data.baseDamage) +
                                context.AttackPotential),
                        source?.DamageSource ?? EntityDamageSource.Skill,
                        tags, context.Skill, data.attackType));
            }
        }

        private static void EditTiles(
            Vector2 center,
            SkillActionContext context,
            AreaAttackSkillActionData data)
        {
            if (!data.damageWalls && data.groundTile == null)
                return;
            Chunkloader loader = FindFirstObjectByType<Chunkloader>();
            if (loader == null)
                return;

            int minX = Mathf.FloorToInt(center.x - data.radius);
            int maxX = Mathf.FloorToInt(center.x + data.radius);
            int minY = Mathf.FloorToInt(center.y - data.radius);
            int maxY = Mathf.FloorToInt(center.y + data.radius);
            float radiusSquared = data.radius * data.radius;
            int damage = checked(Mathf.Max(0, data.baseDamage) +
                                 context.AttackPotential);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 cellCenter = new(x + 0.5f, y + 0.5f);
                if ((cellCenter - center).sqrMagnitude > radiusSquared)
                    continue;
                Vector3Int cell = new(x, y, 0);
                if (!loader.TryGetLoadedChunk(cell, out Chunk chunk))
                    continue;
                if (data.damageWalls && damage > 0)
                    chunk.TryDamageWall(
                        cell, damage, data.wallDestructionType, out _);
                if (data.groundTile != null)
                    chunk.TryPlaceTile(
                        cell,
                        Project.Scripts.DataTypes.SaveData.PersistentTileLayer.Ground,
                        data.groundTile);
            }
        }

        private void SpawnDecal(Vector2 center, AreaAttackSkillActionData data)
        {
            if (data.decalPrefab == null)
                return;
            GameObject instance = Instantiate(data.decalPrefab, center, Quaternion.identity);
            if (data.persistentDecal)
            {
                long now = _clock?.CurrentTick ?? 0;
                _tickDecals.Add(new TickDecal(
                    instance, checked(now + Mathf.Max(1, data.decalLifetimeTicks))));
            }
            else if (data.decalLifetimeSeconds > 0f)
            {
                Destroy(instance, data.decalLifetimeSeconds);
            }
        }

        private void Update()
        {
            if (_clock == null || _tickDecals.Count == 0)
                return;
            long now = _clock.CurrentTick;
            for (int i = _tickDecals.Count - 1; i >= 0; i--)
            {
                TickDecal decal = _tickDecals[i];
                if (decal.Instance != null && now < decal.ExpiresAt)
                    continue;
                if (decal.Instance != null) Destroy(decal.Instance);
                _tickDecals.RemoveAt(i);
            }
        }
    }
}
