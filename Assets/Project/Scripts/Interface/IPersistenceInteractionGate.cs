namespace Project.Scripts.Interface
{
    // Implement this on harvesting, inventory, AI, or other interaction
    // behaviours that must remain disabled until persistence restoration ends.
    public interface IPersistenceInteractionGate
    {
        void SetPersistenceReady(bool ready);
    }
}
