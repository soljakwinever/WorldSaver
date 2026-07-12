namespace Project.Scripts.Bus
{
    public delegate void ChunkBuiltHandler(ChunkBuildResult result);
    public class MapSignalBus
    {
        public event ChunkBuiltHandler ChunkBuilt;
        
        public void RaiseChunkBuilt(ChunkBuildResult result)
        {
            ChunkBuilt?.Invoke(result);
        }
    }
}