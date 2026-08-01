using System;
using IngameDebugConsole;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    public sealed class PlayerGiveItemCommand : IInitializable, IDisposable
    {
        private readonly ItemCatalog _catalog;
        private readonly PersistentInventory _inventory;

        public PlayerGiveItemCommand(ItemCatalog catalog, PlayerDataController player)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _inventory = player != null
                ? player.GetComponent<PersistentInventory>()
                : throw new ArgumentNullException(nameof(player));
        }

        public void Initialize()
        {
            DebugLogConsole.AddCommand<string, int, string>(
                "player.giveitem",
                "Gives the player an item by persistent ID, quantity, and rarity.",
                GiveItem,
                "itemId",
                "quantity",
                "rarity");
        }

        public void Dispose()
        {
            DebugLogConsole.RemoveCommand<string, int, string>(GiveItem);
        }

        private void GiveItem(string itemId, int quantity, string rarityName)
        {
            string id = itemId?.Trim();
            if (string.IsNullOrEmpty(id) || quantity <= 0)
            {
                Debug.LogWarning("Usage: player.giveitem <itemId> <quantity> <rarity>. Quantity must be positive.");
                return;
            }
            if (!_catalog.TryGet(id, out ItemData item))
            {
                Debug.LogWarning($"Unknown item ID '{id}'.");
                return;
            }
            if (!Enum.TryParse(rarityName, true, out ItemData.Rarity rarity) ||
                !Enum.IsDefined(typeof(ItemData.Rarity), rarity))
            {
                Debug.LogWarning(
                    $"Unknown rarity '{rarityName}'. Valid values: {string.Join(", ", Enum.GetNames(typeof(ItemData.Rarity)))}.");
                return;
            }

            _inventory.TryAdd(item, quantity, out int remainder, rarity);
            int added = quantity - remainder;
            if (added == 0)
                Debug.LogWarning("The player inventory is full; no items were added.");
            else if (remainder > 0)
                Debug.LogWarning($"Gave {added}x '{id}' ({rarity}); {remainder} did not fit.");
            else
                Debug.Log($"Gave {added}x '{id}' ({rarity}).");
        }
    }
}
