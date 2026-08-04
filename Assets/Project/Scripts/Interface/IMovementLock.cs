namespace Project.Scripts.Interface
{
    /// <summary>Shared movement gate used by attacks and other timed actions.</summary>
    public interface IMovementLock
    {
        bool IsMovementLocked { get; }
    }
}
