using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Serializable]
    public sealed class ValueTagAssignment
    {
        [SerializeField] private ValueTag tag;
        [SerializeField] private int intValue;
        [SerializeField] private float floatValue;

        public ValueTag Tag => tag;
        public int IntValue => intValue;
        public float FloatValue => floatValue;

        public ValueTagAssignment(ValueTag tag, int value)
        {
            this.tag = tag;
            intValue = value;
        }

        public ValueTagAssignment(ValueTag tag, float value)
        {
            this.tag = tag;
            floatValue = value;
        }
    }

    public static class ValueTagLookup
    {
        public static bool HasTag(ValueTagAssignment[] assignments, EntityTag tag)
        {
            if (tag == null)
                return false;
            for (int i = 0; i < (assignments?.Length ?? 0); i++)
            {
                if (assignments[i]?.Tag == tag)
                    return true;
            }
            return false;
        }

        public static bool TryGetInt(
            ValueTagAssignment[] assignments,
            ValueTag tag,
            out int value)
        {
            value = default;
            if (tag == null || tag.ValueType != ValueTag.NumberType.Integer)
                return false;
            ValueTagAssignment assignment = Find(assignments, tag);
            if (assignment == null)
                return false;
            value = assignment.IntValue;
            return true;
        }

        public static bool TryGetFloat(
            ValueTagAssignment[] assignments,
            ValueTag tag,
            out float value)
        {
            value = default;
            if (tag == null || tag.ValueType != ValueTag.NumberType.Float)
                return false;
            ValueTagAssignment assignment = Find(assignments, tag);
            if (assignment == null)
                return false;
            value = assignment.FloatValue;
            return true;
        }

        public static bool TryGetInt(
            ValueTagAssignment[] assignments,
            string persistentId,
            out int value)
        {
            value = default;
            ValueTagAssignment assignment = Find(assignments, persistentId);
            if (assignment?.Tag == null ||
                assignment.Tag.ValueType != ValueTag.NumberType.Integer)
                return false;
            value = assignment.IntValue;
            return true;
        }

        public static bool TryGetFloat(
            ValueTagAssignment[] assignments,
            string persistentId,
            out float value)
        {
            value = default;
            ValueTagAssignment assignment = Find(assignments, persistentId);
            if (assignment?.Tag == null ||
                assignment.Tag.ValueType != ValueTag.NumberType.Float)
                return false;
            value = assignment.FloatValue;
            return true;
        }

        private static ValueTagAssignment Find(
            ValueTagAssignment[] assignments,
            ValueTag tag)
        {
            for (int i = 0; i < (assignments?.Length ?? 0); i++)
            {
                if (assignments[i]?.Tag == tag)
                    return assignments[i];
            }
            return null;
        }

        private static ValueTagAssignment Find(
            ValueTagAssignment[] assignments,
            string persistentId)
        {
            if (string.IsNullOrWhiteSpace(persistentId))
                return null;
            for (int i = 0; i < (assignments?.Length ?? 0); i++)
            {
                ValueTagAssignment assignment = assignments[i];
                if (assignment?.Tag != null &&
                    string.Equals(
                        assignment.Tag.PersistentId,
                        persistentId,
                        StringComparison.Ordinal))
                    return assignment;
            }
            return null;
        }
    }
}
