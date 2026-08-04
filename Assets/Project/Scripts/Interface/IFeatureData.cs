using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IFeatureData
    {
        Vector3 Position { get; }
        Sprite Icon { get; }
        string Name { get; }
    }

    public interface IFeatureSenseSource
    {
        IReadOnlyList<IFeatureData> FindFeatures(Vector3 center, float radius);
    }

    public interface ISenseService
    {
        bool CanSense(GameObject user);
        void Reveal(GameObject user, float radius, float duration);
    }
}
