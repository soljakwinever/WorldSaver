using System;
using System.Collections.Generic;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.UI
{
    /// <summary>
    /// Type-keyed pool for short-lived visual effects hosted by the world UI.
    /// Any component implementing IPooledVisualEffect can reuse this pool.
    /// </summary>
    public sealed class EffectSpawner : MonoBehaviour, IEffectSpawner
    {
        private readonly Dictionary<Type, Stack<IPooledVisualEffect>> _pool =
            new();

        private RectTransform _worldUi;
        private Transform _inactiveRoot;
        private DiContainer _container;

        [Inject]
        public void Construct(
            [Inject(Id = "WorldUI")] RectTransform worldUi,
            DiContainer container)
        {
            _worldUi = worldUi;
            _container = container;
            GameObject root = new("Pooled Visual Effects");
            root.SetActive(false);
            _inactiveRoot = root.transform;
            _inactiveRoot.SetParent(transform, false);
        }

        public T Spawn<T>(Vector3 worldPosition)
            where T : Component, IPooledVisualEffect
        {
            Type type = typeof(T);
            if (!_pool.TryGetValue(type, out Stack<IPooledVisualEffect> items))
            {
                items = new Stack<IPooledVisualEffect>();
                _pool.Add(type, items);
            }

            T effect;
            if (items.Count > 0)
            {
                effect = (T)items.Pop();
            }
            else
            {
                GameObject instance = new(type.Name, typeof(RectTransform));
                effect = instance.AddComponent<T>();
                _container.InjectGameObject(instance);
            }

            Transform effectTransform = effect.transform;
            effectTransform.SetParent(_worldUi, false);
            effectTransform.position = worldPosition;
            effect.gameObject.SetActive(true);
            effect.OnEffectSpawned(this);
            return effect;
        }

        public void Despawn(IPooledVisualEffect effect)
        {
            if (effect is not Component component || component == null)
                return;

            Type type = component.GetType();
            if (!_pool.TryGetValue(type, out Stack<IPooledVisualEffect> items))
            {
                items = new Stack<IPooledVisualEffect>();
                _pool.Add(type, items);
            }

            effect.OnEffectDespawned();
            component.gameObject.SetActive(false);
            component.transform.SetParent(_inactiveRoot, false);
            items.Push(effect);
        }
    }
}
