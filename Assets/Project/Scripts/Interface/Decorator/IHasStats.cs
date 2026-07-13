namespace Project.Scripts.Interface.Decorator
{
    public interface IHasStats
    {
        float EnergyDrainRate { get; set; }
        float HungerEnergyRegenerationRate { get; set; }
        float HungerDrainRate { get; set; }
    }
}