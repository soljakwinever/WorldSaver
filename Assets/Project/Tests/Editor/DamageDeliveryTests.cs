#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.Bus;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
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
        public void EveryAttackPublishesAnAttackResolution()
        {
            int signalCount = 0;
            int lastDeliveredDamage = -1;
            _entityBus.AttackResolved += (_, _, damage) =>
            {
                signalCount++;
                lastDeliveredDamage = damage;
            };

            _attackService.Attack(
                _health,
                new AttackContext(_attacker, null, 0));
            _attackService.Attack(
                _health,
                new AttackContext(_attacker, null, 10));
            _attackService.Attack(
                _health,
                new AttackContext(_attacker, null, 1));

            Assert.That(signalCount, Is.EqualTo(3));
            Assert.That(lastDeliveredDamage, Is.Zero);
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

        [TestCase(99, 100, 5, 0)]
        [TestCase(80, 100, 5, 0)]
        [TestCase(79, 100, 5, 1)]
        [TestCase(60, 100, 5, 1)]
        [TestCase(59, 100, 5, 2)]
        [TestCase(40, 100, 5, 2)]
        [TestCase(39, 100, 5, 3)]
        [TestCase(20, 100, 5, 3)]
        [TestCase(19, 100, 5, 4)]
        [TestCase(100, 100, 5, -1)]
        [TestCase(100, 100, 0, -1)]
        [TestCase(100, 0, 5, -1)]
        public void WallDamageVisualSelectsSpriteFromDamagePercent(
            byte health,
            byte maximumHealth,
            int spriteCount,
            int expectedIndex)
        {
            Assert.That(
                WallDamageVisual.CalculateSpriteIndex(
                    health,
                    maximumHealth,
                    spriteCount),
                Is.EqualTo(expectedIndex));
        }

        [Test]
        public void DamageRuleRequiresMatchingSourceAndTag()
        {
            EntityTag pickaxe = ScriptableObject.CreateInstance<EntityTag>();
            EntityDamageRule rule = new()
            {
                sources = EntityDamageSource.Tool,
                sourceTags = new[] { pickaxe }
            };

            Assert.That(
                rule.Allows(new AttackContext(
                    _attacker,
                    null,
                    1,
                    EntityDamageSource.Tool,
                    new[] { pickaxe })),
                Is.True);
            Assert.That(
                rule.Allows(new AttackContext(
                    _attacker,
                    null,
                    1,
                    EntityDamageSource.Enemy)),
                Is.False);

            UnityEngine.Object.DestroyImmediate(pickaxe);
        }

        [Test]
        public void EntityDamageVisualSupportsHealthAboveByteRange()
        {
            Assert.That(
                WallDamageVisual.CalculateSpriteIndex(
                    tileHealth: 500,
                    maximumHealth: 1000,
                    spriteCount: 5),
                Is.EqualTo(2));
        }

        [Test]
        public void EntityDamageVisualMasksDamageToEntitySprite()
        {
            Texture2D texture = new(4, 4);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, 4, 4),
                new Vector2(0.5f, 0.5f));
            GameObject entity = new("Entity Sprite");
            GameObject damage = new("Damage Visual");
            try
            {
                SpriteRenderer entityRenderer =
                    entity.AddComponent<SpriteRenderer>();
                entityRenderer.sprite = sprite;
                damage.AddComponent<SpriteRenderer>();
                WallDamageVisual visual =
                    damage.AddComponent<WallDamageVisual>();

                visual.ConfigureEntityMask(entityRenderer);

                Assert.That(
                    damage.GetComponent<SpriteRenderer>().maskInteraction,
                    Is.EqualTo(SpriteMaskInteraction.VisibleInsideMask));
                Assert.That(
                    damage.GetComponent<SpriteMask>().sprite,
                    Is.SameAs(sprite));
                Assert.That(damage.transform.parent, Is.SameAs(entity.transform));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(damage);
                UnityEngine.Object.DestroyImmediate(entity);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void PersistentHealthRaisesVisualRefreshEvent()
        {
            int observedHealth = -1;
            int observedMaximum = -1;
            _health.HealthChanged += (health, maximum) =>
            {
                observedHealth = health;
                observedMaximum = maximum;
            };

            _health.TakeDamage(3);

            Assert.That(observedHealth, Is.EqualTo(7));
            Assert.That(observedMaximum, Is.EqualTo(10));
        }

        [Test]
        public void HealthlessEntityIsRemovedByCompatibleToolDamage()
        {
            GameObject rootObject = new("Chunk Root");
            GameObject entityObject = new("Healthless Entity");
            NodeData nodeData = ScriptableObject.CreateInstance<NodeData>();
            ToolData pickaxe = ScriptableObject.CreateInstance<ToolData>();
            try
            {
                ChunkPersistenceRoot root =
                    rootObject.AddComponent<ChunkPersistenceRoot>();
                root.BeginRestore(Vector2Int.zero);
                PersistentEntity entity =
                    entityObject.AddComponent<PersistentEntity>();
                entity.Initialize(
                    new NodeId(123),
                    EntityPersistenceKind.Procedural);
                root.RegisterGeneratedEntity(entity);
                root.CompleteRestore();

                nodeData.toolRequirement =
                    NodeData.ToolRequirement.Pickaxe;
                SetToolType(pickaxe, ToolType.Pickaxe);
                EntityDamageReceiver receiver =
                    entityObject.AddComponent<EntityDamageReceiver>();
                receiver.Initialize(nodeData, entity, health: null);

                int delivered = receiver.TakeDamage(new AttackContext(
                    _attacker,
                    pickaxe,
                    1,
                    EntityDamageSource.Tool,
                    pickaxe.DamageTags));

                Assert.That(delivered, Is.EqualTo(1));
                Assert.That(entityObject.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(entityObject);
                UnityEngine.Object.DestroyImmediate(rootObject);
                UnityEngine.Object.DestroyImmediate(nodeData);
                UnityEngine.Object.DestroyImmediate(pickaxe);
            }
        }

        [Test]
        public void EnemyDamageInstantlyRemovesHealthlessEntity()
        {
            GameObject rootObject = new("Chunk Root");
            GameObject entityObject = new("Healthless Entity");
            NodeData nodeData = ScriptableObject.CreateInstance<NodeData>();
            try
            {
                ChunkPersistenceRoot root =
                    rootObject.AddComponent<ChunkPersistenceRoot>();
                root.BeginRestore(Vector2Int.zero);
                PersistentEntity entity =
                    entityObject.AddComponent<PersistentEntity>();
                entity.Initialize(
                    new NodeId(456),
                    EntityPersistenceKind.Procedural);
                root.RegisterGeneratedEntity(entity);
                root.CompleteRestore();

                nodeData.toolRequirement =
                    NodeData.ToolRequirement.Pickaxe;
                EntityDamageReceiver receiver =
                    entityObject.AddComponent<EntityDamageReceiver>();
                receiver.Initialize(nodeData, entity, health: null);

                receiver.TakeDamage(new AttackContext(
                    _attacker,
                    null,
                    2,
                    EntityDamageSource.Enemy));

                Assert.That(entityObject.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(entityObject);
                UnityEngine.Object.DestroyImmediate(rootObject);
                UnityEngine.Object.DestroyImmediate(nodeData);
            }
        }

        [Test]
        public void DamageImmuneEntityRejectsDamage()
        {
            GameObject rootObject = new("Chunk Root");
            GameObject entityObject = new("Damage Immune Entity");
            NodeData nodeData = ScriptableObject.CreateInstance<NodeData>();
            try
            {
                ChunkPersistenceRoot root =
                    rootObject.AddComponent<ChunkPersistenceRoot>();
                root.BeginRestore(Vector2Int.zero);
                PersistentEntity entity =
                    entityObject.AddComponent<PersistentEntity>();
                entity.Initialize(
                    new NodeId(789),
                    EntityPersistenceKind.Procedural);
                root.RegisterGeneratedEntity(entity);
                root.CompleteRestore();

                EntityDamageReceiver receiver =
                    entityObject.AddComponent<EntityDamageReceiver>();
                receiver.Initialize(nodeData, entity, health: null);
                receiver.SetDamageImmune(true);

                int delivered = receiver.TakeDamage(new AttackContext(
                    _attacker,
                    null,
                    2,
                    EntityDamageSource.Enemy));

                Assert.That(delivered, Is.Zero);
                Assert.That(entityObject.activeSelf, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(entityObject);
                UnityEngine.Object.DestroyImmediate(rootObject);
                UnityEngine.Object.DestroyImmediate(nodeData);
            }
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
                Assert.That(player.MaxHunger, Is.EqualTo(104));
                Assert.That(player.MaxEnergy, Is.EqualTo(104));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void NeedCapacityCurveIsSlowAndIncreasesEveryLevel()
        {
            Assert.That(PlayerDataController.GetNeedCapacityLevelBonus(1), Is.Zero);
            Assert.That(PlayerDataController.GetNeedCapacityLevelBonus(10), Is.EqualTo(15));
            Assert.That(PlayerDataController.GetNeedCapacityLevelBonus(25), Is.EqualTo(33));
            Assert.That(PlayerDataController.GetNeedCapacityLevelBonus(50), Is.EqualTo(63));

            int previous = 0;
            for (int level = 2; level <= 100; level++)
            {
                int current = PlayerDataController.GetNeedCapacityLevelBonus(level);
                Assert.That(current, Is.GreaterThan(previous));
                previous = current;
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

        private static void SetToolType(ToolData tool, ToolType toolType)
        {
            typeof(ToolData)
                .GetField("_toolType", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(tool, toolType);
        }
    }
}
#endif
