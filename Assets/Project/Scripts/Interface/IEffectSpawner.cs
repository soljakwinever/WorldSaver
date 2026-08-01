using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IPooledVisualEffect
    {
        void OnEffectSpawned(IEffectSpawner spawner);
        void OnEffectDespawned();
    }

    public interface IEffectSpawner
    {
        T Spawn<T>(Vector3 worldPosition)
            where T : Component, IPooledVisualEffect;

        void Despawn(IPooledVisualEffect effect);
    }
}
