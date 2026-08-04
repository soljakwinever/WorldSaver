using UnityEngine;

namespace Project.Scripts.Interface
{
    /// <summary>Content that can be presented by the universal UI tooltip.</summary>
    public interface IToolTipData
    {
        string DisplayName { get; }
        string Description { get; }
        Color Color { get; }
        int Count { get; }
        Sprite Sprite { get; }
    }
}
