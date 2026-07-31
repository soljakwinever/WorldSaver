using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    /// <summary>
    /// Describes a world-space cursor supplied by the selected hotbar action.
    /// </summary>
    public interface IUsesCursor
    {
        bool TryGetCursor(
            ActionContext context,
            out PlacementCursorData cursor);
    }

    public readonly struct PlacementCursorData
    {
        public Sprite Sprite { get; }
        public Vector3 Position { get; }
        public bool IsValid { get; }
        public float Opacity { get; }

        public PlacementCursorData(
            Sprite sprite,
            Vector3 position,
            bool isValid,
            float opacity)
        {
            Sprite = sprite;
            Position = position;
            IsValid = isValid;
            Opacity = Mathf.Clamp01(opacity);
        }
    }
}
