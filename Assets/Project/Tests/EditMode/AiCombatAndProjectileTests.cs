#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Project.Scripts.AI;
using Project.Scripts.AI.Leaves.Actions;
using Project.Scripts.AI.Leaves.Sensors;
using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Tests.EditMode
{
    [Category("AiCombatAndProjectile")]
    public sealed class AiCombatAndProjectileTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            _created.Clear();

            GameObject projectileRoot = GameObject.Find("Projectiles");
            if (projectileRoot != null)
                Object.DestroyImmediate(projectileRoot);
        }

        [Test]
        public void AttackTargetUsesAttackService()
        {
            GameObject attacker = CreateGameObject("Attacker");
            GameObject target = CreateGameObject("Target");
            PersistentHealth health = target.AddComponent<PersistentHealth>();
            health.Initialize(10);
            target.transform.position = Vector3.right;

            AttackTarget node = new();
            SetField(node, "force", 3);
            SetField(node, "maximumRange", 2f);
            Blackboard blackboard = CreateBlackboard(attacker, target.transform);
            blackboard.Set(
                AiKeys.AttackService,
                new AttackService(new EntityBus()));
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(health.Health, Is.EqualTo(7));
        }

        [Test]
        public void AttackTargetFailsOutsideConfiguredRange()
        {
            GameObject attacker = CreateGameObject("Attacker");
            GameObject target = CreateGameObject("Target");
            PersistentHealth health = target.AddComponent<PersistentHealth>();
            health.Initialize(10);
            target.transform.position = Vector3.right * 3f;

            AttackTarget node = new();
            SetField(node, "force", 3);
            SetField(node, "maximumRange", 2f);
            Blackboard blackboard = CreateBlackboard(attacker, target.transform);
            blackboard.Set(
                AiKeys.AttackService,
                new AttackService(new EntityBus()));
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Failure));
            Assert.That(health.Health, Is.EqualTo(10));
        }

        [Test]
        public void FireProjectileBuildsOwnerAgnosticLaunchContext()
        {
            GameObject attacker = CreateGameObject("Attacker");
            GameObject target = CreateGameObject("Target");
            target.transform.position = Vector3.right * 4f;
            ProjectileData data = CreateScriptableObject<ProjectileData>();
            CapturingProjectileService projectileService = new();

            FireProjectileAtTarget node = new();
            SetField(node, "projectile", data);
            SetField(node, "force", 5);
            SetField(node, "maximumRange", 6f);
            Blackboard blackboard = CreateBlackboard(attacker, target.transform);
            blackboard.Set(AiKeys.ProjectileService, projectileService);
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(projectileService.LaunchCount, Is.EqualTo(1));
            Assert.That(
                projectileService.LastContext.Attack.Attacker,
                Is.SameAs(attacker));
            Assert.That(
                projectileService.LastContext.Attack.Force,
                Is.EqualTo(5));
            Assert.That(
                projectileService.LastContext.Direction,
                Is.EqualTo(Vector2.right));
        }

        [Test]
        public void ProjectileImpactUsesAttackServiceAndReusesPoolEntry()
        {
            GameObject attacker = CreateGameObject("Attacker");
            GameObject target = CreateGameObject("Target");
            PersistentHealth health = target.AddComponent<PersistentHealth>();
            health.Initialize(10);
            Collider2D targetCollider =
                target.AddComponent<CircleCollider2D>();

            GameObject prefab = CreateGameObject("Projectile Prefab");
            prefab.AddComponent<Projectile>();
            prefab.AddComponent<CircleCollider2D>().isTrigger = true;
            prefab.SetActive(false);

            ProjectileData data = CreateScriptableObject<ProjectileData>();
            data.prefab = prefab;
            data.speed = 4f;
            data.lifetime = 2f;

            ProjectileService service = new(
                new AttackService(new EntityBus()));
            ProjectileLaunchContext context = new(
                data,
                new AttackContext(attacker, null, 3),
                Vector3.zero,
                Vector2.right);

            Assert.That(service.TryLaunch(context), Is.True);
            Projectile first = GameObject.Find("Projectiles")
                .GetComponentInChildren<Projectile>(true);
            Assert.That(first, Is.Not.Null);
            Assert.That(first.IsActive, Is.True);

            first.SendMessage(
                "OnTriggerEnter2D",
                targetCollider,
                SendMessageOptions.RequireReceiver);

            Assert.That(health.Health, Is.EqualTo(7));
            Assert.That(first.IsActive, Is.False);
            Assert.That(service.TryLaunch(context), Is.True);

            Projectile second = GameObject.Find("Projectiles")
                .GetComponentInChildren<Projectile>(true);
            Assert.That(second, Is.SameAs(first));
            service.Dispose();
        }

        [Test]
        public void FleeChoosesAReachableDestinationAwayFromThreat()
        {
            GameObject self = CreateGameObject("Self");
            GameObject threat = CreateGameObject("Threat");
            threat.transform.position = Vector3.left;
            CapturingPathFinder pathFinder = new();

            FleeFromTarget node = new();
            SetField(node, "safeDistance", 5f);
            SetField(node, "candidateAttempts", 1);
            Blackboard blackboard = CreateBlackboard(
                self,
                threat.transform);
            blackboard.Set(AiKeys.PathFindingMap, new WalkableMap());
            blackboard.Set(AiKeys.PathFindingService, pathFinder);
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Running));
            Assert.That(pathFinder.LastDestination.x, Is.GreaterThan(0));
        }

        [Test]
        public void WanderTowardTargetBiasesItsStepTowardTarget()
        {
            GameObject self = CreateGameObject("Self");
            GameObject target = CreateGameObject("Target");
            target.transform.position = Vector3.right * 10f;
            CapturingPathFinder pathFinder = new();

            WanderTowardTarget node = new();
            SetField(node, "stepRadius", 5f);
            SetField(node, "targetBias", 1f);
            SetField(node, "candidateAttempts", 1);
            Blackboard blackboard =
                CreateBlackboard(self, target.transform);
            blackboard.Set(AiKeys.PathFindingMap, new WalkableMap());
            blackboard.Set(AiKeys.PathFindingService, pathFinder);
            node.Bind(blackboard);

            Assert.That(
                node.Evaluate(),
                Is.EqualTo(AiNode.NodeState.Running));
            Assert.That(pathFinder.LastDestination.x, Is.GreaterThan(0));
        }

        [Test]
        public void DetectNearbySelectsNearestAndClearsMissingTarget()
        {
            GameObject self = CreateGameObject("Self");
            self.layer = 31;
            self.AddComponent<CircleCollider2D>();
            GameObject near = CreateGameObject("Near");
            near.layer = 31;
            near.transform.position = Vector3.right;
            near.AddComponent<CircleCollider2D>();
            GameObject far = CreateGameObject("Far");
            far.layer = 31;
            far.transform.position = Vector3.right * 2f;
            far.AddComponent<CircleCollider2D>();
            Physics2D.SyncTransforms();

            DetectNearby node = new();
            SetField(node, "distance", 3f);
            SetField(node, "layerMask", (LayerMask)(1 << 31));
            SetField(node, "tag", string.Empty);
            Blackboard blackboard = CreateBlackboard(self, null);
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(
                blackboard.GetOrDefault(AiKeys.Target),
                Is.SameAs(near.transform));

            near.transform.position = Vector3.right * 10f;
            far.transform.position = Vector3.right * 10f;
            Physics2D.SyncTransforms();

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Failure));
            Assert.That(blackboard.GetOrDefault(AiKeys.Target), Is.Null);
        }

        private Blackboard CreateBlackboard(
            GameObject self,
            Transform target)
        {
            Blackboard blackboard = new();
            blackboard.Set(AiKeys.Self, self);
            blackboard.Set(AiKeys.Target, target);
            return blackboard;
        }

        private GameObject CreateGameObject(string name)
        {
            GameObject gameObject = new(name);
            _created.Add(gameObject);
            return gameObject;
        }

        private T CreateScriptableObject<T>() where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            _created.Add(instance);
            return instance;
        }

        private static void SetField(
            object instance,
            string name,
            object value)
        {
            instance.GetType()
                .GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(instance, value);
        }

        private sealed class CapturingProjectileService :
            IProjectileService
        {
            public int LaunchCount { get; private set; }
            public ProjectileLaunchContext LastContext { get; private set; }

            public bool TryLaunch(ProjectileLaunchContext context)
            {
                LaunchCount++;
                LastContext = context;
                return true;
            }
        }

        private sealed class WalkableMap : IPathFindingMap
        {
            public bool IsWalkable(Vector2Int worldCell) => true;
            public float GetTraversalCost(Vector2Int worldCell) => 1f;
        }

        private sealed class CapturingPathFinder : IPathFindingService
        {
            public Vector2Int LastDestination { get; private set; }

            public bool TryFindPath(
                Vector2Int start,
                Vector2Int destination,
                List<Vector2Int> path,
                int maxVisitedTiles = 100000)
            {
                path.Clear();
                path.Add(start);
                path.Add(destination);
                LastDestination = destination;
                return true;
            }

            public Task<List<Vector2Int>> FindPathAsync(
                Vector2Int start,
                Vector2Int destination,
                int maxVisitedTiles = 100000,
                CancellationToken cancellationToken = default)
            {
                LastDestination = destination;
                return Task.FromResult(
                    new List<Vector2Int> { start, destination });
            }
        }
    }
}
#endif
