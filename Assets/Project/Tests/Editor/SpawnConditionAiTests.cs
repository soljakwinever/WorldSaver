#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Project.Scripts.AI;
using Project.Scripts.AI.Leaves.Actions;
using Project.Scripts.AI.Leaves.Sensors;
using Project.Scripts.DataTypes;
using Project.Scripts.Enums;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class SpawnConditionAiTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        [Test]
        public void OutsideSpawnHoursReadsTheCreatingRule()
        {
            EnemySpawnRule rule = CreateRule();
            rule.firstHour = 22;
            rule.lastHour = 5;
            FakeTimeController time = new() { CurrentHour = 12 };
            Blackboard blackboard = CreateBlackboard(rule, time);
            IsOutsideSpawnHours sensor = new();
            sensor.Bind(blackboard);

            Assert.That(
                sensor.Evaluate(),
                Is.EqualTo(AiNode.NodeState.Success));

            time.CurrentHour = 23;
            Assert.That(
                sensor.Evaluate(),
                Is.EqualTo(AiNode.NodeState.Failure));
        }

        [Test]
        public void OutsideSpawnSeasonReadsTheCreatingRule()
        {
            EnemySpawnRule rule = CreateRule();
            rule.seasons = SeasonMask.Winter;
            FakeTimeController time = new()
            {
                CurrentSeason = Season.Summer
            };
            Blackboard blackboard = CreateBlackboard(rule, time);
            IsOutsideSpawnSeason sensor = new();
            sensor.Bind(blackboard);

            Assert.That(
                sensor.Evaluate(),
                Is.EqualTo(AiNode.NodeState.Success));

            time.CurrentSeason = Season.Winter;
            Assert.That(
                sensor.Evaluate(),
                Is.EqualTo(AiNode.NodeState.Failure));
        }

        [Test]
        public void WanderOffScreenRemainsRunningAfterLeavingView()
        {
            GameObject cameraObject = CreateGameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            GameObject enemy = CreateGameObject("Enemy");
            enemy.transform.position = new Vector3(100f, 0f, 0f);
            Blackboard blackboard = new();
            blackboard.Set(AiKeys.Self, enemy);
            blackboard.Set<IPathFindingMap>(
                AiKeys.PathFindingMap,
                new WalkableMap());
            blackboard.Set<IPathFindingService>(
                AiKeys.PathFindingService,
                new StraightPathFinder());
            WanderOffScreen action = new();
            action.Bind(blackboard);

            Assert.That(
                WanderOffScreen.IsOutsideViewport(
                    camera,
                    enemy.transform.position,
                    0.1f),
                Is.True);
            Assert.That(
                action.Evaluate(),
                Is.EqualTo(AiNode.NodeState.Running));
        }

        private EnemySpawnRule CreateRule()
        {
            EnemySpawnRule rule =
                ScriptableObject.CreateInstance<EnemySpawnRule>();
            _created.Add(rule);
            return rule;
        }

        private GameObject CreateGameObject(string name)
        {
            GameObject gameObject = new(name);
            _created.Add(gameObject);
            return gameObject;
        }

        private static Blackboard CreateBlackboard(
            EnemySpawnRule rule,
            ITimeController time)
        {
            Blackboard blackboard = new();
            blackboard.Set(AiKeys.SpawnRule, rule);
            blackboard.Set(AiKeys.TimeController, time);
            return blackboard;
        }

        private sealed class FakeTimeController : ITimeController
        {
            public int CurrentHour;
            public Season CurrentSeason;
            public int DayInMonth => 1;
            public Season Season => CurrentSeason;
            public int Year => 1;
            public int Hour => CurrentHour;
            public int Minute => 0;
            public float DayProgress => Hour / 24f;

            public void RestoreTime(
                int dayInMonth,
                Season season,
                int year,
                float dayProgress)
            {
                CurrentSeason = season;
                CurrentHour = Mathf.FloorToInt(dayProgress * 24f);
            }

            public void AdvanceDay()
            {
            }

            public void AdvanceMonth()
            {
            }

            public void AdvanceYear()
            {
            }
        }

        private sealed class WalkableMap : IPathFindingMap
        {
            public bool IsWalkable(Vector2Int worldCell) => true;
            public float GetTraversalCost(Vector2Int worldCell) => 1f;
        }

        private sealed class StraightPathFinder : IPathFindingService
        {
            public bool TryFindPath(
                Vector2Int start,
                Vector2Int destination,
                List<Vector2Int> path,
                int maxVisitedTiles = 100000)
            {
                path.Clear();
                path.Add(start);
                path.Add(destination);
                return true;
            }

            public Task<List<Vector2Int>> FindPathAsync(
                Vector2Int start,
                Vector2Int destination,
                int maxVisitedTiles = 100000,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(
                    new List<Vector2Int> { start, destination });
            }
        }
    }
}
#endif
