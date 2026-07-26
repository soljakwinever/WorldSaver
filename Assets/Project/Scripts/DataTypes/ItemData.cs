using UnityEngine;
using System;
using System.Collections.Generic;

namespace Project.Scripts.DataTypes
{
    /// <summary>
    /// Item definition. The action selects behavior; action data configures that
    /// behavior for this item.
    /// </summary>
    [CreateAssetMenu(fileName = "New Item Data", menuName = "Data/Item Data", order = 0)]
    public class ItemData : ScriptableObject
    {
        public const int FuelUnitsPerBaseValue = 4;

        [Tooltip("Stable identifier used in save data. Do not change after the item ships.")]
        public string persistentId;

        [TextArea(2,4)]
        public string description;
        public int maxStack;
        
        public Sprite sprite;

        [Tooltip("Optional action performed when this item is used from the HotBar.")]
        public ItemAction action;

        [SerializeReference]
        [ManagedReferenceSelector(typeof(ItemActionData))]
        [Tooltip("Only add the data records required by this item's action and tool actions.")]
        private ItemActionData[] actionData = Array.Empty<ItemActionData>();

        /// <summary>All action-specific records stored by this item.</summary>
        public IReadOnlyList<ItemActionData> ActionData =>
            actionData ?? Array.Empty<ItemActionData>();
        
        public int goldValue;
        [Min(1)] public int fuelValue = 1;

        [SerializeField]
        private EntityTag[] tags = System.Array.Empty<EntityTag>();

        public bool HasTag(EntityTag tag)
        {
            if (tag == null)
                return false;

            for (int i = 0; i < (tags?.Length ?? 0); i++)
            {
                if (tags[i] == tag)
                    return true;
            }

            return false;
        }

        /// <summary>Finds the first action-data record of the requested type.</summary>
        public bool TryGetActionData<T>(out T data)
            where T : ItemActionData
        {
            foreach (ItemActionData candidate in ActionData)
            {
                if (candidate is T match)
                {
                    data = match;
                    return true;
                }
            }

            data = null;
            return false;
        }

        public long GetFuelUnits(Rarity rarity)
        {
            int rarityMultiplier = rarity switch
            {
                Rarity.Common => 4,
                Rarity.Uncommon => 5,
                Rarity.Rare => 6,
                Rarity.Mythic => 8,
                Rarity.Legendary => 12,
                _ => throw new System.ArgumentOutOfRangeException(
                    nameof(rarity), rarity, "Unknown item rarity.")
            };

            return checked((long)fuelValue * rarityMultiplier);
        }

        private void OnEnable()
        {
            actionData ??= Array.Empty<ItemActionData>();
        }
        
        public enum Rarity
        {
            Common,
            Uncommon,
            Rare,
            Mythic,
            Legendary,
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            fuelValue = Mathf.Max(1, fuelValue);
            actionData ??= Array.Empty<ItemActionData>();
        }
#endif
    }
}
