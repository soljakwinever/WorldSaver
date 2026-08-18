using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public enum EquipmentSlot : byte
    {
        Head,
        Chest,
        Legs,
        Feet,
        Hands,
        MainHand,
        OffHand,
        Accessory
    }

    public enum EquipmentStat : byte
    {
        Strength,
        Constitution,
        Dexterity,
        Wisdom,
        Intelligence,
        Luck,
        Defense,
        MeleeAttack,
        RangedAttack,
        MagicAttack,
        MaximumHealth,
        MaximumMana,
        MaximumEnergy,
        FireResistance,
        WaterResistance,
        EarthResistance,
        AirResistance,
        PoisonResistance
    }

    [Serializable]
    public struct EquipmentStatModifier
    {
        public EquipmentStat stat;
        public int amount;
    }

    [CreateAssetMenu(
        fileName = "New Equipable Item Data",
        menuName = "Data/Equipable Item Data",
        order = 1)]
    public sealed class EquipableItemData : ItemData
    {
        [SerializeField] private EquipmentSlot equipmentSlot;
        [SerializeField] private EquipmentStatModifier[] statModifiers =
            Array.Empty<EquipmentStatModifier>();
        [SerializeField, Tooltip("Item-specific generated modifiers added to the matching global modifier pool.")]
        private ItemModifierDefinition[] generatedModifiers =
            Array.Empty<ItemModifierDefinition>();

        public EquipmentSlot EquipmentSlot => equipmentSlot;
        public IReadOnlyList<EquipmentStatModifier> StatModifiers =>
            statModifiers ?? Array.Empty<EquipmentStatModifier>();
        public IReadOnlyList<ItemModifierDefinition> GeneratedModifiers =>
            generatedModifiers ?? Array.Empty<ItemModifierDefinition>();

        public int GetStatModifier(EquipmentStat stat)
        {
            int total = 0;
            EquipmentStatModifier[] modifiers =
                statModifiers ?? Array.Empty<EquipmentStatModifier>();
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i].stat == stat)
                    total = checked(total + modifiers[i].amount);
            }

            return total;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxStack = 1;
            statModifiers ??= Array.Empty<EquipmentStatModifier>();
            generatedModifiers ??= Array.Empty<ItemModifierDefinition>();
        }
#endif
    }
}
