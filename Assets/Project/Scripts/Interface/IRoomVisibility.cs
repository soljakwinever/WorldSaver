using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IRoomVisibility
    {
        bool IsPlayerInsideRoom { get; }
        bool IsWorldPositionMasked(Vector3 worldPosition);
    }
}
