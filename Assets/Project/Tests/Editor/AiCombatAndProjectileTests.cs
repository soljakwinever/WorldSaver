#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.Actions;
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
        public void AttackTargetUsesEnemyNormalAttackSkill()
        {
            GameObject attacker = CreateGameObject("Attacker");
            GameObject target = CreateGameObject("Target");
            PersistentHealth health = target.AddComponent<PersistentHealth>();
            health.Initialize(10);
            target.transform.position = Vector3.right;

            AttackSkillAction attackAction =
                CreateScriptableObject<AttackSkillAction>();
            SkillData normalAttack = CreateScriptableObject<SkillData>();
            SetField(
                normalAttack,
                "actionData",
                new SkillActionData[]
                {
                    new AttackSkillActionData
                    {
                        action = attackAction,
                        mode = SkillActionMode.Active,
                        baseDamage = 3
                    }
                });
            EnemyData enemyData = CreateScriptableObject<EnemyData>();
            enemyData.attack = 2;
            enemyData.NormalAttack = normalAttack;
            attacker.AddComponent<EnemyRuntime>().Initialize(enemyData);
            Assert.That(
                attacker.GetComponent<AiNodeRunner>().Blackboard.GetOrDefault(
                    AiKeys.Attack),
                Is.EqualTo(2));
            Assert.That(
                attacker.GetComponent<AiNodeRunner>().Blackboard.GetOrDefault(
                    AiKeys.MovementSpeed),
                Is.EqualTo(enemyData.movementSpeed));
            Assert.That(
                attacker.GetComponent<AiNodeRunner>().Blackboard.GetOrDefault(
                    AiKeys.SprintMultiplier),
                Is.EqualTo(enemyData.sprintMultiplier));
            Assert.That(
                attacker.GetComponent<AiNodeRunner>().Blackboard.GetOrDefault(
                    AiKeys.Accuracy),
                Is.EqualTo(enemyData.accuracy));
            AttackService attackService = new(new EntityBus());
            attacker.GetComponent<SkillRuntime>().Initialize(attackService);

            AttackTarget node = new();
            SetField(node, "maximumRange", 2f);
            Blackboard blackboard = CreateBlackboard(attacker, target.transform);
            blackboard.Set(AiKeys.EnemyData, enemyData);
            blackboard.Set(AiKeys.Attack, enemyData.attack);
            blackboard.Set(AiKeys.AttackService, attackService);
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(health.Health, Is.EqualTo(5));
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
        public void AttackTargetStartsCommittedPatternAndLocksMovement()
        {
            GameObject attacker = CreateGameObject("Attacker");
            GameObject target = CreateGameObject("Target");
            target.AddComponent<PersistentHealth>().Initialize(10);
            target.transform.position = Vector3.right;

            ForwardBoxAttackSkillAction action =
                CreateScriptableObject<ForwardBoxAttackSkillAction>();
            SkillData swipe = CreateScriptableObject<SkillData>();
            SetField(swipe, "actionData", new SkillActionData[]
            {
                new ForwardBoxAttackSkillActionData
                {
                    action = action,
                    mode = SkillActionMode.Active,
                    boxSize = Vector2.one
                }
            });
            EnemyData enemy = CreateScriptableObject<EnemyData>();
            enemy.NormalAttack = swipe;
            enemy.conditionalSkills = new[]
            {
                new ConditionalEnemySkill
                {
                    skill = swipe,
                    maximumRange = 1.5f,
                    windUpDuration = 10f,
                    recoveryDuration = 1f,
                    condition = new EnemySkillCondition
                    {
                        type = EnemySkillConditionType.Always
                    }
                }
            };

            SkillRuntime runtime = attacker.AddComponent<SkillRuntime>();
            runtime.Initialize(new AttackService(new EntityBus()));
            EnemyAttackController controller =
                attacker.AddComponent<EnemyAttackController>();
            AttackTarget node = new();
            Blackboard blackboard = CreateBlackboard(attacker, target.transform);
            blackboard.Set(AiKeys.EnemyData, enemy);
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Running));
            Assert.That(controller.Phase,
                Is.EqualTo(EnemyAttackController.AttackPhase.WindUp));
            Assert.That(controller.IsMovementLocked, Is.True);

            controller.Stun(0.25f);
            Assert.That(controller.Phase,
                Is.EqualTo(EnemyAttackController.AttackPhase.Ready));
            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Failure));
        }

        [Test]
        public void ArrowTelegraphUsesSeparateRendererObjects()
        {
            GameObject attacker = CreateGameObject("Attacker");
            attacker.AddComponent<SpriteRenderer>();
            EnemyAttackController controller =
                attacker.AddComponent<EnemyAttackController>();
            ConditionalEnemySkill attack = new()
            {
                maximumRange = 8f,
                telegraphWidth = 0.08f,
                telegraphShape = EnemyAttackTelegraphShape.Arrow
            };

            typeof(EnemyAttackController)
                .GetMethod(
                    "ShowTelegraph",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(controller, new object[] { attack, Vector3.right });

            LineRenderer[] renderers =
                attacker.GetComponentsInChildren<LineRenderer>();
            Assert.That(renderers, Has.Length.EqualTo(2));
            Assert.That(renderers[0].gameObject,
                Is.Not.SameAs(renderers[1].gameObject));

            typeof(EnemyAttackController)
                .GetMethod(
                    "UpdateTelegraph",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(controller, new object[] { attack, Vector3.up });
            LineRenderer shaft = attacker.transform
                .Find("Attack Telegraph")
                ?.GetComponent<LineRenderer>();
            Assert.That(shaft, Is.Not.Null);
            Assert.That(shaft.GetPosition(1).x,
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(shaft.GetPosition(1).y,
                Is.EqualTo(8f).Within(0.0001f));
        }

        [Test]
        public void ConditionalSkillEvaluatorSelectsFirstValidSkill()
        {
            GameObject enemyObject = CreateGameObject("Enemy");
            GameObject target = CreateGameObject("Target");
            target.transform.position = Vector3.right * 1.5f;
            SkillData charge = CreateScriptableObject<SkillData>();
            SkillData fallback = CreateScriptableObject<SkillData>();
            WeaponSwingAnimation basicSwing =
                CreateScriptableObject<WeaponSwingAnimation>();
            WeaponSwingAnimation chargeSwing =
                CreateScriptableObject<WeaponSwingAnimation>();
            EnemyData enemy = CreateScriptableObject<EnemyData>();
            enemy.BasicAttackWeaponSwing = basicSwing;
            enemy.conditionalSkills = new[]
            {
                new ConditionalEnemySkill
                {
                    skill = charge,
                    weaponSwing = chargeSwing,
                    condition = new EnemySkillCondition
                    {
                        type = EnemySkillConditionType.DistanceToTarget,
                        distance = 2f
                    }
                },
                new ConditionalEnemySkill
                {
                    skill = fallback,
                    condition = new EnemySkillCondition
                    {
                        type = EnemySkillConditionType.Always
                    }
                }
            };

            Blackboard blackboard = CreateBlackboard(
                enemyObject, target.transform);
            blackboard.Set(AiKeys.EnemyData, enemy);
            EvaluateConditionalSkills node = new();
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(
                blackboard.GetOrDefault(AiKeys.ConditionalSkill),
                Is.SameAs(charge));
            Assert.That(
                blackboard.GetOrDefault(AiKeys.ConditionalWeaponSwing),
                Is.SameAs(chargeSwing));

            target.transform.position = Vector3.right * 3f;
            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(
                blackboard.GetOrDefault(AiKeys.ConditionalSkill),
                Is.SameAs(fallback));
            Assert.That(
                blackboard.GetOrDefault(AiKeys.ConditionalWeaponSwing),
                Is.SameAs(basicSwing));
        }

        [Test]
        public void SkeletonMeleeWinsAtRangedBoundary()
        {
            GameObject skeleton = CreateGameObject("Skeleton");
            GameObject target = CreateGameObject("Target");
            target.transform.position = Vector3.right * 1.4f;
            SkillData melee = CreateScriptableObject<SkillData>();
            SkillData ranged = CreateScriptableObject<SkillData>();
            EnemyData enemy = CreateScriptableObject<EnemyData>();
            enemy.conditionalSkills = new[]
            {
                new ConditionalEnemySkill
                {
                    skill = melee,
                    maximumRange = 1.4f,
                    priority = 2,
                    condition = new EnemySkillCondition
                    {
                        type = EnemySkillConditionType.Always
                    }
                },
                new ConditionalEnemySkill
                {
                    skill = ranged,
                    minimumRange = 1.4f,
                    maximumRange = 8f,
                    priority = 1,
                    condition = new EnemySkillCondition
                    {
                        type = EnemySkillConditionType.Always
                    }
                }
            };
            Blackboard blackboard = CreateBlackboard(
                skeleton, target.transform);
            blackboard.Set(AiKeys.EnemyData, enemy);
            EvaluateConditionalSkills node = new();
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(blackboard.GetOrDefault(AiKeys.ConditionalSkill),
                Is.SameAs(melee));
        }

        [Test]
        public void LockedProjectileUsesCommittedPosition()
        {
            GameObject skeleton = CreateGameObject("Skeleton");
            GameObject target = CreateGameObject("Target");
            target.transform.position = Vector3.up * 4f;
            GameObject projectilePrefab = CreateGameObject("Arrow Prefab");
            ProjectileData projectile = CreateScriptableObject<ProjectileData>();
            projectile.prefab = projectilePrefab;
            ProjectileSkillAction action =
                CreateScriptableObject<ProjectileSkillAction>();
            SkillData shot = CreateScriptableObject<SkillData>();
            SetField(shot, "actionData", new SkillActionData[]
            {
                new ProjectileSkillActionData
                {
                    action = action,
                    mode = SkillActionMode.Active,
                    useLockedTargetPosition = true,
                    predictTargetMovement = true,
                    consumeProjectile = false
                }
            });
            CapturingProjectileService projectiles = new();
            SkillRuntime runtime = skeleton.AddComponent<SkillRuntime>();
            runtime.Initialize(
                new AttackService(new EntityBus()),
                projectileService: projectiles);

            Assert.That(runtime.TryUse(
                shot,
                target,
                Vector3.right * 4f,
                projectile: projectile), Is.True);
            Assert.That(projectiles.LastContext.Direction.x,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(projectiles.LastContext.Direction.y,
                Is.EqualTo(0f).Within(0.0001f));
        }

        [TestCase(1.3f, 0f)]
        [TestCase(0f, 1.3f)]
        [TestCase(0.9192388f, 0.9192388f)]
        public void EnemyAttackEngagementRangeIsAngleIndependent(
            float targetX,
            float targetY)
        {
            GameObject enemyObject = CreateGameObject("Enemy");
            GameObject target = CreateGameObject("Target");
            target.transform.position = new Vector3(targetX, targetY);
            SkillData swipe = CreateScriptableObject<SkillData>();
            EnemyData enemy = CreateScriptableObject<EnemyData>();
            enemy.conditionalSkills = new[]
            {
                new ConditionalEnemySkill
                {
                    skill = swipe,
                    maximumRange = 1.4f
                }
            };

            IsWithinDistance node = new();
            SetField(node, "distance", 0f);
            Blackboard blackboard = CreateBlackboard(
                enemyObject, target.transform);
            blackboard.Set(AiKeys.EnemyData, enemy);
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
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
        public void FloatMathBindingUsesLiveBlackboardValues()
        {
            Blackboard blackboard = new();
            blackboard.Set(AiKeys.MovementSpeed, 3f);
            blackboard.Set(AiKeys.SprintMultiplier, 2f);
            FloatBindingNode node = new();
            node.SetFloatBinding(
                "value",
                new AiFloatMathExpression(
                    new AiFloatVariableExpression(
                        AiFloatVariable.MovementSpeed),
                    new AiFloatVariableExpression(
                        AiFloatVariable.SprintMultiplier),
                    AiFloatOperation.Multiply));
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(node.Value, Is.EqualTo(6f));

            blackboard.Set(AiKeys.MovementSpeed, 4f);
            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(node.Value, Is.EqualTo(8f));
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
        public void ProjectileCollidesWithStaticEnvironmentAndReturnsToPool()
        {
            GameObject attacker = CreateGameObject("Attacker");
            GameObject wall = CreateGameObject("Wall");
            wall.transform.position = Vector3.right;
            wall.AddComponent<BoxCollider2D>().size =
                new Vector2(0.1f, 2f);

            GameObject prefab = CreateGameObject("Projectile Prefab");
            prefab.AddComponent<Projectile>();
            prefab.AddComponent<CircleCollider2D>().radius = 0.1f;
            prefab.SetActive(false);

            ProjectileData data = CreateScriptableObject<ProjectileData>();
            data.prefab = prefab;
            data.speed = 32f;
            data.lifetime = 2f;
            data.collisionMask = ~0;
            data.despawnOnEnvironmentHit = true;

            int impacts = 0;
            ProjectileService service = new(
                new AttackService(new EntityBus()));
            ProjectileLaunchContext context = new(
                data,
                new AttackContext(attacker, null, 3),
                Vector3.zero,
                Vector2.right,
                onImpact: _ => impacts++);

            Physics2D.SyncTransforms();
            Assert.That(service.TryLaunch(context), Is.True);
            Projectile projectile = GameObject.Find("Projectiles")
                .GetComponentInChildren<Projectile>(true);
            Rigidbody2D body = projectile.GetComponent<Rigidbody2D>();
            Assert.That(body.useFullKinematicContacts, Is.True);
            Assert.That(body.collisionDetectionMode,
                Is.EqualTo(CollisionDetectionMode2D.Continuous));

            Physics2D.SyncTransforms();
            for (int i = 0; i < 4 && projectile.IsActive; i++)
                Physics2D.Simulate(0.02f);

            Assert.That(projectile.IsActive, Is.False);
            Assert.That(impacts, Is.EqualTo(1));
            Assert.That(projectile.gameObject.activeSelf, Is.False);
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

        [Test]
        public void DetectNearbyRetainsFleeTargetBeyondAcquisitionRange()
        {
            GameObject self = CreateGameObject("Self");
            GameObject target = CreateGameObject("Target");
            target.transform.position = Vector3.right * 10f;

            DetectNearby node = new();
            SetField(node, "distance", 3f);
            SetField(node, "tag", string.Empty);
            Blackboard blackboard = CreateBlackboard(self, target.transform);
            blackboard.Set(AiKeys.TargetRetentionDistance, 12f);
            node.Bind(blackboard);

            Assert.That(node.Evaluate(), Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(
                blackboard.GetOrDefault(AiKeys.Target),
                Is.SameAs(target.transform));
        }

        [Test]
        public void TargetMemoryFollowsOnlyRecordedVisibleBreadcrumbs()
        {
            TargetTrackingMemory memory = new();
            memory.Record(Vector3.right, Vector3.zero, 1f, 0.25f, 12);
            memory.Record(Vector3.right * 2f, Vector3.zero, 2f, 0.25f, 12);

            Assert.That(memory.TryGetNext(
                Vector3.zero, 3f, 5f, 0.2f, out Vector3 first), Is.True);
            Assert.That(first, Is.EqualTo(Vector3.right));

            // Moving the real target while hidden cannot affect memory because
            // only RecordVisibleTargetPath writes observed positions.
            Assert.That(memory.TryGetNext(
                Vector3.right, 3f, 5f, 0.2f, out Vector3 second), Is.True);
            Assert.That(second, Is.EqualTo(Vector3.right * 2f));
        }

        [Test]
        public void TargetMemoryExpiresAfterConfiguredLifetime()
        {
            TargetTrackingMemory memory = new();
            memory.Record(Vector3.right, Vector3.zero, 1f, 0.25f, 12);

            Assert.That(memory.TryGetNext(
                Vector3.zero, 6.01f, 5f, 0.2f, out _), Is.False);
            Assert.That(memory.Count, Is.Zero);
        }

        [Test]
        public void ProjectileAttackLineOfFireDetectsConfiguredCover()
        {
            GameObject attacker = CreateGameObject("Attacker");
            attacker.layer = 31;
            attacker.AddComponent<CircleCollider2D>().radius = 0.25f;
            GameObject target = CreateGameObject("Target");
            target.layer = 31;
            target.transform.position = Vector3.right * 2f;
            target.AddComponent<CircleCollider2D>().radius = 0.25f;
            GameObject wall = CreateGameObject("Cover");
            wall.layer = 31;
            wall.transform.position = Vector3.right;
            wall.AddComponent<BoxCollider2D>().size =
                new Vector2(0.2f, 2f);
            Physics2D.SyncTransforms();

            Assert.That(LineOfFireUtility.HasClearPath(
                attacker.transform.position,
                target.transform.position,
                attacker.transform,
                target.transform,
                1 << 31), Is.False);

            wall.SetActive(false);
            Physics2D.SyncTransforms();
            Assert.That(LineOfFireUtility.HasClearPath(
                attacker.transform.position,
                target.transform.position,
                attacker.transform,
                target.transform,
                1 << 31), Is.True);
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

        private sealed class FloatBindingNode : AiNode
        {
            private float value;
            public float Value => value;

            protected override NodeState OnTick() => NodeState.Success;
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
