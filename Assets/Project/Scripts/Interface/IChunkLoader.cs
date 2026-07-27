namespace Project.Scripts.Interface
{
    public interface IChunkLoader
    {
        int LoadedChunks { get; }
        void ReportSpawn(IChunk chunk);
    }
}