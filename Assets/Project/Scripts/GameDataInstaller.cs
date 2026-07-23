using Project.Scripts;
using Project.Scripts.Bus;
using Project.Scripts.Gameplay;
using Project.Scripts.Core;
using Project.Scripts.Interface;
using Project.Scripts.Persistence;
using UnityEngine;
using Zenject;

public class GameDataInstaller : MonoInstaller
{
    public Chunk chunkPrefab;
    public Node nodePrefab;

    public RectTransform worldUi;
    
    public override void InstallBindings()
    {
        Container.Bind<ItemCatalog>().FromComponentInHierarchy().AsSingle();
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

        Container.Bind<BiomeData[]>().FromMethod(t => Resources.LoadAll<BiomeData>("Biomes") 
        ).AsSingle();

        Container.Bind<RectTransform>().WithId("WorldUI").FromInstance(worldUi);

        Container.BindInterfacesAndSelfTo<ChunkGenerator>().AsSingle().NonLazy();
        Container.Bind<WorldGeneration>().FromNew().AsSingle();

        Container.Bind<Chunkloader>().FromComponentInHierarchy().AsSingle();

        Container.Bind<IRegionDiskStore>().To<FileRegionDiskStore>().AsSingle();
        Container.Bind<IRegionRepository>().To<RegionRepository>().AsSingle();
        Container.BindInterfacesAndSelfTo<WorldClock>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        Container.Bind<DataController>()
            .FromNewComponentOnNewGameObject()
            .AsSingle()
            .NonLazy();
        
        Container.Bind<PlayerDataController>().FromComponentInHierarchy().AsSingle();
        
        Container.Bind<MapSignalBus>().FromNew().AsSingle().NonLazy();
        Container.Bind<TimeSignalBus>().FromNew().AsSingle().NonLazy();
        Container.Bind<PlayerBus>().FromNew().AsSingle().NonLazy();

    }
}
