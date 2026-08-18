using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Actions
{
    [CreateAssetMenu(
        fileName = "New Projectile Skill Action",
        menuName = "Data/Skill Actions/Projectile")]
    public sealed class ProjectileSkillAction : SkillAction
    {
        public override Type DataType => typeof(ProjectileSkillActionData);

        public override bool CanPerform(
            SkillActionContext context,
            SkillActionData data)
        {
            if (data is not ProjectileSkillActionData projectile ||
                context.User == null || context.Projectile?.prefab == null)
                return false;

            WeaponSwingController swing =
                context.User.GetComponentInParent<WeaponSwingController>();
            WeaponSwingAnimation weaponSwing = context.User
                .GetComponentInParent<SkillRuntime>()?
                .WeaponSwingOverride ?? projectile.weaponSwing;
            if (weaponSwing != null && swing != null && swing.IsSwinging)
                return false;

            Vector3 origin = ResolveOrigin(context, projectile);
            return ResolveDirection(context, projectile, origin).sqrMagnitude >
                   Mathf.Epsilon;
        }

        public override void Perform(
            SkillActionContext context,
            SkillActionData data)
        {
            ProjectileSkillActionData projectile =
                (ProjectileSkillActionData)data;
            IProjectileService service =
                context.User.GetComponentInParent<SkillRuntimeProjectileBridge>()?
                    .ProjectileService;
            if (service == null)
            {
                Debug.LogError("The caster has no projectile-service bridge.");
                return;
            }

            if (projectile.consumeProjectile &&
                context.ConsumeProjectile != null &&
                !context.ConsumeProjectile())
                return;

            IEntityDamageSource source =
                context.User.GetComponentInParent<IEntityDamageSource>();
            List<EntityTag> tags = new();
            if (source?.DamageTags != null) tags.AddRange(source.DamageTags);
            if (context.Skill?.tags != null) tags.AddRange(context.Skill.tags);
            if (projectile.damageTags != null)
                tags.AddRange(projectile.damageTags);
            if (projectile.element != null) tags.Add(projectile.element);

            IProjectileAccuracy accuracy =
                context.User.GetComponentInParent<IProjectileAccuracy>();
            context.PlayAnimation?.Invoke(projectile.animationClip);
            void Launch()
            {
                // Re-resolve at release so a moving target is aimed at from the
                // weapon's current position after the telegraph.
                Vector3 releaseOrigin = ResolveOrigin(context, projectile);
                Vector2 releaseDirection = ResolveDirection(
                    context, projectile, releaseOrigin);
                if (source?.DamageSource == EntityDamageSource.Enemy)
                {
                    releaseDirection = ProjectileAim.ApplyAccuracy(
                        releaseDirection,
                        accuracy?.ProjectileAccuracy ?? 1f);
                }
                service.TryLaunch(new ProjectileLaunchContext(
                    context.Projectile,
                    new AttackContext(
                        context.User, null,
                        checked(projectile.baseDamage + context.AttackPotential),
                        source?.DamageSource ?? EntityDamageSource.Skill,
                        tags, context.Skill, projectile.attackType),
                    releaseOrigin,
                    releaseDirection));
            }

            SkillRuntime skillRuntime =
                context.User.GetComponentInParent<SkillRuntime>();
            WeaponSwingAnimation weaponSwing =
                skillRuntime?.WeaponSwingOverride ?? projectile.weaponSwing;
            if (weaponSwing == null)
            {
                Launch();
                return;
            }

            WeaponSwingController controller =
                context.User.GetComponentInParent<WeaponSwingController>() ??
                context.User.AddComponent<WeaponSwingController>();
            controller.TryPlay(
                weaponSwing,
                context.Target != null
                    ? context.Target.transform.position
                    : context.TargetPosition,
                onRelease: Launch,
                trackedTarget: context.Target != null
                    ? context.Target.transform
                    : null,
                spriteOverride: skillRuntime?.WeaponSpriteOverride);
        }

        private static Vector2 ResolveDirection(
            SkillActionContext context,
            ProjectileSkillActionData data,
            Vector3 origin)
        {
            if (!data.useLockedTargetPosition &&
                data.predictTargetMovement && context.Target != null)
            {
                return ProjectileAim.PredictDirection(
                    origin,
                    context.Target.transform,
                    context.Projectile.speed,
                    data.maximumPredictionTime);
            }

            Vector2 direction = data.useFacingDirection
                ? ResolveFacing(context)
                : context.TargetPosition - origin;
            if (direction.sqrMagnitude <= Mathf.Epsilon &&
                !data.useLockedTargetPosition && context.Target != null)
                direction = context.Target.transform.position - origin;
            return direction.normalized;
        }

        private static Vector3 ResolveOrigin(
            SkillActionContext context,
            ProjectileSkillActionData data)
        {
            if (!data.useLockedTargetPosition &&
                data.predictTargetMovement && context.Target != null)
            {
                Vector2 aim = ProjectileAim.PredictDirection(
                    context.User.transform.position,
                    context.Target.transform,
                    context.Projectile.speed,
                    data.maximumPredictionTime);
                return ProjectileAim.ResolveDirectionalOrigin(
                    context.User.transform.position,
                    aim,
                    data.launchOffset);
            }

            ISkillFacing skillFacing =
                context.User.GetComponentInParent<ISkillFacing>();
            return skillFacing?.ResolveLaunchOrigin(data.launchOffset) ??
                   context.User.transform.position +
                   (Vector3)data.launchOffset;
        }

        private static Vector2 ResolveFacing(SkillActionContext context) =>
            context.User.GetComponentInParent<ISkillFacing>()?
                .FacingDirection.normalized ?? Vector2.zero;

    }
}
