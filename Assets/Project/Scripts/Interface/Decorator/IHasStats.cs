namespace Project.Scripts.Interface.Decorator
{
    public interface IHasStats
    {
        int Strength { get; }
        int Constitution { get; }
        int Dexterity { get; }
        int Wisdom { get; }
        int Intelligence { get; }
        int Luck { get; }
        float EnergyDrainRate { get; set; }
        float HungerEnergyRegenerationRate { get; set; }
        float HungerDrainRate { get; set; }
    }
}
