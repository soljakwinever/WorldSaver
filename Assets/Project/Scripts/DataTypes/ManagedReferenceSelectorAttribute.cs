using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    /// <summary>
    /// Adds a concrete-type picker to a SerializeReference collection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ManagedReferenceSelectorAttribute : PropertyAttribute
    {
        public Type BaseType { get; }

        public ManagedReferenceSelectorAttribute(Type baseType)
        {
            BaseType = baseType;
        }
    }
}
