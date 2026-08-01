using System;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts.Actions;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
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

        private static void SetActions(SkillData skill, params SkillActionData[] actions)
        {
            typeof(SkillData).GetField("actionData",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(skill, actions);
        }
    }
}
