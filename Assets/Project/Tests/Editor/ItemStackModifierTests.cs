#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;

namespace Project.Tests.EditMode
{
    public sealed class ItemStackModifierTests
    {
        [TestCase(0, ItemStackFlags.Broken)]
        [TestCase(77, ItemStackFlags.HasPartialDurability)]
        [TestCase(255, ItemStackFlags.None)]
        public void DurabilityUsesExpectedFlags(int durability, ItemStackFlags expected)
        {
            Assert.That(ItemStackDataCodec.GetFlags((byte)durability, null), Is.EqualTo(expected));
        }

        [Test]
        public void GeneratedPayloadRoundTrips()
        {
            GeneratedItemData source = new(42, new List<ItemModifier>
            {
                new(ItemModifierType.Stat, 3, stat: EquipmentStat.Strength),
                new(ItemModifierType.ElementalDamage, 7, element: SkillElement.Fire)
            }, "Tempered Sword");
            using MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
                ItemStackDataCodec.Write(writer, 123, source);
            stream.Position = 0;
            using BinaryReader reader = new(stream);
            GeneratedItemData restored = ItemStackDataCodec.Read(reader, out byte durability);
            Assert.That(durability, Is.EqualTo(123));
            Assert.That(restored, Is.EqualTo(source));
        }

        [Test]
        public void GeneratedStacksDoNotMerge()
        {
            EquipableItemData item = ScriptableObject.CreateInstance<EquipableItemData>();
            try
            {
                item.maxStack = 1;
                Inventory inventory = new(2);
                var modifiers = new[] { new ItemModifier(ItemModifierType.Stat, 1, stat: EquipmentStat.Luck) };
                Assert.That(inventory.TryAdd(new ItemStack(item, 1, generatedData: new GeneratedItemData(1, modifiers)), out _), Is.True);
                Assert.That(inventory.TryAdd(new ItemStack(item, 1, generatedData: new GeneratedItemData(2, modifiers)), out _), Is.True);
                Assert.That(inventory.OccupiedSlots, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(item); }
        }

        [Test]
        public void GeneratedNameUsesStrictHighestPriorities()
        {
            ItemModifierDefinition first = new()
            {
                prefix = "Fine", prefixPriority = 2,
                suffix = "of Sparks", suffixPriority = 1,
                replacementName = "Brand", replacementNamePriority = 4
            };
            ItemModifierDefinition equal = new()
            {
                prefix = "Equal", prefixPriority = 2,
                replacementName = "Equal Brand", replacementNamePriority = 4
            };
            ItemModifierDefinition higher = new()
            {
                prefix = "Exalted", prefixPriority = 3,
                suffix = "of Storms", suffixPriority = 5
            };

            Assert.That(GeneratedEquipmentFactory.BuildName("Sword",
                new[] { first, equal, higher }),
                Is.EqualTo("Exalted Brand of Storms"));
        }

        [Test]
        public void ItemSpecificModifierIsUsedWhenGlobalPoolIsEmpty()
        {
            EquipableItemData item = ScriptableObject.CreateInstance<EquipableItemData>();
            ModifiersData data = ScriptableObject.CreateInstance<ModifiersData>();
            try
            {
                item.name = "Sword"; item.maxStack = 1;
                ItemModifierDefinition definition = new()
                {
                    type = ItemModifierType.Damage,
                    minimumMagnitude = 2,
                    maximumMagnitude = 2,
                    allowNegative = false,
                    prefix = "Keen",
                    prefixPriority = 1
                };
                typeof(EquipableItemData).GetField("generatedModifiers",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(
                    item, new[] { definition });
                GeneratedEquipmentFactory factory = new(data, new System.Random(17));

                ItemStack stack = factory.Create(item, ItemData.Rarity.Rare, 1f);

                Assert.That(stack.GeneratedData, Is.Not.Null);
                Assert.That(stack.DisplayName, Is.EqualTo("Keen Sword"));
                Assert.That(stack.GeneratedData.Modifiers[0].Type,
                    Is.EqualTo(ItemModifierType.Damage));
            }
            finally
            {
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(data);
            }
        }
    }
}
#endif
