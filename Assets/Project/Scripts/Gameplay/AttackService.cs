using System;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    /// <summary>
    /// Resolves attack strength, applies it to a target, and publishes successful
    /// damage deliveries.
    /// </summary>
    public sealed class AttackService : IAttackService
    {
        private readonly EntityBus _entityBus;

        public AttackService(EntityBus entityBus)
        {
            _entityBus = entityBus ?? throw new ArgumentNullException(nameof(entityBus));
        }

        public int CalculateDamage(AttackContext context)
        {
            int baseDamage = checked(context.Force + (context.Weapon?.Power ?? 0));
            PlayerDataController player =
                context.Attacker.GetComponentInParent<PlayerDataController>();
            int statBonus =
                player?.GetAttackDamageBonus(context.AttackType) ?? 0;
            int attackDamage = checked(baseDamage + statBonus);
            if (context.Skill == null)
                return attackDamage;
            return context.SkillPowerMode == SkillPowerMode.Multiplier
                ? checked(attackDamage * Mathf.Max(0, context.Skill.power))
                : checked(attackDamage + context.Skill.power);
        }

        public int Attack(IDamageable target, AttackContext context)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            int calculatedDamage = CalculateDamage(context);
            AttackContext resolvedContext = new(
                context.Attacker,
                context.Weapon,
                calculatedDamage,
                context.Source,
                context.SourceTags);

            int damageDelivered = target.TakeDamage(resolvedContext);
            if (damageDelivered < 0 || damageDelivered > calculatedDamage)
                throw new InvalidOperationException(
                    $"{target.GetType().Name} reported invalid delivered damage " +
                    $"{damageDelivered} for a {calculatedDamage}-damage attack.");

            _entityBus.RaiseAttackResolved(
                target,
                resolvedContext,
                damageDelivered);

            if (damageDelivered > 0)
                _entityBus.RaiseDamageDelivered(
                    target,
                    resolvedContext,
                    damageDelivered);

            return damageDelivered;
        }
    }
}
