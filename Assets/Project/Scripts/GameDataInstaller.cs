using IngameDebugConsole;
using Project.Scripts;
using Project.Scripts.Bus;
using Project.Scripts.Gameplay;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Persistence;
using Project.Scripts.Pathfinding;
using Project.Scripts.UI;
using Project.Scripts.TimeAndWeather;
using Project.Scripts.GameTime;
using UnityEngine;
using Zenject;

public class GameDataInstaller : MonoInstaller
{
    public Chunk chunkPrefab;
    public Node nodePrefab;
    public ItemStackPickup itemStackPrefab;
    public WallDamageVisual wallDamageVisualPrefab;

    public DebugLogManager console;

    public RectTransform worldUi;
    public PopTextSettings popTextSettings = new();
    public ItemStackExplosionSettings itemStackExplosionSettings = new();
    
    public override void InstallBindings()
    {
        Container.Bind<ItemCatalog>().FromComponentInHierarchy().AsSingle();
        Container.Bind<SkillCatalog>().AsSingle();
        Container.Bind<ICraftingRandom>()
            .To<CraftingService.UnityCraftingRandom>()
            .AsSingle();
        Container.Bind<ICraftingService>().To<CraftingService>().AsSingle();
        Container.Bind<IVillagerWorldAdapter>()
            .To<VillagerWorldAdapter>().AsSingle();
        Container.BindInterfacesAndSelfTo<ComponentWindowService>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.BindInterfacesAndSelfTo<InputManager>().AsSingle().NonLazy();
        Container.Bind<Chunk>().FromInstance(chunkPrefab);

        Container
            .BindMemoryPool<Chunk, Chunk.Pool>()
            .WithInitialSize(32)
            .WithMaxSize(1024)
            .FromComponentInNewPrefab(chunkPrefab)
            .UnderTransformGroup("Chunks");

        Container.BindMemoryPool<Node, Node.Pool>()
            .WithInitialSize(1024)
            .FromComponentInNewPrefab(nodePrefab)
            .UnderTransformGroup("Nodes");

        Container.BindMemoryPool<ItemStackPickup, ItemStackPickupPool>()
            .WithInitialSize(30)
            .WithMaxSize(100)
            .FromComponentInNewPrefab(itemStackPrefab)
            .UnderTransformGroup("ItemPickups");

        Container.BindMemoryPool<WallDamageVisual, WallDamageVisualPool>()
            .WithInitialSize(32)
            .WithMaxSize(1024)
            .FromComponentInNewPrefab(wallDamageVisualPrefab)
            .UnderTransformGroup("WallDamageVisuals");

        Container.BindMemoryPool<NPCSpawnInstance, NPCSpawnPool>()
            .WithInitialSize(32)
            .WithMaxSize(512)
            .FromNewComponentOnNewGameObject()
            .UnderTransformGroup("NPCs");
        Container.Bind<IItemStackPickupPool>()
            .To<ItemStackPickupPool>()
            .FromResolve();
        itemStackExplosionSettings ??= new ItemStackExplosionSettings();
        Container.BindInstance(itemStackExplosionSettings);
        Container.Bind<IItemStackExplosionService>()
            .To<ItemStackExplosionService>()
            .AsSingle();

        Container.Bind<BiomeData[]>().FromMethod(t => Resources.LoadAll<BiomeData>("Biomes") 
        ).AsSingle();

        Container.Bind<RectTransform>().WithId("WorldUI").FromInstance(worldUi);
        popTextSettings ??= new PopTextSettings();
        Container.BindInstance(popTextSettings);
        Container.BindInterfacesAndSelfTo<EffectSpawner>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.BindInterfacesAndSelfTo<DamagePopTextPresenter>()
            .AsSingle()
            .NonLazy();

        Container.BindInterfacesAndSelfTo<ChunkGenerator>().AsSingle().NonLazy();
        Container.Bind<WorldGeneration>().FromNew().AsSingle();
        Container.Bind<IWorldGenerator>().To<WorldGeneration>().FromResolve();
        Container.BindInterfacesAndSelfTo<WorldPathFindingMap>()
            .AsSingle()
            .NonLazy();
        Container.Bind<IPathFindingService>()
            .To<PathFindingService>()
            .AsSingle();

        Container.BindInterfacesAndSelfTo<WorldTilemapRenderer>()
            .FromComponentInHierarchy()
            .AsSingle()
            .NonLazy();
        Container.BindInterfacesAndSelfTo<Chunkloader>().FromComponentInHierarchy().AsSingle();
        Container.BindInterfacesAndSelfTo<RoomDetectionSystem>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.BindInterfacesAndSelfTo<RoomVisibilityController>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.BindInterfacesAndSelfTo<TileSpreadSystem>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.Bind<INPCSpawnEnvironmentProvider>()
            .To<PlayerNPCSpawnEnvironmentProvider>()
            .AsSingle()
            .IfNotBound();
        Container.BindInterfacesAndSelfTo<NPCSpawnController>()
            .AsSingle()
            .NonLazy();

        Container.Bind<IRegionDiskStore>().To<FileRegionDiskStore>().AsSingle();
        Container.Bind<IRegionRepository>().To<RegionRepository>().AsSingle();
        Container.Bind<IRegionSimulationService>()
            .To<RegionSimulationService>()
            .AsSingle();
        Container.BindInterfacesAndSelfTo<WorldClock>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.Bind<DataController>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.Bind<IWorldSaveService>()
            .To<DataController>()
            .FromResolve();
        
        Container.Bind<PlayerDataController>().FromComponentInHierarchy().AsSingle();
        Container.BindInterfacesAndSelfTo<PlayerHUD>()
            .FromComponentInHierarchy()
            .AsSingle();
        Container.BindInterfacesTo<PlayerGiveItemCommand>().AsSingle().NonLazy();
        Container.BindInterfacesTo<VillagerSpawnCommand>().AsSingle().NonLazy();
        Container.BindInterfacesAndSelfTo<TimeController>()
            .FromComponentInHierarchy()
            .AsSingle();
        Container.BindInterfacesAndSelfTo<VillagerNightService>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.BindInterfacesAndSelfTo<EventService>()
            .AsSingle()
            .NonLazy();
        
        Container.Bind<MapSignalBus>().FromNew().AsSingle().NonLazy();
        Container.Bind<TimeSignalBus>().FromNew().AsSingle().NonLazy();
        Container.BindInterfacesAndSelfTo<AudioService>()
            .AsSingle()
            .NonLazy();
        Container.Bind<DangerSettings>()
            .FromMethod(_ => LoadDangerSettings())
            .AsSingle();
        Container.BindInterfacesAndSelfTo<DangerService>()
            .AsSingle()
            .NonLazy();
        Container.Bind<WeatherSimulationSettings>()
            .FromMethod(_ => LoadWeatherSettings())
            .AsSingle();
        Container.BindInterfacesAndSelfTo<ClimateCoreInfluenceService>()
            .AsSingle();
        Container.BindInterfacesAndSelfTo<FeatureBuildingService>()
            .AsSingle()
            .NonLazy();
        Container.Bind<WeatherBus>().AsSingle();
        Container.Bind<IWeatherWorldClock>()
            .FromMethod(context => new WeatherWorldClockAdapter(
                context.Container.Resolve<IWorldClock>()))
            .AsSingle();
        Container.BindInterfacesAndSelfTo<RegionalClimateService>().AsSingle();
        Container.BindInterfacesAndSelfTo<RegionalWeatherService>()
            .AsSingle()
            .NonLazy();
        Container.BindInterfacesAndSelfTo<TileCoverageSystem>()
            .AsSingle()
            .NonLazy();
        Container.BindInterfacesAndSelfTo<RoomIndoorWeatherMask>()
            .AsSingle();
        Container.BindInterfacesAndSelfTo<WeatherEffectPresenter>()
            .AsSingle()
            .NonLazy();
        Container.BindInterfacesAndSelfTo<EarthquakeWeatherController>()
            .AsSingle()
            .NonLazy();
        Container.Bind<PlayerBus>().FromNew().AsSingle().NonLazy();
        Container.Bind<EntityBus>().FromNew().AsSingle().NonLazy();
        Container.BindInterfacesTo<PersistentEntityRemovalBridge>()
            .AsSingle()
            .NonLazy();
        Container.Bind<IAttackService>().To<AttackService>().AsSingle();
        Container.BindInterfacesAndSelfTo<AreaAttackService>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.BindInterfacesAndSelfTo<ProjectileService>()
            .AsSingle();

        Container.Bind<Grid>().FromComponentInHierarchy().AsSingle();

        Container.Bind<DebugLogManager>().FromComponentInNewPrefab(console).AsSingle().NonLazy();
    }

    private static WeatherSimulationSettings LoadWeatherSettings()
    {
        WeatherSimulationSettings settings =
            Resources.Load<WeatherSimulationSettings>(
                "Weather/WeatherSimulationSettings");

        if (settings != null)
            return settings;

        Debug.LogWarning(
            "No Resources/Weather/WeatherSimulationSettings asset was found. " +
            "Regional weather will run with default climate settings and clear weather.");
        return ScriptableObject.CreateInstance<WeatherSimulationSettings>();
    }

    private static DangerSettings LoadDangerSettings()
    {
        DangerSettings settings =
            Resources.Load<DangerSettings>("Audio/DangerSettings");
        if (settings != null)
            return settings;

        Debug.LogWarning(
            "No Resources/Audio/DangerSettings asset was found. " +
            "Using default danger settings.");
        return ScriptableObject.CreateInstance<DangerSettings>();
    }

    private sealed class WeatherWorldClockAdapter : IWeatherWorldClock
    {
        private readonly IWorldClock _clock;

        public WeatherWorldClockAdapter(IWorldClock clock)
        {
            _clock = clock;
        }

        public long CurrentTick => _clock.CurrentTick;
    }
}
