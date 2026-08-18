namespace Project.Scripts.Interface
{
    public enum ShadowSize : byte
    {
        Small,
        Medium,
        Large
    }

    public interface IHasShadow
    {
        ShadowSize ShadowSize { get; }
        float VisualHeight { get; }
        bool IsAirborne { get; }
        void Launch(float peakHeight, float duration);
    }
}
