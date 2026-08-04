using UnityEngine;
using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public interface ISkillStamina
    {
        float CurrentStamina { get; }
        float MaximumStamina { get; }
        bool TrySpendStamina(float amount);
    }

    public interface ISkillAnimationPlayer
    {
        void PlaySkillAnimation(AnimationClip clip);
    }

    public interface ISkillFacing
    {
        Vector2 FacingDirection { get; }
        Vector3 ResolveLaunchOrigin(Vector2 offset);
    }

    public interface IProjectileAccuracy
    {
        float ProjectileAccuracy { get; }
    }

    public interface ISkillRuntime
    {
        bool CanUse(
            SkillData skill,
            GameObject target = null,
            Vector3 targetPosition = default,
            int attackPotential = 0,
            ProjectileData projectile = null);
        bool TryUse(
            SkillData skill,
            GameObject target = null,
            Vector3 targetPosition = default,
            int attackPotential = 0,
            ProjectileData projectile = null);
        bool TryUseWithWeaponPresentation(
            SkillData skill,
            GameObject target,
            Vector3 targetPosition,
            WeaponSwingAnimation weaponSwingOverride,
            Sprite weaponSpriteOverride = null,
            int attackPotential = 0,
            ProjectileData projectile = null);
    }
}
