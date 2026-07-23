namespace Project.Scripts.Core
{
    public interface IWorldClock
    {
        long CurrentTick { get; }
        void Save();
    }
}
