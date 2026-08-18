#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
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
    }
}
#endif
