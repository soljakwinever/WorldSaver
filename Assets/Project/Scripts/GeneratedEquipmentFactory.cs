using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public sealed class GeneratedEquipmentFactory :
        IFactory<ItemData, ItemData.Rarity, float, ItemStack>
    {
        public const string RandomId = "EquipmentModifiers";

        private readonly ModifiersData _data;
        private readonly System.Random _source;

        public GeneratedEquipmentFactory(
            ModifiersData data,
            [Inject(Id = RandomId)] System.Random source)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public ItemStack Create(ItemData item, ItemData.Rarity rarity,
            float uniqueNameChance)
        {
            if (item is not EquipableItemData equipment)
                return new ItemStack(item, 1, rarity);

            List<ItemModifierDefinition> candidates = BuildCandidates(equipment);
            if (candidates.Count == 0)
                return new ItemStack(item, 1, rarity);

            ulong seed = ((ulong)(uint)_source.Next() << 32) | (uint)_source.Next();
            System.Random random = new(unchecked((int)(seed ^ (seed >> 32))));
            int minimum = rarity >= ItemData.Rarity.Mythic ? 2 : 1;
            int maximum = rarity switch
            {
                ItemData.Rarity.Common => 2,
                ItemData.Rarity.Uncommon => 3,
                ItemData.Rarity.Rare => 3,
                ItemData.Rarity.Mythic => 4,
                _ => 5
            };
            int requested = random.Next(minimum, maximum + 1);
            List<ItemModifier> modifiers = new(requested);
            List<ItemModifierDefinition> selected = new(requested);
            HashSet<ItemModifierDefinition> unavailable = new();

            for (int index = 0; index < requested; index++)
            {
                ItemModifierDefinition definition = Select(candidates, unavailable, random);
                if (definition == null) break;
                selected.Add(definition);
                if (!definition.allowDuplicates) unavailable.Add(definition);

                float amount = Lerp(definition.minimumMagnitude,
                    definition.maximumMagnitude, random.NextDouble());
                amount *= GetRarityMagnitude(rarity, index);
                if (ShouldBeNegative(rarity, definition, random)) amount = -amount;
                modifiers.Add(new ItemModifier(definition.type, amount,
                    definition.mode, definition.stat, definition.element,
                    definition.referenceId));
            }

            if (modifiers.Count == 0)
                return new ItemStack(item, 1, rarity);

            string uniqueName = random.NextDouble() < Math.Max(0f, Math.Min(1f, uniqueNameChance))
                ? BuildName(item.name, selected)
                : null;
            if (uniqueName == item.name) uniqueName = null;
            return new ItemStack(item, 1, rarity, generatedData:
                new GeneratedItemData(seed, modifiers, uniqueName));
        }

        private List<ItemModifierDefinition> BuildCandidates(EquipableItemData item)
        {
            IReadOnlyList<ItemModifierDefinition> global = item.EquipmentSlot switch
            {
                EquipmentSlot.MainHand or EquipmentSlot.OffHand => _data.WeaponModifiers,
                EquipmentSlot.Accessory => _data.AccessoryModifiers,
                _ => _data.EquipmentModifiers
            };
            List<ItemModifierDefinition> result = new(global.Count + item.GeneratedModifiers.Count);
            AddValid(global, result); AddValid(item.GeneratedModifiers, result);
            return result;
        }

        private static void AddValid(IReadOnlyList<ItemModifierDefinition> source,
            List<ItemModifierDefinition> destination)
        {
            for (int i = 0; i < source.Count; i++)
                if (source[i]?.IsValid == true) destination.Add(source[i]);
        }

        private static ItemModifierDefinition Select(
            List<ItemModifierDefinition> candidates,
            HashSet<ItemModifierDefinition> unavailable,
            System.Random random)
        {
            double total = 0;
            foreach (ItemModifierDefinition value in candidates)
                if (!unavailable.Contains(value)) total += value.weight;
            if (total <= 0) return null;
            double target = random.NextDouble() * total;
            ItemModifierDefinition last = null;
            foreach (ItemModifierDefinition value in candidates)
            {
                if (unavailable.Contains(value)) continue;
                last = value; target -= value.weight;
                if (target < 0) return value;
            }
            return last;
        }

        private static bool ShouldBeNegative(ItemData.Rarity rarity,
            ItemModifierDefinition definition, System.Random random)
        {
            if (!definition.allowNegative) return false;
            return rarity <= ItemData.Rarity.Uncommon
                ? random.Next(2) == 0
                : rarity == ItemData.Rarity.Rare && random.NextDouble() < .1;
        }

        private static float GetRarityMagnitude(ItemData.Rarity rarity, int index) => rarity switch
        {
            ItemData.Rarity.Common => 1f,
            ItemData.Rarity.Uncommon => 2f,
            ItemData.Rarity.Rare => 3f,
            ItemData.Rarity.Mythic => index == 0 ? 5f : 3f,
            _ => index == 0 ? 8f : 2f
        };

        private static float Lerp(float minimum, float maximum, double value) =>
            minimum + (maximum - minimum) * (float)value;

        public static string BuildName(string baseName,
            IReadOnlyList<ItemModifierDefinition> definitions)
        {
            string prefix = null, suffix = null, replacement = null;
            int prefixPriority = int.MinValue, suffixPriority = int.MinValue,
                replacementPriority = int.MinValue;
            bool hasPrefix = false, hasSuffix = false, hasReplacement = false;
            foreach (ItemModifierDefinition value in definitions)
            {
                if (!string.IsNullOrWhiteSpace(value.prefix) &&
                    (!hasPrefix || value.prefixPriority > prefixPriority))
                { prefix = value.prefix.Trim(); prefixPriority = value.prefixPriority; hasPrefix = true; }
                if (!string.IsNullOrWhiteSpace(value.suffix) &&
                    (!hasSuffix || value.suffixPriority > suffixPriority))
                { suffix = value.suffix.Trim(); suffixPriority = value.suffixPriority; hasSuffix = true; }
                if (!string.IsNullOrWhiteSpace(value.replacementName) &&
                    (!hasReplacement || value.replacementNamePriority > replacementPriority))
                { replacement = value.replacementName.Trim(); replacementPriority = value.replacementNamePriority; hasReplacement = true; }
            }
            string core = replacement ?? baseName?.Trim() ?? string.Empty;
            List<string> parts = new(3);
            if (!string.IsNullOrEmpty(prefix)) parts.Add(prefix);
            if (!string.IsNullOrEmpty(core)) parts.Add(core);
            if (!string.IsNullOrEmpty(suffix)) parts.Add(suffix);
            return string.Join(" ", parts);
        }
    }
}
