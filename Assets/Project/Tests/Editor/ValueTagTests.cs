using System.Reflection;
using NUnit.Framework;
using Project.Scripts;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Tests.Editor
{
    public sealed class ValueTagTests
    {
        [Test]
        public void ItemDataReadsIntegerAndFloatValueTags()
        {
            ValueTag count = CreateTag("count", ValueTag.NumberType.Integer);
            ValueTag weight = CreateTag("weight", ValueTag.NumberType.Float);
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            SetAssignments(item, new ValueTagAssignment(count, 4),
                new ValueTagAssignment(weight, 2.5f));

            Assert.That(item.HasTag(count), Is.True);
            Assert.That(item.TryGetValue(count, out int intValue), Is.True);
            Assert.That(intValue, Is.EqualTo(4));
            Assert.That(item.TryGetValue(weight, out float floatValue), Is.True);
            Assert.That(floatValue, Is.EqualTo(2.5f));
            Assert.That(item.TryGetValue(count, out float _), Is.False);
        }

        [Test]
        public void NodeAndTileDataExposeAssignedValuesAsTags()
        {
            ValueTag tag = CreateTag("temperature", ValueTag.NumberType.Float);
            NodeData node = ScriptableObject.CreateInstance<NodeData>();
            TileData tile = ScriptableObject.CreateInstance<TileData>();
            SetAssignments(node, new ValueTagAssignment(tag, 12.5f));
            SetAssignments(tile, new ValueTagAssignment(tag, -2f));

            Assert.That(node.HasTag(tag), Is.True);
            Assert.That(node.TryGetValue(tag, out float nodeValue), Is.True);
            Assert.That(nodeValue, Is.EqualTo(12.5f));
            Assert.That(tile.HasTag(tag), Is.True);
            Assert.That(tile.TryGetValue(tag, out float tileValue), Is.True);
            Assert.That(tileValue, Is.EqualTo(-2f));
        }

        [Test]
        public void FuelGetterPrefersMigratedValueTag()
        {
            ValueTag fuelValue = CreateTag(ItemData.FuelValueTagId,
                ValueTag.NumberType.Integer);
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            item.fuelValue = 1;
            SetAssignments(item, new ValueTagAssignment(fuelValue, 4));

            Assert.That(item.GetFuelUnits(ItemData.Rarity.Common), Is.EqualTo(16));
        }

        private static ValueTag CreateTag(string id, ValueTag.NumberType type)
        {
            ValueTag tag = ScriptableObject.CreateInstance<ValueTag>();
            tag.Configure(id, type);
            return tag;
        }

        private static void SetAssignments(Object target,
            params ValueTagAssignment[] assignments)
        {
            FieldInfo field = target.GetType().GetField("valueTags",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, assignments);
        }
    }
}
