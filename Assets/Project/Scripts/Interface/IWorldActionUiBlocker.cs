using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IWorldActionUiBlocker
    {
        bool IsPointerOverBlockingUi(Vector2 screenPosition);
    }
}
