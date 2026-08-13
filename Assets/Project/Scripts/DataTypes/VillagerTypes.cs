namespace Project.Scripts.Entities
{
    public enum VillagerRole : byte
    {
        Generalist, Gatherer, Hunter, Builder, Crafter, Guard, Farmer
    }

    [System.Flags]
    public enum VillagerJobMask : ushort
    {
        None = 0, Gather = 1 << 0, Hunt = 1 << 1,
        Build = 1 << 2, Craft = 1 << 3, Defend = 1 << 4,
        Farm = 1 << 5, All = Gather | Hunt | Build | Craft | Defend | Farm
    }

    public enum VillagerJobType : byte
    {
        None, Gather, Hunt, Build, Craft, Defend, Farm, Eat, Sleep
    }

    public enum TownJobPriority : byte
    {
        Off, Low, Normal, High, Emergency
    }

    public enum TownJobStatus : byte
    {
        Queued, Claimed, AwaitingWorldCommit, Completed, Blocked, Cancelled
    }

    public enum VillagerMode : byte
    {
        Idle, MovingToTarget, MovingToSleep, Working, Eating, Sleeping,
        NightSleeping,
        AwaitingWorldCommit, Dead
    }

    public enum VillagerWorkPhase : byte
    {
        Work,
        Delivering
    }
}
