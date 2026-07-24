namespace Project.Scripts.Interface
{
    /// <summary>
    /// Gates repeatable operations. Pure conditions return either long.MaxValue or zero;
    /// resource-backed conditions return the number of operations currently affordable.
    /// </summary>
    public interface IOperationCondition
    {
        bool ConsumesOperations { get; }
        long AvailableOperations { get; }
        void Commit(long operations);
    }
}
