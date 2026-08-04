using UnityEngine;
using System;
using System.Collections.Generic;
using Project.Scripts.Interface;

namespace Project.Scripts.DataTypes
{
    /// <summary>
    /// Item definition. The action selects behavior; action data configures that
    /// behavior for this item.
    /// </summary>
    [CreateAssetMenu(fileName = "New Item Data", menuName = "Data/Item Data", order = 0)]
    public class ItemData : ScriptableObject, IToolTipData
    {
        public const int FuelUnitsPerBaseValue = 4;
        public const string GoldValueTagId = "gold-value";
        public const string MagicValueTagId = "magic-value";
        public const string FuelValueTagId = "fuel-value";

        [Tooltip("Stable identifier used in save data. Do not change after the item ships.")]
        public string persistentId;

        [TextArea(2,4)]
        public string description;
        public int maxStack;
        
        public Sprite sprite;

        [Tooltip("Optional projectile supplied when this item is used as skill ammunition.")]
        public ProjectileData projectile;

        public string DisplayName => name;
        public string Description => description;
        public Color Color => Color.white;
        public int Count => 0;
        public Sprite Sprite => sprite;

        [Tooltip("Optional action performed when this item is used from the HotBar.")]
        public ItemAction action;

        [SerializeReference]
        [ManagedReferenceSelector(typeof(ItemActionData))]
        [Tooltip("Only add the data records required by this item's action and tool actions.")]
        private ItemActionData[] actionData = Array.Empty<ItemActionData>();

        /// <summary>All action-specific records stored by this item.</summary>
        public IReadOnlyList<ItemActionData> ActionData =>
            actionData ?? Array.Empty<ItemActionData>();
        
        [HideInInspector] public int goldValue;
        [HideInInspector, Min(1)] public int fuelValue = 1;
        [Min(0f)]
        [HideInInspector]
        [Tooltip("Base mana granted when a common item is offered to a Town Core shrine.")]
        public float magicValue;

        [SerializeField]
        private EntityTag[] tags = System.Array.Empty<EntityTag>();

        [SerializeField]
        private ValueTagAssignment[] valueTags =
            Array.Empty<ValueTagAssignment>();

        public bool HasTag(EntityTag tag)
        {
            if (tag == null)
                return false;

            for (int i = 0; i < (tags?.Length ?? 0); i++)
            {
                if (tags[i] == tag)
                    return true;
            }

            return ValueTagLookup.HasTag(valueTags, tag);
        }

        public bool TryGetValue(ValueTag tag, out int value) =>
            ValueTagLookup.TryGetInt(valueTags, tag, out value);

        public bool TryGetValue(ValueTag tag, out float value) =>
            ValueTagLookup.TryGetFloat(valueTags, tag, out value);

        public int GetGoldValue() =>
            ValueTagLookup.TryGetInt(valueTags, GoldValueTagId, out int value)
                ? value
                : goldValue;

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

            int value = ValueTagLookup.TryGetInt(
                valueTags, FuelValueTagId, out int taggedValue)
                    ? taggedValue
                    : fuelValue;
            return checked((long)value * rarityMultiplier);
        }

        public float GetMagicValue(Rarity rarity)
        {
            float rarityMultiplier = rarity switch
            {
                Rarity.Common => 1f,
                Rarity.Uncommon => 1.25f,
                Rarity.Rare => 1.5f,
                Rarity.Mythic => 2f,
                Rarity.Legendary => 3f,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(rarity), rarity, "Unknown item rarity.")
            };

            float value = ValueTagLookup.TryGetFloat(
                valueTags, MagicValueTagId, out float taggedValue)
                    ? taggedValue
                    : magicValue;
            return value * rarityMultiplier;
        }

        private void OnEnable()
        {
            actionData ??= Array.Empty<ItemActionData>();
            valueTags ??= Array.Empty<ValueTagAssignment>();
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
            magicValue = Mathf.Max(0f, magicValue);
            actionData ??= Array.Empty<ItemActionData>();
            valueTags ??= Array.Empty<ValueTagAssignment>();
        }
#endif
    }
}
