using System;
using System.Collections;
using UnityEngine;

namespace Project.Scripts.AI.Decorators
{
    public enum BlackboardCondition
    {
        Exists,
        Missing,
        HasValue,
        NoValue,
        BoolIsTrue,
        CompareBlackboardValue,
        FloatEquals,
        FloatNotEquals,
        FloatGreaterThan,
        FloatGreaterThanOrEqual,
        FloatLessThan,
        FloatLessThanOrEqual,
        IntegerEquals,
        IntegerNotEquals,
        IntegerGreaterThan,
        IntegerGreaterThanOrEqual,
        IntegerLessThan,
        IntegerLessThanOrEqual,
        EnumEquals,
        ObjectIsValid,
        TransformIsActive,
        ListIsEmpty,
        ListContainsObject
    }

    internal static class BlackboardConditionEvaluator
    {
        public static bool Evaluate(
            Blackboard blackboard,
            AiKeys.Key key,
            BlackboardCondition condition,
            AiKeys.Key comparisonKey = default,
            bool boolValue = true,
            float floatValue = 0f,
            int integerValue = 0,
            string enumValue = null,
            UnityEngine.Object objectValue = null)
        {
            if (blackboard == null)
                return false;

            bool exists = blackboard.TryGetValue(
                AiKeys.Resolve(key),
                out object value);
            bool hasValue = exists && IsValidValue(value);

            return condition switch
            {
                BlackboardCondition.Exists => exists,
                BlackboardCondition.Missing => !exists,
                BlackboardCondition.HasValue => hasValue,
                BlackboardCondition.NoValue => !hasValue,
                BlackboardCondition.BoolIsTrue =>
                    value is bool boolean && boolean == boolValue,
                BlackboardCondition.CompareBlackboardValue =>
                    blackboard.TryGetValue(
                        AiKeys.Resolve(comparisonKey),
                        out object comparisonValue) &&
                    ValuesEqual(value, comparisonValue),
                BlackboardCondition.FloatEquals =>
                    TryGetNumber(value, out double number) &&
                    Math.Abs(number - floatValue) <= Mathf.Epsilon,
                BlackboardCondition.FloatNotEquals =>
                    TryGetNumber(value, out double number) &&
                    Math.Abs(number - floatValue) > Mathf.Epsilon,
                BlackboardCondition.FloatGreaterThan =>
                    TryGetNumber(value, out double number) &&
                    number > floatValue,
                BlackboardCondition.FloatGreaterThanOrEqual =>
                    TryGetNumber(value, out double number) &&
                    number >= floatValue,
                BlackboardCondition.FloatLessThan =>
                    TryGetNumber(value, out double number) &&
                    number < floatValue,
                BlackboardCondition.FloatLessThanOrEqual =>
                    TryGetNumber(value, out double number) &&
                    number <= floatValue,
                BlackboardCondition.IntegerEquals =>
                    TryGetInteger(value, out long integer) &&
                    integer == integerValue,
                BlackboardCondition.IntegerNotEquals =>
                    TryGetInteger(value, out long integer) &&
                    integer != integerValue,
                BlackboardCondition.IntegerGreaterThan =>
                    TryGetInteger(value, out long integer) &&
                    integer > integerValue,
                BlackboardCondition.IntegerGreaterThanOrEqual =>
                    TryGetInteger(value, out long integer) &&
                    integer >= integerValue,
                BlackboardCondition.IntegerLessThan =>
                    TryGetInteger(value, out long integer) &&
                    integer < integerValue,
                BlackboardCondition.IntegerLessThanOrEqual =>
                    TryGetInteger(value, out long integer) &&
                    integer <= integerValue,
                BlackboardCondition.EnumEquals =>
                    value is Enum enumObject &&
                    string.Equals(
                        enumObject.ToString(),
                        enumValue,
                        StringComparison.OrdinalIgnoreCase),
                BlackboardCondition.ObjectIsValid =>
                    value is UnityEngine.Object unityObject && unityObject != null,
                BlackboardCondition.TransformIsActive =>
                    TryGetTransform(value, out Transform transform) &&
                    transform.gameObject.activeInHierarchy,
                BlackboardCondition.ListIsEmpty =>
                    value is IList list && list.Count == 0,
                BlackboardCondition.ListContainsObject =>
                    value is IList list && ContainsObject(list, objectValue),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(condition),
                    condition,
                    null)
            };
        }

        private static bool IsValidValue(object value)
        {
            return value != null &&
                   (!(value is UnityEngine.Object unityObject) ||
                    unityObject != null);
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (left is UnityEngine.Object leftObject)
                return right is UnityEngine.Object rightObject &&
                       leftObject == rightObject;

            return Equals(left, right);
        }

        private static bool TryGetNumber(object value, out double number)
        {
            if (value is Enum || value == null)
            {
                number = default;
                return false;
            }

            try
            {
                number = Convert.ToDouble(value);
                return value is byte or sbyte or short or ushort or int or
                    uint or long or ulong or float or double or decimal;
            }
            catch (Exception)
            {
                number = default;
                return false;
            }
        }

        private static bool TryGetInteger(object value, out long integer)
        {
            if (value is Enum || value == null)
            {
                integer = default;
                return false;
            }

            try
            {
                integer = Convert.ToInt64(value);
                return value is byte or sbyte or short or ushort or int or
                    uint or long or ulong;
            }
            catch (Exception)
            {
                integer = default;
                return false;
            }
        }

        private static bool TryGetTransform(
            object value,
            out Transform transform)
        {
            switch (value)
            {
                case Transform directTransform:
                    transform = directTransform;
                    return transform != null;
                case GameObject gameObject:
                    transform = gameObject != null
                        ? gameObject.transform
                        : null;
                    return transform != null;
                case Component component:
                    transform = component != null
                        ? component.transform
                        : null;
                    return transform != null;
                default:
                    transform = null;
                    return false;
            }
        }

        private static bool ContainsObject(
            IList list,
            UnityEngine.Object expected)
        {
            foreach (object item in list)
            {
                if (item is UnityEngine.Object unityObject &&
                    unityObject == expected)
                    return true;
            }

            return false;
        }
    }
}
