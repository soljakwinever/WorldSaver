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

        [Test]
        public void TransientTargetIsKnockedAwayFromAttacker()
        {
            _attacker.transform.position = Vector3.left;
            Rigidbody2D body = _target.AddComponent<Rigidbody2D>();
            TransientHealth transientHealth =
                _target.AddComponent<TransientHealth>();
            transientHealth.Initialize(10);

            _attackService.Attack(
                transientHealth,
                new AttackContext(_attacker, null, 1));

            Assert.That(body.linearVelocity.x, Is.GreaterThan(0f));
            Assert.That(transientHealth.Health, Is.EqualTo(9));
        }

        [Test]
        public void DamagedTransientTargetStunsItsAi()
        {
            TransientHealth transientHealth =
                _target.AddComponent<TransientHealth>();
            transientHealth.Initialize(10);
            StunnableSpy stunnable = _target.AddComponent<StunnableSpy>();

            _attackService.Attack(
                transientHealth,
                new AttackContext(_attacker, null, 1));

            Assert.That(stunnable.StunCount, Is.EqualTo(1));
            Assert.That(stunnable.LastDuration, Is.GreaterThan(0f));
        }

        [Test]
        public void DropChanceHonorsNeverPartialAndGuaranteedValues()
        {
            DropData drop = new();

            drop.dropChance = 0f;
            Assert.That(drop.PassesDropChance(0f), Is.False);

            drop.dropChance = 0.25f;
            Assert.That(drop.PassesDropChance(0.249f), Is.True);
            Assert.That(drop.PassesDropChance(0.25f), Is.False);

            drop.dropChance = 1f;
            Assert.That(drop.PassesDropChance(1f), Is.True);
        }

        [Test]
        public void ItemPickupLaunchAppliesOutwardImpulse()
        {
            GameObject pickupObject = new("Pickup");
            ItemStackPickup pickup =
                pickupObject.AddComponent<ItemStackPickup>();

            pickup.Launch(Vector2.right * 2f);

            Rigidbody2D body = pickupObject.GetComponent<Rigidbody2D>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.linearVelocity.x, Is.GreaterThan(0f));

            UnityEngine.Object.DestroyImmediate(pickupObject);
        }

        [Test]
        public void PlayerKillAwardsLevelAndFiveSpendableStatPoints()
        {
            GameObject playerObject = new("Player");
            try
            {
                PlayerDataController player =
                    playerObject.AddComponent<PlayerDataController>();
                PlayerBus playerBus = new();
                EntityBus entityBus = new();
                player.Construct(playerBus, entityBus);

                int raisedLevel = 0;
                int awardedPoints = 0;
                playerBus.OnLevelUp += (newLevel, points) =>
                {
                    raisedLevel = newLevel;
                    awardedPoints = points;
                };

                entityBus.RaiseEnemyDefeated(
                    null,
                    Vector3.zero,
                    PlayerDataController.GetExperienceRequired(1),
                    playerObject);

                Assert.That(player.Level, Is.EqualTo(2));
                Assert.That(player.Experience, Is.Zero);
                Assert.That(player.UnspentStatPoints, Is.EqualTo(5));
                Assert.That(raisedLevel, Is.EqualTo(2));
                Assert.That(awardedPoints, Is.EqualTo(5));

                for (int i = 0; i < 5; i++)
                    Assert.That(
                        player.TrySpendStatPoint(PlayerStat.Strength),
                        Is.True);

                Assert.That(player.Strength, Is.EqualTo(10));
                Assert.That(player.UnspentStatPoints, Is.Zero);
                Assert.That(
                    player.GetAttackDamageBonus(PlayerAttackType.Melee),
                    Is.EqualTo(5));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void ExperienceCurveGrowsAndCarriesExcessExperience()
        {
            Assert.That(
                PlayerDataController.GetExperienceRequired(1),
                Is.EqualTo(100));
            Assert.That(
                PlayerDataController.GetExperienceRequired(2),
                Is.EqualTo(175));
            Assert.That(
                PlayerDataController.GetExperienceRequired(3),
                Is.EqualTo(300));

            GameObject playerObject = new("Player");
            try
            {
                PlayerDataController player =
                    playerObject.AddComponent<PlayerDataController>();
                player.Construct(new PlayerBus(), new EntityBus());

                player.AddExperience(300);

                Assert.That(player.Level, Is.EqualTo(3));
                Assert.That(player.Experience, Is.EqualTo(25));
                Assert.That(player.UnspentStatPoints, Is.EqualTo(10));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        private sealed class StunnableSpy : MonoBehaviour, IStunnable
        {
            public int StunCount { get; private set; }
            public float LastDuration { get; private set; }

            public void Stun(float duration)
            {
                StunCount++;
                LastDuration = duration;
            }
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
