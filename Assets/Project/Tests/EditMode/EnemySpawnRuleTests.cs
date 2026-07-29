#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class EnemySpawnRuleTests
    {
        private EnemySpawnRule _rule;
        private EnemyData _defaultEnemy;
        private EnemyData _rareEnemy;
        private EnemyData _veryRareEnemy;
        private GameObject _defaultVisual;

        [SetUp]
        public void SetUp()
        {
            _rule = ScriptableObject.CreateInstance<EnemySpawnRule>();
            _defaultEnemy = ScriptableObject.CreateInstance<EnemyData>();
            _rareEnemy = ScriptableObject.CreateInstance<EnemyData>();
            _veryRareEnemy = ScriptableObject.CreateInstance<EnemyData>();
            _defaultVisual = new GameObject("Default Enemy Visual");
            _defaultEnemy.visual = _defaultVisual;
            _rule.enemyData = _defaultEnemy;
            _rule.variations.Add(new EnemySpawnVariation
            {
                enemyData = _rareEnemy,
                chance = 0.2f
            });
            _rule.variations.Add(new EnemySpawnVariation
            {
                enemyData = _veryRareEnemy,
                chance = 0.3f
            });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rule);
            Object.DestroyImmediate(_defaultEnemy);
            Object.DestroyImmediate(_rareEnemy);
            Object.DestroyImmediate(_veryRareEnemy);
            Object.DestroyImmediate(_defaultVisual);
        }

        [TestCase(0.1f, 1)]
        [TestCase(0.3f, 2)]
        [TestCase(0.8f, 0)]
        public void VariationsUseAbsoluteChanceAndDefaultFallback(
            float roll,
            int expected)
        {
            EnemyData selected = _rule.SelectEnemyData(roll);
            EnemyData expectedEnemy = expected switch
            {
                1 => _rareEnemy,
                2 => _veryRareEnemy,
                _ => _defaultEnemy
            };

            Assert.That(selected, Is.SameAs(expectedEnemy));
        }

        [TestCase(23, true)]
        [TestCase(2, true)]
        [TestCase(12, false)]
        public void OvernightWindowSpansMidnight(int hour, bool expected)
        {
            _rule.firstHour = 22;
            _rule.lastHour = 5;

            Assert.That(_rule.AllowsHour(hour), Is.EqualTo(expected));
        }

        [TestCase(0, 3, true)]
        [TestCase(2, 3, true)]
        [TestCase(3, 3, false)]
        [TestCase(0, 0, false)]
        public void PopulationCapMustHaveAvailableCapacity(
            int living,
            int maxAllowed,
            bool expected)
        {
            Assert.That(
                NPCSpawnController.IsUnderPopulationCap(
                    living,
                    maxAllowed),
                Is.EqualTo(expected));
        }

        [TestCase(12, true, true)]
        [TestCase(23, true, false)]
        [TestCase(12, false, false)]
        public void InvalidHourEnemiesArePrioritizedOnceOffscreen(
            int hour,
            bool isOffscreen,
            bool expected)
        {
            _rule.firstHour = 22;
            _rule.lastHour = 5;

            Assert.That(
                NPCSpawnController.ShouldPrioritizeTimeDespawn(
                    _rule,
                    hour,
                    isOffscreen),
                Is.EqualTo(expected));
        }

        [Test]
        public void TransientVisualComesFromSelectedEnemyData()
        {
            bool found = NPCSpawnController.TryResolveTransientVisual(
                _rule.SelectEnemyData(0.8f),
                out GameObject visual);

            Assert.That(found, Is.True);
            Assert.That(visual, Is.SameAs(_defaultVisual));
        }

        [Test]
        public void MissingEnemyDataVisualDoesNotUseAnotherPrefab()
        {
            _defaultEnemy.visual = null;

            bool found = NPCSpawnController.TryResolveTransientVisual(
                _defaultEnemy,
                out GameObject visual);

            Assert.That(found, Is.False);
            Assert.That(visual, Is.Null);
        }
    }
}
#endif
