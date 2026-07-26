using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class ItemCatalog : MonoBehaviour
    {
        [SerializeField] private ItemData[] items = Array.Empty<ItemData>();

        private Dictionary<string, ItemData> _itemsById;

        public IReadOnlyList<ItemData> Items => items;

        private void Awake()
        {
            EnsureInitialized();
        }

        public bool TryGet(string persistentId, out ItemData item)
        {
            EnsureInitialized();
            return _itemsById.TryGetValue(persistentId, out item);
        }

        public bool Contains(ItemData item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.persistentId))
                return false;

            return TryGet(item.persistentId, out ItemData registered) && registered == item;
        }

        private void EnsureInitialized()
        {
            if (_itemsById != null)
                return;

            _itemsById = BuildLookup(items);
        }

        private static Dictionary<string, ItemData> BuildLookup(IReadOnlyList<ItemData> catalog)
        {
            Dictionary<string, ItemData> result = new(StringComparer.Ordinal);
            for (int i = 0; i < catalog.Count; i++)
            {
                ItemData item = catalog[i];
                if (item == null)
                    throw new InvalidOperationException($"Item catalog entry {i} is null.");
                if (string.IsNullOrWhiteSpace(item.persistentId))
                    throw new InvalidOperationException(
                        $"Item catalog entry '{item.name}' has no persistent ID.");
                if (!result.TryAdd(item.persistentId, item))
                    throw new InvalidOperationException(
                        $"The item catalog contains duplicate persistent ID '{item.persistentId}'.");
            }

            return result;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _itemsById = null;
        }
#endif
    }
}
