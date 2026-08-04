namespace Project.Scripts.Interface
{
    /// <summary>Tooltip data with a periodically refreshed count display.</summary>
    public interface IDisplayable : IToolTipData
    {
        float Refresh { get; }
        bool DisplayCount { get; }
    }
}
