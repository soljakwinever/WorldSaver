namespace Project.Scripts.Interface
{
    public interface IToolData
    {
        public string ToolName { get; }
        public ToolType ToolType { get; }
        public int Power { get; }
        public float StaminaCost { get; }
    }
    
    public enum ToolType
    {
        Axe,
        Pickaxe,
        Hoe,
        WateringCan,
        Shovel,
        FishingRod,
        Hammer,
        Sword,
        Spear,
        None
    }
}