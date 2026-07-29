using UnityEngine;
using Zenject;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;

namespace Project.Scripts
{
    public sealed class NPCSpawnInstance : MonoBehaviour
    {
        internal GameObject Prefab { get; set; }
        internal GameObject Visual { get; set; }
    }

    /// <summary>Dedicated MonoMemoryPool for transient, chunk-owned NPCs.</summary>
    public sealed class NPCSpawnPool :
        MonoMemoryPool<GameObject, Vector3, EnemyData, NPCSpawnInstance>
    {
        private readonly DiContainer _container;

        public NPCSpawnPool(DiContainer container)
        {
            _container = container;
        }

        protected override void Reinitialize(
            GameObject prefab,
            Vector3 position,
            EnemyData enemyData,
            NPCSpawnInstance item)
        {
            if (prefab == null)
                return;

            // The pooled shell contains the rule prefab as its only child. This permits
            // different enemy and villager prefabs to share one chunk population pool.
            if (item.Prefab != prefab || item.Visual == null)
            {
                if (item.Visual != null)
                {
                    item.Visual.SetActive(false);
                    Object.Destroy(item.Visual);
                }
                item.Visual = _container.InstantiatePrefab(prefab, item.transform);
                item.Visual.name = prefab.name;
                item.Prefab = prefab;
            }

            item.name = $"NPC ({prefab.name})";
            item.transform.position = position;
            item.Visual.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            item.gameObject.SetActive(true);

            if (enemyData != null)
            {
                EnemyRuntime runtime =
                    item.Visual.GetComponent<EnemyRuntime>() ??
                    _container.InstantiateComponent<EnemyRuntime>(item.Visual);
                runtime.Initialize(enemyData);
            }
        }

        protected override void OnDespawned(NPCSpawnInstance item)
        {
            item.name = "Pooled NPC";
            base.OnDespawned(item);
        }
    }
}
