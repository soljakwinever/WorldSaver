namespace Project.Scripts.Interface
{
    /// <summary>
    /// Marks a hotbar action that may repeat while its input remains held.
    /// </summary>
    public interface IRepeatsWhileHeld
    {
        float RepeatInterval { get; }
    }
}
