using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(fileName = "New Explosive Projectile Skill Action", menuName = "Data/Skill Actions/Explosive Projectile")]
    public sealed class ExplosiveProjectileSkillAction : SkillAction
    {
        public override Type DataType => typeof(ExplosiveProjectileSkillActionData);

        public override bool CanPerform(SkillActionContext context, SkillActionData data)
        {
            if (data is not ExplosiveProjectileSkillActionData explosive ||
                context.User == null || explosive.projectile?.prefab == null ||
                explosive.explosionRadius <= 0f)
                return false;
            return (context.TargetPosition - context.User.transform.position)
                .sqrMagnitude > Mathf.Epsilon;
        }

        public override void Perform(SkillActionContext context, SkillActionData data)
        {
            var explosive = (ExplosiveProjectileSkillActionData)data;
            IProjectileService projectiles = context.User
                .GetComponentInParent<SkillRuntimeProjectileBridge>()?
                .ProjectileService;
            AreaAttackService area = UnityEngine.Object
                .FindFirstObjectByType<AreaAttackService>();
            if (projectiles == null || area == null)
            {
                Debug.LogError("Explosive projectiles require projectile and area-attack services.");
                return;
            }

            Vector3 origin = ResolveOrigin(context, explosive.launchOffset);
            Vector2 direction = (context.TargetPosition - origin).normalized;
            IEntityDamageSource source =
                context.User.GetComponentInParent<IEntityDamageSource>();
            List<EntityTag> tags = new();
            if (source?.DamageTags != null) tags.AddRange(source.DamageTags);
            if (context.Skill?.tags != null) tags.AddRange(context.Skill.tags);
            if (explosive.damageTags != null) tags.AddRange(explosive.damageTags);
            if (explosive.element != null) tags.Add(explosive.element);

            context.PlayAnimation?.Invoke(explosive.animationClip);
            projectiles.TryLaunch(new ProjectileLaunchContext(
                explosive.projectile,
                new AttackContext(
                    context.User, null, explosive.baseDamage,
                    source?.DamageSource ?? EntityDamageSource.Skill,
                    tags, context.Skill, explosive.attackType),
                origin,
                direction,
                context.TargetPosition,
                impact => Explode(context.AtPosition(impact), explosive, area),
                explosive.projectileParticlePrefab,
                dealDirectDamageOnImpact: false));
        }

        private static Vector3 ResolveOrigin(
            SkillActionContext context, Vector2 offset)
        {
            ISkillFacing facing =
                context.User.GetComponentInParent<ISkillFacing>();
            return facing?.ResolveLaunchOrigin(offset) ??
                   context.User.transform.position + (Vector3)offset;
        }

        private static void Explode(
            SkillActionContext context,
            ExplosiveProjectileSkillActionData explosive,
            AreaAttackService area)
        {
            if (explosive.explosionParticlePrefab != null)
            {
                GameObject effect = UnityEngine.Object.Instantiate(
                    explosive.explosionParticlePrefab,
                    context.TargetPosition,
                    Quaternion.identity);
                float lifetime = explosive.explosionParticleLifetime > 0f
                    ? explosive.explosionParticleLifetime
                    : ResolveParticleLifetime(effect);
                UnityEngine.Object.Destroy(effect, lifetime);
            }

            area.Execute(context, new AreaAttackSkillActionData
            {
                baseDamage = explosive.baseDamage,
                radius = explosive.explosionRadius,
                attackType = explosive.attackType,
                targetLayers = explosive.targetLayers,
                damageWalls = explosive.damageWalls,
                wallDestructionType = explosive.wallDestructionType,
                element = explosive.element,
                damageTags = explosive.damageTags,
                decalPrefab = explosive.decalPrefab,
                decalLifetimeSeconds = explosive.decalLifetimeSeconds,
                persistentDecal = explosive.persistentDecal,
                decalLifetimeTicks = explosive.decalLifetimeTicks
            });
        }

        private static float ResolveParticleLifetime(GameObject effect)
        {
            float lifetime = 2f;
            foreach (ParticleSystem particle in
                     effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                lifetime = Mathf.Max(
                    lifetime, main.duration + main.startLifetime.constantMax);
            }
            return lifetime;
        }
    }
}
