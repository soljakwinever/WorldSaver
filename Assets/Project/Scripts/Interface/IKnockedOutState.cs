namespace Project.Scripts.Interface
{
    public interface IKnockedOutState
    {
        bool IsKnockedOut { get; }
        bool TryStruggle();
    }
}
