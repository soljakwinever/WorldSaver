using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class AutoItemCatalog : ItemCatalog
    {
        private const string ResourcesPath = "Items";

        [SerializeField] private ItemData[] blacklist = Array.Empty<ItemData>();

        protected override void Awake()
        {
            SetItems(LoadItems());
            base.Awake();
        }

        private ItemData[] LoadItems()
        {
            ItemData[] loadedItems = Resources.LoadAll<ItemData>(ResourcesPath);
            if (blacklist == null || blacklist.Length == 0)
                return loadedItems;

            HashSet<ItemData> excludedItems = new(blacklist);
            List<ItemData> allowedItems = new(loadedItems.Length);
            for (int i = 0; i < loadedItems.Length; i++)
            {
                ItemData item = loadedItems[i];
                if (!excludedItems.Contains(item))
                    allowedItems.Add(item);
            }

            return allowedItems.ToArray();
        }
    }
}
