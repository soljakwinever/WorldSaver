namespace Project.Scripts
{
    /// <summary>
    /// Opts a target-based tool action into its own proximity requirement.
    /// Kept with the tool data contracts to avoid an assembly dependency cycle.
    /// </summary>
    public interface IRequiresToolProximity
    {
        bool IsWithinToolRange(ToolActionContext context);
    }
}
