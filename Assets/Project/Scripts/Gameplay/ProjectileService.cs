using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    /// <summary>
    /// Pools projectile prefabs by identity and supplies AttackService to every
    /// impact. The service has no assumptions about who launched a projectile.
    /// </summary>
    public sealed class ProjectileService : IProjectileService, IDisposable
    {
        private readonly IAttackService _attackService;
        private readonly Dictionary<GameObject, Stack<Projectile>> _inactive =
            new();
        private readonly Dictionary<Projectile, GameObject> _prefabByInstance =
            new();
        private readonly HashSet<Projectile> _active = new();

        private Transform _poolRoot;

        public ProjectileService(IAttackService attackService)
        {
            _attackService = attackService ??
                throw new ArgumentNullException(nameof(attackService));
        }

        public bool TryLaunch(ProjectileLaunchContext context)
        {
            if (context.Projectile == null ||
                context.Attack.Attacker == null ||
                context.Direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            GameObject prefab = context.Projectile.prefab;
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"{context.Projectile.name} has no projectile prefab.",
                    context.Projectile);
                return false;
            }

            Projectile projectile = Take(prefab);
            if (projectile == null)
                return false;

            _active.Add(projectile);
            projectile.Launch(context, _attackService, Release);
            return true;
        }

        public void Dispose()
        {
            foreach (Projectile projectile in _active)
                if (projectile != null)
                    UnityEngine.Object.Destroy(projectile.gameObject);

            foreach (Stack<Projectile> pool in _inactive.Values)
                foreach (Projectile projectile in pool)
                    if (projectile != null)
                        UnityEngine.Object.Destroy(projectile.gameObject);

            _active.Clear();
            _inactive.Clear();
            _prefabByInstance.Clear();

            if (_poolRoot != null)
                UnityEngine.Object.Destroy(_poolRoot.gameObject);
            _poolRoot = null;
        }

        private Projectile Take(GameObject prefab)
        {
            if (_inactive.TryGetValue(prefab, out Stack<Projectile> pool))
            {
                while (pool.Count > 0)
                {
                    Projectile pooled = pool.Pop();
                    if (pooled != null)
                        return pooled;
                }
            }

            GameObject instance =
                UnityEngine.Object.Instantiate(prefab, GetPoolRoot());
            Projectile projectile =
                instance.GetComponent<Projectile>() ??
                instance.AddComponent<Projectile>();
            _prefabByInstance[projectile] = prefab;
            return projectile;
        }

        private void Release(Projectile projectile)
        {
            if (projectile == null || !_active.Remove(projectile))
                return;

            if (!_prefabByInstance.TryGetValue(
                    projectile,
                    out GameObject prefab) ||
                prefab == null)
            {
                UnityEngine.Object.Destroy(projectile.gameObject);
                return;
            }

            projectile.Deactivate();
            projectile.transform.SetParent(GetPoolRoot(), false);

            if (!_inactive.TryGetValue(prefab, out Stack<Projectile> pool))
            {
                pool = new Stack<Projectile>();
                _inactive.Add(prefab, pool);
            }
            pool.Push(projectile);
        }

        private Transform GetPoolRoot()
        {
            if (_poolRoot != null)
                return _poolRoot;

            GameObject root = new("Projectiles");
            _poolRoot = root.transform;
            return _poolRoot;
        }
    }
}
