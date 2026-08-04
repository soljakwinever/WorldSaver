using System;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.UI
{
    public sealed class DamagePopTextPresenter : IInitializable, IDisposable
    {
        private static readonly Color DamageColor =
            new(1f, 0.22f, 0.16f, 1f);
        private static readonly Color BlockedColor =
            new(0.7f, 0.7f, 0.7f, 1f);

        private readonly EntityBus _entityBus;
        private readonly IEffectSpawner _effects;

        public DamagePopTextPresenter(
            EntityBus entityBus,
            IEffectSpawner effects)
        {
            _entityBus = entityBus;
            _effects = effects;
        }

        public void Initialize()
        {
            _entityBus.AttackResolved += OnAttackResolved;
        }

        public void Dispose()
        {
            _entityBus.AttackResolved -= OnAttackResolved;
        }

        private void OnAttackResolved(
            IDamageable target,
            AttackContext context,
            int damageDelivered)
        {
            // Entity receivers use their damage rules to express immunity to
            // particular sources, tools, and source tags. Do not present an
            // immune attack as a blocked hit; zero damage from mitigation still
            // receives the normal blocked feedback below.
            if (damageDelivered == 0 &&
                target is EntityDamageReceiver receiver &&
                !receiver.CanReceiveDamage(context))
            {
                return;
            }

            Vector3 position = target is Component component
                ? component.transform.position
                : context.Attacker.transform.position;
            position.y += 0.5f;

            PopText popText = _effects.Spawn<PopText>(position);
            popText.Show(
                context.Force.ToString(),
                PopTextOptions.Damage(
                    damageDelivered > 0 ? DamageColor : BlockedColor));
        }
    }
}
