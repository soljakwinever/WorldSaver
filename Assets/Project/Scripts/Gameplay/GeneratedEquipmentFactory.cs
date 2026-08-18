using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;

namespace Project.Scripts.Gameplay
{
    public static class GeneratedEquipmentFactory
    {
        private static readonly EquipmentStat[] Stats =
        {
            EquipmentStat.Strength, EquipmentStat.Constitution, EquipmentStat.Dexterity,
            EquipmentStat.Wisdom, EquipmentStat.Intelligence, EquipmentStat.Luck
        };

        public static ItemStack Create(ItemData item, ItemData.Rarity rarity,
            System.Random source, float uniqueNameChance = .35f)
        {
            if (item is not EquipableItemData || source == null)
                return new ItemStack(item, 1, rarity);
            ulong seed = ((ulong)(uint)source.Next() << 32) | (uint)source.Next();
            Random random = new(unchecked((int)(seed ^ (seed >> 32))));
            int min = rarity >= ItemData.Rarity.Mythic ? 2 : 1;
            int max = rarity switch { ItemData.Rarity.Common => 2, ItemData.Rarity.Uncommon => 3,
                ItemData.Rarity.Rare => 3, ItemData.Rarity.Mythic => 4, _ => 5 };
            int count = random.Next(min, max + 1);
            List<ItemModifier> modifiers = new(count);
            HashSet<EquipmentStat> used = new();
            for (int i = 0; i < count; i++)
            {
                EquipmentStat stat;
                do stat = Stats[random.Next(Stats.Length)]; while (!used.Add(stat) && used.Count < Stats.Length);
                float magnitude = rarity switch { ItemData.Rarity.Common => 1, ItemData.Rarity.Uncommon => 2,
                    ItemData.Rarity.Rare => 3, ItemData.Rarity.Mythic => i == 0 ? 5 : 3,
                    _ => i == 0 ? 8 : 2 };
                bool negative = rarity <= ItemData.Rarity.Uncommon ? random.Next(2) == 0 :
                    rarity == ItemData.Rarity.Rare && random.NextDouble() < .1;
                modifiers.Add(new ItemModifier(ItemModifierType.Stat, negative ? -magnitude : magnitude,
                    stat: stat));
            }
            string uniqueName = random.NextDouble() < uniqueNameChance
                ? $"{(modifiers[0].Amount >= 5 ? "Exalted" : modifiers[0].Amount < 0 ? "Cursed" : "Tempered")} {item.name}"
                : null;
            return new ItemStack(item, 1, rarity, generatedData: new GeneratedItemData(seed, modifiers, uniqueName));
        }
    }
}
