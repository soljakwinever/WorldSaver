using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Entities;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.Persistence;
using Unity.Entities;
using Unity.Core;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Zenject;
using RuntimeEntityArchetype = Project.Scripts.DataTypes.EntityArchetype;

namespace Project.Tests.Editor
{
    public sealed class VillagerEcsTests
    {
        [Test]
        public void NeedsDrainAndStarvationKillsVillager()
        {
            using World world = new("Villager needs test");
            Entity entity = world.EntityManager.CreateEntity(
                typeof(VillagerTag), typeof(VillagerNeeds),
                typeof(VillagerStats), typeof(VillagerState));
            VillagerStats stats = VillagerDefaults.CreateStats();
            stats.hungerDrainPerSecond = 10f;
            stats.starvationDamagePerSecond = 10f;
            VillagerNeeds needs = VillagerDefaults.CreateNeeds();
            needs.hunger = 5f;
            needs.health = 5;
            world.EntityManager.SetComponentData(entity, stats);
            world.EntityManager.SetComponentData(entity, needs);
            world.EntityManager.SetComponentData(entity,
                new VillagerState { mode = VillagerMode.Idle, activeJobEntity = Entity.Null });

            SystemHandle system = world.GetOrCreateSystem<VillagerNeedsSystem>();
            world.SetTime(new TimeData(1d, 1f));
            system.Update(world.Unmanaged);

            VillagerNeeds result = world.EntityManager.GetComponentData<VillagerNeeds>(entity);
            VillagerState state = world.EntityManager.GetComponentData<VillagerState>(entity);
            Assert.That(result.hunger, Is.Zero);
            Assert.That(result.health, Is.Zero);
            Assert.That(state.mode, Is.EqualTo(VillagerMode.Dead));
        }

        [Test]
        public void AssignmentClaimsHighestPriorityCompatibleJob()
        {
            using World world = new("Villager jobs test");
            Entity villager = world.EntityManager.CreateEntity(
                typeof(VillagerTag), typeof(VillagerIdentity), typeof(VillagerAssignment),
                typeof(VillagerState), typeof(VillagerOrder), typeof(LocalTransform));
            world.EntityManager.SetComponentData(villager,
                new VillagerIdentity { townId = "town" });
            world.EntityManager.SetComponentData(villager,
                new VillagerAssignment { role = VillagerRole.Builder, allowedJobs = VillagerJobMask.Build });
            world.EntityManager.SetComponentData(villager,
                new VillagerState { mode = VillagerMode.Idle, activeJobEntity = Entity.Null });
            world.EntityManager.SetComponentData(villager, LocalTransform.FromPosition(float3.zero));

            Entity low = CreateJob(world, 1, VillagerJobType.Build, TownJobPriority.Low);
            Entity high = CreateJob(world, 2, VillagerJobType.Build, TownJobPriority.High);
            CreateJob(world, 3, VillagerJobType.Gather, TownJobPriority.Emergency);

            world.GetOrCreateSystem<VillagerJobAssignmentSystem>().Update(world.Unmanaged);

            VillagerState state = world.EntityManager.GetComponentData<VillagerState>(villager);
            Assert.That(state.activeJobEntity, Is.EqualTo(high));
            Assert.That(world.EntityManager.GetComponentData<TownJob>(high).status,
                Is.EqualTo(TownJobStatus.Claimed));
            Assert.That(world.EntityManager.GetComponentData<TownJob>(low).status,
                Is.EqualTo(TownJobStatus.Queued));
        }

        [Test]
        public void WorkSystemFollowsAStarWaypointsInsteadOfDirectDestination()
        {
            using World world = new("Villager waypoint movement test");
            Entity villager = world.EntityManager.CreateEntity(
                typeof(VillagerTag), typeof(VillagerNeeds), typeof(VillagerStats),
                typeof(VillagerState), typeof(VillagerOrder),
                typeof(VillagerPathState), typeof(LocalTransform));
            DynamicBuffer<VillagerWaypoint> waypoints =
                world.EntityManager.AddBuffer<VillagerWaypoint>(villager);
            waypoints.Add(new VillagerWaypoint { value = new float3(0f, 1f, 0f) });
            waypoints.Add(new VillagerWaypoint { value = new float3(2f, 1f, 0f) });
            waypoints.Add(new VillagerWaypoint { value = new float3(2f, 0f, 0f) });
            world.EntityManager.SetComponentData(villager,
                VillagerDefaults.CreateNeeds());
            VillagerStats stats = VillagerDefaults.CreateStats();
            stats.movementSpeed = 0.5f;
            world.EntityManager.SetComponentData(villager, stats);
            world.EntityManager.SetComponentData(villager,
                new VillagerState
                {
                    mode = VillagerMode.MovingToTarget,
                    activeJobEntity = Entity.Null
                });
            world.EntityManager.SetComponentData(villager,
                new VillagerOrder
                {
                    destination = new float3(2f, 0f, 0f),
                    hasOrder = 1
                });
            world.EntityManager.SetComponentData(villager,
                new VillagerPathState());
            world.EntityManager.SetComponentData(villager,
                LocalTransform.FromPosition(float3.zero));

            world.SetTime(new TimeData(1d, 1f));
            world.GetOrCreateSystem<VillagerWorkSystem>().Update(world.Unmanaged);

            float3 position = world.EntityManager
                .GetComponentData<LocalTransform>(villager).Position;
            Assert.That(position.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(position.y, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void JobBoardPoliciesAndQueueRoundTrip()
        {
            GameObject sourceObject = new("Source job board");
            GameObject restoredObject = new("Restored job board");
            try
            {
                TownJobBoard source = sourceObject.AddComponent<TownJobBoard>();
                source.SetPriority(VillagerJobType.Farm, TownJobPriority.High);
                Assert.That(source.TryIssue(new Project.Scripts.Interface.TownJobRequest(
                    VillagerJobType.Farm, Vector3.one, 2f, TownJobPriority.High), out long id), Is.True);
                byte[] data;
                using (MemoryStream stream = new())
                {
                    using BinaryWriter writer = new(stream);
                    source.WriteState(writer);
                    data = stream.ToArray();
                }

                TownJobBoard restored = restoredObject.AddComponent<TownJobBoard>();
                using MemoryStream input = new(data, writable: false);
                using BinaryReader reader = new(input);
                restored.ReadState(reader, source.PersistentVersion);
                Assert.That(restored.GetPriority(VillagerJobType.Farm), Is.EqualTo(TownJobPriority.High));
                Assert.That(restored.Jobs.Count, Is.EqualTo(1));
                Assert.That(restored.Jobs[0].Id, Is.EqualTo(id));
                Assert.That(restored.Jobs[0].Status, Is.EqualTo(TownJobStatus.Queued));
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(restoredObject);
            }
        }

        [Test]
        public void LiveTargetJobsAreNotRestoredWithoutTheirSceneTargets()
        {
            GameObject sourceObject = new("Source live-target job board");
            GameObject restoredObject = new("Restored live-target job board");
            GameObject target = new("Hunt target");
            try
            {
                TownJobBoard source = sourceObject.AddComponent<TownJobBoard>();
                Assert.That(source.TryIssue(new TownJobRequest(
                    VillagerJobType.Hunt, target.transform.position,
                    target: target), out _), Is.True);

                byte[] data;
                using (MemoryStream stream = new())
                {
                    using BinaryWriter writer = new(stream);
                    source.WriteState(writer);
                    data = stream.ToArray();
                }

                TownJobBoard restored = restoredObject.AddComponent<TownJobBoard>();
                using MemoryStream input = new(data, writable: false);
                using BinaryReader reader = new(input);
                restored.ReadState(reader, source.PersistentVersion);

                Assert.That(restored.Jobs, Is.Empty,
                    "Scene targets cannot be reconstructed after loading, so stale combat jobs must be discarded.");
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(restoredObject);
            }
        }

        [TestCase("Complete")]
        [TestCase("Fail")]
        public void FinishedOrFailedJobIsRemovedImmediately(string methodName)
        {
            GameObject host = new("Job board removal test");
            try
            {
                TownJobBoard board = host.AddComponent<TownJobBoard>();
                Assert.That(board.TryIssue(new TownJobRequest(
                    VillagerJobType.Gather, Vector3.zero), out long id), Is.True);
                MethodInfo method = typeof(TownJobBoard).GetMethod(
                    methodName, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                object[] arguments = methodName == "Fail"
                    ? new object[] { id, "Cannot proceed." }
                    : new object[] { id };
                method.Invoke(board, arguments);
                Assert.That(board.Jobs, Is.Empty);
                Assert.That(board.IsAtBaseline(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ConstructionCancellationRemovesParentAndDependencyJobs()
        {
            GameObject host = new("Construction job cancellation test");
            try
            {
                TownJobBoard board = host.AddComponent<TownJobBoard>();
                const string owner = "12345678901234567890123456789012";
                board.TryIssue(new TownJobRequest(VillagerJobType.Build,
                    Vector3.zero, constructionId: owner), out _);
                board.TryIssue(new TownJobRequest(VillagerJobType.Gather,
                    Vector3.one, constructionId:
                    "123456789012345678901234:g:0123456789abcdef"), out _);
                board.CancelConstructionJobs(owner);
                Assert.That(board.Jobs, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void CancellingGhostAtWorldCellRemovesBlueprintAndDependencies()
        {
            GameObject host = new("Construction ghost erase test");
            try
            {
                TownJobBoard board = host.AddComponent<TownJobBoard>();
                TownConstructionQueue queue =
                    host.AddComponent<TownConstructionQueue>();
                const string blueprintId =
                    "12345678901234567890123456789012";
                var blueprint = new TownConstructionQueue.BlueprintRecord(
                    blueprintId, "wall", new Vector3(4.5f, 7.5f),
                    1f, TownJobPriority.High);
                FieldInfo recordsField = typeof(TownConstructionQueue).GetField(
                    "_records", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(recordsField, Is.Not.Null);
                var records = (List<TownConstructionQueue.BlueprintRecord>)
                    recordsField.GetValue(queue);
                records.Add(blueprint);

                board.TryIssue(new TownJobRequest(VillagerJobType.Build,
                    blueprint.Position, constructionId: blueprintId), out _);
                board.TryIssue(new TownJobRequest(VillagerJobType.Gather,
                    blueprint.Position, constructionId:
                    "123456789012345678901234:g:0123456789abcdef"), out _);

                Assert.That(queue.TryCancelAt(new Vector3(4.1f, 7.9f),
                    out TownConstructionQueue.BlueprintRecord cancelled),
                    Is.True);
                Assert.That(cancelled, Is.SameAs(blueprint));
                Assert.That(queue.Blueprints, Is.Empty);
                Assert.That(board.Jobs, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void UnreachableTargetIsRejectedUntilCacheIsCleared()
        {
            GameObject host = new("Invalid target cache test");
            GameObject target = new("Unreachable target");
            try
            {
                TownJobBoard board = host.AddComponent<TownJobBoard>();
                var request = new TownJobRequest(VillagerJobType.Gather,
                    new Vector3(4f, 5f), target: target);
                Assert.That(board.TryIssue(request, out long id), Is.True);

                MethodInfo mark = typeof(TownJobBoard).GetMethod(
                    "MarkTargetUnreachable",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(mark?.Invoke(board, new object[] { id }), Is.True);
                board.TryCancel(id);

                Assert.That(board.TryIssue(request, out _), Is.False);
                Assert.That(board.TryIssue(new TownJobRequest(
                    VillagerJobType.Gather, new Vector3(5f, 5f), target: target),
                    out _), Is.True, "A different destination cell remains valid.");
                Assert.That(board.TryIssue(new TownJobRequest(
                    VillagerJobType.Hunt, new Vector3(4f, 5f), target: target),
                    out _), Is.True, "A different job type remains valid.");

                board.ClearInvalidTargets();
                Assert.That(board.TryIssue(request, out _), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(host);
            }
        }

        [TestCase("", "generated-town", "generated-town")]
        [TestCase("saved-town", "generated-town", "saved-town")]
        [TestCase("", "", "")]
        public void DoorRestoreFallsBackToSpawnVillageIdentity(
            string savedVillageId,
            string spawnVillageId,
            string expectedVillageId)
        {
            GameObject host = new("Door identity migration test");
            try
            {
                DoorComponent door = host.AddComponent<DoorComponent>();
                FieldInfo spawnVillage = typeof(DoorComponent).GetField(
                    "_spawnVillageId",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(spawnVillage, Is.Not.Null);
                spawnVillage.SetValue(door, spawnVillageId);

                using MemoryStream stream = new();
                using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8,
                           leaveOpen: true))
                {
                    writer.Write(2);
                    writer.Write(3);
                    writer.Write(false);
                    writer.Write(false);
                    writer.Write(25);
                    writer.Write(false);
                    writer.Write(string.Empty);
                    writer.Write(savedVillageId);
                    writer.Write(string.Empty);
                }
                stream.Position = 0;
                using BinaryReader reader = new(stream);
                door.ReadState(reader, 4);

                Assert.That(door.VillageId, Is.EqualTo(expectedVillageId));

                using MemoryStream written = new();
                using (BinaryWriter writer = new(written,
                           System.Text.Encoding.UTF8, leaveOpen: true))
                    door.WriteState(writer);
                written.Position = 0;
                using BinaryReader persisted = new(written);
                persisted.ReadInt32();
                persisted.ReadInt32();
                persisted.ReadBoolean();
                persisted.ReadBoolean();
                persisted.ReadInt32();
                persisted.ReadBoolean();
                persisted.ReadString();
                Assert.That(persisted.ReadString(),
                    Is.EqualTo(expectedVillageId));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void VillagerArchetypeReferencesConfiguredVillagerNode()
        {
            RuntimeEntityArchetype archetype = Resources.Load<RuntimeEntityArchetype>(
                "EntityArchetypes/archetypes.villager.DoggyVillager");

            Assert.That(archetype, Is.Not.Null);
            Assert.That(archetype.Id, Is.EqualTo(43223));
            Assert.That(archetype.NodeData, Is.Not.Null);
            Assert.That(archetype.NodeData.name, Is.EqualTo("DoggyVillager"));
            Assert.That(archetype.NodeData.nodeType,
                Is.EqualTo(NodeData.NodeType.Entity));

            VillagerComponentData[] configurations = archetype.NodeData
                .persistentComponents
                .OfType<VillagerComponentData>()
                .ToArray();
            Assert.That(configurations, Has.Length.EqualTo(1));
            Assert.That(configurations[0].ComponentDefinition,
                Is.TypeOf<VillagerDefinition>());
        }

        [Test]
        public void TownCoreDefinitionCreatesInjectedInteractableTown()
        {
            GameObject host = new("Town Core host");
            GameObject catalogObject = new("Item catalog");
            GameObject player = new("Player");
            TownCoreDefinition definition =
                ScriptableObject.CreateInstance<TownCoreDefinition>();
            try
            {
                ItemCatalog catalog = catalogObject.AddComponent<ItemCatalog>();
                FakeWindowService window = new();
                DiContainer container = new();
                container.BindInstance(catalog);
                container.Bind<IWorldClock>().To<FakeWorldClock>().AsSingle();
                container.BindInstance<IComponentWindowService>(window);

                TownCoreData data = new() { componentDefinition = definition };
                definition.Install(host, container, default, data);

                TownCore town = host.GetComponent<TownCore>();
                Assert.That(town, Is.Not.Null);
                Assert.That(host.GetComponents<TownCore>(), Has.Length.EqualTo(1));
                Assert.That(host.GetComponent<TownWorkDiscovery>(), Is.Not.Null);
                Assert.That(host.GetComponent<VillagerResourceDiscovery>(), Is.Not.Null);

                player.AddComponent<PersistentInventory>();
                InteractionContext interaction = new(
                    player, InteractionType.Direct, null);
                Assert.That(town.CanInteract(interaction), Is.True);
                town.Interact(interaction);
                Assert.That(window.LastRequest, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(catalogObject);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DestroyedUnityTargetResolvesToNullWithoutThrowing()
        {
            GameObject targetObject = new("Destroyed target");
            BoxCollider2D target = targetObject.AddComponent<BoxCollider2D>();
            Object.DestroyImmediate(targetObject);

            MethodInfo resolver = typeof(VillagerEntityBridge).GetMethod(
                "ToGameObject",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(resolver, Is.Not.Null);
            Assert.DoesNotThrow(() =>
            {
                object result = resolver.Invoke(null, new object[] { target });
                Assert.That(result, Is.Null);
            });
        }

        [Test]
        public void WorkApproachCandidatesExcludeBlockedCellsAndPreferNearest()
        {
            GameObject host = new("Villager work approach test");
            try
            {
                VillagerEntityBridge villager =
                    host.AddComponent<VillagerEntityBridge>();
                var map = new SelectivePathMap(
                    new Vector2Int(4, 4),
                    new Vector2Int(5, 4));
                typeof(VillagerEntityBridge).GetField(
                        "_pathMap", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(villager, map);
                MethodInfo candidates = typeof(VillagerEntityBridge).GetMethod(
                    "GetApproachCandidates",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(candidates, Is.Not.Null);
                var result = (List<Vector2Int>)candidates.Invoke(villager,
                    new object[]
                    {
                        new Vector2Int(3, 5),
                        new Vector2Int(5, 5),
                        default(PathFindingQuery)
                    });

                Assert.That(result, Is.EqualTo(new[]
                {
                    new Vector2Int(4, 4),
                    new Vector2Int(5, 4)
                }));
                Assert.That(result.Contains(new Vector2Int(5, 5)), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static Entity CreateJob(World world, long id,
            VillagerJobType type, TownJobPriority priority)
        {
            Entity entity = world.EntityManager.CreateEntity(typeof(TownJobTag), typeof(TownJob));
            world.EntityManager.SetComponentData(entity, new TownJob
            {
                jobId = id, townId = "town", type = type, priority = priority,
                status = TownJobStatus.Queued, targetPosition = new float3(id, 0f, 0f),
                workRequired = 1f, claimedBy = Entity.Null
            });
            return entity;
        }

        private sealed class FakeWorldClock : IWorldClock
        {
            public long CurrentTick => 0;
            public void Save() { }
        }

        private sealed class FakeWindowService : IComponentWindowService
        {
            public bool IsOpen => LastRequest != null;
            public ComponentWindowRequest LastRequest { get; private set; }
            public bool IsPointerOverWindow(Vector2 screenPosition) => false;
            public void Open(ComponentWindowRequest request) => LastRequest = request;
            public void Close() => LastRequest = null;
        }

        private sealed class SelectivePathMap : IPathFindingMap
        {
            private readonly HashSet<Vector2Int> _walkable;

            public SelectivePathMap(params Vector2Int[] walkable) =>
                _walkable = new HashSet<Vector2Int>(walkable);

            public bool IsWalkable(Vector2Int worldCell) =>
                _walkable.Contains(worldCell);

            public float GetTraversalCost(Vector2Int worldCell) =>
                IsWalkable(worldCell) ? 1f : float.PositiveInfinity;
        }
    }
}
