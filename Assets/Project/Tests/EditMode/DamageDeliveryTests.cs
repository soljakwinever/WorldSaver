#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class DamageDeliveryTests
    {
        private GameObject _attacker;
        private GameObject _target;
        private PersistentHealth _health;
        private EntityBus _entityBus;
        private AttackService _attackService;

        [SetUp]
        public void SetUp()
        {
            _attacker = new GameObject("Attacker");
            _target = new GameObject("Target");
            _health = _target.AddComponent<PersistentHealth>();
            _health.Initialize(10);
            _entityBus = new EntityBus();
            _attackService = new AttackService(_entityBus);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_attacker);
            UnityEngine.Object.DestroyImmediate(_target);
        }

        [Test]
        public void AttackContextRejectsInvalidInputs()
        {
            Assert.Throws<ArgumentNullException>(
                () => new AttackContext(null, null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AttackContext(_attacker, null, -1));
        }

        [Test]
        public void AttackCalculatesAndPublishesActualDeliveredAmount()
        {
            ToolData weapon = ScriptableObject.CreateInstance<ToolData>();
            SetWeaponPower(weapon, 4);
            IDamageable signalledTarget = null;
            AttackContext signalledContext = default;
            int signalledDamage = 0;
            int signalCount = 0;
            _entityBus.DamageDelivered += (target, context, damage) =>
            {
                signalledTarget = target;
                signalledContext = context;
                signalledDamage = damage;
                signalCount++;
            };

            AttackContext attack = new(_attacker, weapon, 11);
            int delivered = _attackService.Attack(_health, attack);

            Assert.That(_health.Health, Is.Zero);
            Assert.That(delivered, Is.EqualTo(10));
            Assert.That(signalCount, Is.EqualTo(1));
            Assert.That(signalledTarget, Is.SameAs(_health));
            Assert.That(signalledContext.Attacker, Is.SameAs(_attacker));
            Assert.That(signalledContext.Weapon, Is.SameAs(weapon));
            Assert.That(signalledContext.Force, Is.EqualTo(15));
            Assert.That(signalledDamage, Is.EqualTo(10));

            UnityEngine.Object.DestroyImmediate(weapon);
        }

        [Test]
        public void IneffectiveAttacksDoNotPublishDamage()
        {
            int signalCount = 0;
            _entityBus.DamageDelivered += (_, _, _) => signalCount++;

            _attackService.Attack(
                _health,
                new AttackContext(_attacker, null, 0));
            _attackService.Attack(
                _health,
                new AttackContext(_attacker, null, 10));
            _attackService.Attack(
                _health,
                new AttackContext(_attacker, null, 1));

            Assert.That(signalCount, Is.EqualTo(1));
        }

        [Test]
        public void LegacyDamageRemainsAvailableWithoutPublishingAttack()
        {
            int signalCount = 0;
            _entityBus.DamageDelivered += (_, _, _) => signalCount++;

            _health.TakeDamage(3);

            Assert.That(_health.Health, Is.EqualTo(7));
            Assert.That(signalCount, Is.Zero);
        }

        [Test]
        public void CalculateDamageAddsWeaponPowerToAttackForce()
        {
            ToolData weapon = ScriptableObject.CreateInstance<ToolData>();
            SetWeaponPower(weapon, 3);

            int damage = _attackService.CalculateDamage(
                new AttackContext(_attacker, weapon, 5));

            Assert.That(damage, Is.EqualTo(8));
            UnityEngine.Object.DestroyImmediate(weapon);
        }

        private static void SetWeaponPower(ToolData weapon, int power)
        {
            typeof(ToolData)
                .GetField("_power", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(weapon, power);
        }
    }
}
#endif
