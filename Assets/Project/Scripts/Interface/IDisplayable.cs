using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IDisplayable
    {
        Sprite Sprite { get; }
        string Label { get; }
        int Count { get; }
        float Refresh { get; }
        bool DisplayCount { get; }
    }
}