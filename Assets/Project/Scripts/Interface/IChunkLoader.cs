namespace Project.Scripts.Interface
{
    public interface IChunkLoader
    {
        int LoadedChunks { get; }
        float CurrentAmbientTemperature { get; }
        void ReportSpawn(IChunk chunk);
    }
}
