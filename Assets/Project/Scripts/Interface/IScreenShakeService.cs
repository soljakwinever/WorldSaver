using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public interface IScreenShakeService
    {
        void Shake(ScreenShakeRequest request);
        void SetContinuous(string channel, ScreenShakeRequest request);
        void ClearContinuous(string channel);
    }
}
