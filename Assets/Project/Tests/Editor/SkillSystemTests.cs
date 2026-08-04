using System;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.Actions;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Tests.Editor
{
    public sealed class SkillSystemTests
    {
        private GameObject _user;
        private GameObject _target;
        private SkillData _skill;
        private AttackSkillAction _attack;
        private StatModifierSkillAction _modifier;

        [SetUp]
        public void SetUp()
        {
            _user = new GameObject("skill-user");
            _target = new GameObject("skill-target");
            _target.AddComponent<TransientHealth>();
            _skill = ScriptableObject.CreateInstance<SkillData>();
            _skill.persistentId = "test-skill";
            _attack = ScriptableObject.CreateInstance<AttackSkillAction>();
            _modifier = ScriptableObject.CreateInstance<StatModifierSkillAction>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_user);
            UnityEngine.Object.DestroyImmediate(_target);
            UnityEngine.Object.DestroyImmediate(_skill);
            UnityEngine.Object.DestroyImmediate(_attack);
            UnityEngine.Object.DestroyImmediate(_modifier);
        }

        [Test]
        public void AttackAction_ComposesBaseAndSkillPower_AndPublishesDamage()
        {
            _skill.targetMode = SkillTargetMode.Entity;
            _skill.power = 5;
            SetActions(_skill, new AttackSkillActionData
            {
                action = _attack,
                baseDamage = 7,
                attackType = PlayerAttackType.Magic
            });

            var bus = new EntityBus();
            int published = 0;
            bus.DamageDelivered += (_, context, amount) =>
            {
                published = amount;
                Assert.AreEqual(EntityDamageSource.Skill, context.Source);
                Assert.AreSame(_skill, context.Skill);
            };
            SkillRuntime runtime = _user.AddComponent<SkillRuntime>();
            runtime.Initialize(new AttackService(bus));

            Assert.IsTrue(runtime.TryUse(_skill, _target));
            Assert.AreEqual(12, published);
            Assert.AreEqual(88, _target.GetComponent<TransientHealth>().Health);
        }

        [Test]
        public void Cooldown_BlocksSecondUse()
        {
            _skill.targetMode = SkillTargetMode.Entity;
            _skill.cooldown = 10f;
            SetActions(_skill, new AttackSkillActionData
            {
                action = _attack,
                baseDamage = 1
            });
            SkillRuntime runtime = _user.AddComponent<SkillRuntime>();
            runtime.Initialize(new AttackService(new EntityBus()));

            Assert.IsTrue(runtime.TryUse(_skill, _target));
            Assert.IsFalse(runtime.TryUse(_skill, _target));
            Assert.Greater(runtime.GetRemainingCooldown(_skill), 0f);
        }

        [Test]
        public void PassiveModifier_IsReferenceCountedAndReversible()
        {
            SetActions(_skill, new StatModifierSkillActionData
            {
                action = _modifier,
                mode = SkillActionMode.Passive,
                stat = EquipmentStat.Strength,
                amount = 3
            });
            SkillRuntime runtime = _user.AddComponent<SkillRuntime>();

            runtime.GrantPassive(_skill);
            runtime.GrantPassive(_skill);
            Assert.AreEqual(3, runtime.GetStatModifier(EquipmentStat.Strength));
            runtime.RevokePassive(_skill);
            Assert.AreEqual(3, runtime.GetStatModifier(EquipmentStat.Strength));
            runtime.RevokePassive(_skill);
            Assert.AreEqual(0, runtime.GetStatModifier(EquipmentStat.Strength));
        }

        [Test]
        public void Catalog_RejectsDuplicatePersistentIds()
        {
            SkillData duplicate = ScriptableObject.CreateInstance<SkillData>();
            duplicate.persistentId = _skill.persistentId;
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new SkillCatalog(new[] { _skill, duplicate }));
            }
            finally { UnityEngine.Object.DestroyImmediate(duplicate); }
        }

        [Test]
        public void SkillTree_EvaluatesRecursivePositionsAndParents()
        {
            SkillData childSkill = ScriptableObject.CreateInstance<SkillData>();
            SkillData grandchildSkill = ScriptableObject.CreateInstance<SkillData>();
            SkillTreeData tree = ScriptableObject.CreateInstance<SkillTreeData>();
            try
            {
                SkillTreeNode root = new() { skill = _skill };
                SkillTreeNode child = new()
                {
                    skill = childSkill,
                    position = new Vector2Int(1, 1)
                };
                SkillTreeNode grandchild = new()
                {
                    skill = grandchildSkill,
                    position = new Vector2Int(-1, 2)
                };
                root.connections.Add(child);
                child.connections.Add(grandchild);
                tree.root = root;

                var layout = tree.EvaluateLayout();

                Assert.AreEqual(Vector2Int.zero, layout[root]);
                Assert.AreEqual(new Vector2Int(1, 1), layout[child]);
                Assert.AreEqual(new Vector2Int(0, 3), layout[grandchild]);
                Assert.AreSame(root, child.Parent);
                Assert.AreSame(child, grandchild.Parent);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(childSkill);
                UnityEngine.Object.DestroyImmediate(grandchildSkill);
                UnityEngine.Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void SkillTree_RejectsOverlappingNodes()
        {
            SkillData leftSkill = ScriptableObject.CreateInstance<SkillData>();
            SkillData rightSkill = ScriptableObject.CreateInstance<SkillData>();
            SkillTreeData tree = ScriptableObject.CreateInstance<SkillTreeData>();
            try
            {
                tree.root = new SkillTreeNode { skill = _skill };
                tree.root.connections.Add(new SkillTreeNode
                {
                    skill = leftSkill,
                    position = Vector2Int.up
                });
                tree.root.connections.Add(new SkillTreeNode
                {
                    skill = rightSkill,
                    position = Vector2Int.up
                });

                Assert.Throws<InvalidOperationException>(() => tree.EvaluateLayout());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(leftSkill);
                UnityEngine.Object.DestroyImmediate(rightSkill);
                UnityEngine.Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void ChargeScaling_MultipliesCompleteAttackDamage()
        {
            _skill.power = 3;
            var service = new AttackService(new EntityBus());
            AttackContext context = new(
                _user, null, 4,
                EntityDamageSource.Skill, null,
                _skill, PlayerAttackType.Melee,
                SkillPowerMode.Multiplier);

            Assert.AreEqual(12, service.CalculateDamage(context));
        }

        [Test]
        public void ChargeDirection_CanAimFromPlayerToCursor()
        {
            Vector2 direction = SkillRuntime.ResolveChargeDirection(
                ChargeDirectionMode.TowardCursor,
                new Vector2(2f, 3f),
                new Vector2(5f, 7f),
                Vector2.left);

            Assert.That(direction.x, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(direction.y, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void ChargeDirection_UsesFacingWhenCursorOverlapsPlayer()
        {
            Vector2 direction = SkillRuntime.ResolveChargeDirection(
                ChargeDirectionMode.TowardCursor,
                Vector2.one,
                Vector2.one,
                Vector2.left);

            Assert.AreEqual(Vector2.left, direction);
        }

        [Test]
        public void InventoryMenuBlocksHotbarPointerOnlyInsideVisibleWindow()
        {
            GameObject host = new("Inventory Menu");
            try
            {
                InventoryDebugUI menu = host.AddComponent<InventoryDebugUI>();
                menu.SetVisible(true);
                Assert.That(menu.IsPointerOverBlockingUi(
                    new Vector2(20f, Screen.height - 100f)), Is.True);
                Assert.That(menu.IsPointerOverBlockingUi(
                    new Vector2(1000f, Screen.height - 100f)), Is.False);
                menu.SetVisible(false);
                Assert.That(menu.IsPointerOverBlockingUi(
                    new Vector2(20f, Screen.height - 100f)), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SenseSpendsManaAndRevealsConfiguredRadius()
        {
            _user.SetActive(false);
            PlayerDataController player = _user.AddComponent<PlayerDataController>();
            SkillRuntime runtime = _user.GetComponent<SkillRuntime>();
            var senseService = new RecordingSenseService();
            runtime.Initialize(new AttackService(new EntityBus()), senseService);
            SenseSkillAction action = ScriptableObject.CreateInstance<SenseSkillAction>();
            try
            {
                _skill.targetMode = SkillTargetMode.Self;
                _skill.manaCost = 20f;
                SetActions(_skill, new SenseSkillActionData
                {
                    action = action,
                    radius = 512f,
                    revealDuration = 8f
                });
                player.Mana = 1f;

                Assert.IsTrue(runtime.TryUse(_skill));
                Assert.AreEqual(30, player.CurrentMana);
                Assert.AreEqual(1, senseService.RevealCount);
                Assert.AreEqual(512f, senseService.Radius);
                Assert.AreEqual(8f, senseService.Duration);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(action);
            }
        }

        [Test]
        public void SenseRejectsUseWhenManaIsInsufficient()
        {
            _user.SetActive(false);
            PlayerDataController player = _user.AddComponent<PlayerDataController>();
            SkillRuntime runtime = _user.GetComponent<SkillRuntime>();
            var senseService = new RecordingSenseService();
            runtime.Initialize(new AttackService(new EntityBus()), senseService);
            SenseSkillAction action = ScriptableObject.CreateInstance<SenseSkillAction>();
            try
            {
                _skill.targetMode = SkillTargetMode.Self;
                _skill.manaCost = 20f;
                SetActions(_skill, new SenseSkillActionData
                {
                    action = action,
                    radius = 512f,
                    revealDuration = 8f
                });
                player.Mana = 0.2f;

                Assert.IsFalse(runtime.TryUse(_skill));
                Assert.AreEqual(10, player.CurrentMana);
                Assert.Zero(senseService.RevealCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(action);
            }
        }

        private sealed class RecordingSenseService : ISenseService
        {
            public int RevealCount { get; private set; }
            public float Radius { get; private set; }
            public float Duration { get; private set; }
            public bool CanSense(GameObject user) => user != null;
            public void Reveal(GameObject user, float radius, float duration)
            {
                RevealCount++;
                Radius = radius;
                Duration = duration;
            }
        }

        [Test]
        public void SenseIndicatorProjectsDirectionToScreenEdge()
        {
            Vector2 right = PlayerHUD.GetSenseEdgeScreenPosition(
                new Vector2(900f, 300f),
                new Vector2(800f, 600f),
                50f);
            Vector2 upperLeft = PlayerHUD.GetSenseEdgeScreenPosition(
                new Vector2(0f, 700f),
                new Vector2(800f, 600f),
                50f);

            Assert.That(right, Is.EqualTo(new Vector2(750f, 300f)));
            Assert.That(upperLeft.x, Is.EqualTo(150f).Within(0.0001f));
            Assert.That(upperLeft.y, Is.EqualTo(550f).Within(0.0001f));
        }

        private static void SetActions(SkillData skill, params SkillActionData[] actions)
        {
            typeof(SkillData).GetField("actionData",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(skill, actions);
        }
    }
}
