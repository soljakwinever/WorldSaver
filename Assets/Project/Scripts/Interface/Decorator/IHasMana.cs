namespace Project.Scripts.Interface.Decorator
{
    public interface IHasMana
    {
        int CurrentMana { get; }
        int MaxMana { get; }

        void RestoreMana(int amount);
    }
}
