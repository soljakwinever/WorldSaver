using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Serializable]
    public sealed class ItemModifierDefinition
    {
        [Min(0f)] public float weight = 1f;
        public ItemModifierType type = ItemModifierType.Stat;
        public ModifierValueMode mode = ModifierValueMode.Flat;
        public EquipmentStat stat;
        public SkillElement element;
        [Tooltip("Stable catalog ID used by projectile and skill modifiers.")]
        public string referenceId;
        [Min(0f)] public float minimumMagnitude = 1f;
        [Min(0f)] public float maximumMagnitude = 1f;
        public bool allowNegative = true;
        public bool allowDuplicates;

        [Header("Generated Name")]
        public string prefix;
        public int prefixPriority;
        public string suffix;
        public int suffixPriority;
        [Tooltip("Replaces the base item's name when this contribution wins.")]
        public string replacementName;
        public int replacementNamePriority;

        public bool IsValid => weight > 0f && !float.IsNaN(weight) &&
            !float.IsInfinity(weight) && maximumMagnitude >= minimumMagnitude &&
            !float.IsNaN(minimumMagnitude) && !float.IsNaN(maximumMagnitude) &&
            !float.IsInfinity(minimumMagnitude) && !float.IsInfinity(maximumMagnitude);
    }

    [CreateAssetMenu(fileName = "ModifiersData", menuName = "Data/Modifiers Data")]
    public sealed class ModifiersData : ScriptableObject
    {
        [SerializeField] private ItemModifierDefinition[] weaponModifiers = Array.Empty<ItemModifierDefinition>();
        [SerializeField] private ItemModifierDefinition[] equipmentModifiers = Array.Empty<ItemModifierDefinition>();
        [SerializeField] private ItemModifierDefinition[] accessoryModifiers = Array.Empty<ItemModifierDefinition>();

        public IReadOnlyList<ItemModifierDefinition> WeaponModifiers => weaponModifiers ?? Array.Empty<ItemModifierDefinition>();
        public IReadOnlyList<ItemModifierDefinition> EquipmentModifiers => equipmentModifiers ?? Array.Empty<ItemModifierDefinition>();
        public IReadOnlyList<ItemModifierDefinition> AccessoryModifiers => accessoryModifiers ?? Array.Empty<ItemModifierDefinition>();

#if UNITY_EDITOR
        private void OnValidate()
        {
            weaponModifiers ??= Array.Empty<ItemModifierDefinition>();
            equipmentModifiers ??= Array.Empty<ItemModifierDefinition>();
            accessoryModifiers ??= Array.Empty<ItemModifierDefinition>();
            Normalize(weaponModifiers); Normalize(equipmentModifiers); Normalize(accessoryModifiers);
        }

        private static void Normalize(ItemModifierDefinition[] definitions)
        {
            foreach (ItemModifierDefinition definition in definitions)
            {
                if (definition == null) continue;
                definition.weight = Mathf.Max(0f, definition.weight);
                definition.minimumMagnitude = Mathf.Max(0f, definition.minimumMagnitude);
                definition.maximumMagnitude = Mathf.Max(definition.minimumMagnitude, definition.maximumMagnitude);
            }
        }
#endif
    }
}
