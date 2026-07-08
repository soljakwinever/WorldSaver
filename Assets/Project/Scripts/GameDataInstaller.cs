using Project.Scripts;
using UnityEngine;
using Zenject;

public class GameDataInstaller : MonoInstaller
{
    public Chunk chunkPrefab;
    public Node nodePrefab;
    
    public override void InstallBindings()
    {
        Container.Bind<Chunk>().FromInstance(chunkPrefab);

        Container
            .BindMemoryPool<Chunk, Chunk.Pool>()
            .WithInitialSize(32)
            .FromComponentInNewPrefab(chunkPrefab)
            .UnderTransformGroup("Chunks");

        Container.BindMemoryPool<Node, Node.Pool>()
            .WithInitialSize(1024)
            .FromComponentInNewPrefab(nodePrefab)
            .UnderTransformGroup("Nodes");

        Container.Bind<BiomeData[]>().FromMethod(t => Resources.LoadAll<BiomeData>("Biomes") 
        ).AsSingle();
        
        Container.Bind<ChunkGenerator>().FromNew().AsSingle();
        Container.Bind<WorldGeneration>().FromNew().AsSingle();
    }
}