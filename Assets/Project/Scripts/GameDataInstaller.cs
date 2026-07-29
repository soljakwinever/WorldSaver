using IngameDebugConsole;
using Project.Scripts;
using Project.Scripts.Bus;
using Project.Scripts.Gameplay;
using Project.Scripts.Core;
using Project.Scripts.Interface;
using Project.Scripts.Persistence;
using Project.Scripts.Pathfinding;
using Project.Scripts.UI;
using Project.Scripts.TimeAndWeather;
using UnityEngine;
using Zenject;

public class GameDataInstaller : MonoInstaller
{
    public Chunk chunkPrefab;
    public Node nodePrefab;
    public ItemStackPickup itemStackPrefab;

    public DebugLogManager console;

    public RectTransform worldUi;
    
    public override void InstallBindings()
    {
        Container.Bind<ItemCatalog>().FromComponentInHierarchy().AsSingle();
        Container.Bind<ICraftingRandom>()
            .To<CraftingService.UnityCraftingRandom>()
            .AsSingle();
        Container.Bind<ICraftingService>().To<CraftingService>().AsSingle();
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

        Container.BindMemoryPool<NPCSpawnInstance, NPCSpawnPool>()
            .WithInitialSize(32)
            .WithMaxSize(512)
            .FromNewComponentOnNewGameObject()
            .UnderTransformGroup("NPCs");
        Container.Bind<IItemStackPickupPool>()
            .To<ItemStackPickupPool>()
            .FromResolve();

        Container.Bind<BiomeData[]>().FromMethod(t => Resources.LoadAll<BiomeData>("Biomes") 
        ).AsSingle();

        Container.Bind<RectTransform>().WithId("WorldUI").FromInstance(worldUi);

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
        Container.Bind<Chunkloader>().FromComponentInHierarchy().AsSingle();
        Container.BindInterfacesAndSelfTo<TileSpreadSystem>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.Bind<INPCSpawnEnvironmentProvider>()
            .To<NullNPCSpawnEnvironmentProvider>()
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
        
        Container.Bind<PlayerDataController>().FromComponentInHierarchy().AsSingle();
        Container.Bind<ITimeController>()
            .FromComponentInHierarchy()
            .AsSingle();
        
        Container.Bind<MapSignalBus>().FromNew().AsSingle().NonLazy();
        Container.Bind<TimeSignalBus>().FromNew().AsSingle().NonLazy();
        Container.Bind<WeatherSimulationSettings>()
            .FromMethod(_ => LoadWeatherSettings())
            .AsSingle();
        Container.Bind<IWeatherModifierSource>()
            .To<NullWeatherModifierSource>()
            .AsSingle()
            .IfNotBound();
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
        Container.BindInterfacesAndSelfTo<WeatherEffectPresenter>()
            .AsSingle()
            .NonLazy();
        Container.Bind<PlayerBus>().FromNew().AsSingle().NonLazy();
        Container.Bind<EntityBus>().FromNew().AsSingle().NonLazy();
        Container.Bind<IAttackService>().To<AttackService>().AsSingle();

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
