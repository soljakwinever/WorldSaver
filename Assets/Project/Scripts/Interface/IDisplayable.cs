using UnityEngine;

namespace Project.Scripts.Interface
{
    /// <summary>Optional count display used by hotbar actions.</summary>
    public interface IDisplayable
    {
        Sprite Sprite { get; }
        string Label { get; }
        int Count { get; }
        float Refresh { get; }
        bool DisplayCount { get; }
    }
}
