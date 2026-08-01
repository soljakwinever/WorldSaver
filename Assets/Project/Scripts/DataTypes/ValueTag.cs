using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "New Value Tag", menuName = "Data/Value Tag")]
    public sealed class ValueTag : EntityTag
    {
        public enum NumberType : byte
        {
            Integer,
            Float,
        }

        [SerializeField]
        [Tooltip("Stable identifier used by systems that share this value. Do not change after shipping.")]
        private string persistentId;

        [SerializeField] private NumberType valueType;

        public string PersistentId => persistentId;
        public NumberType ValueType => valueType;

        public void Configure(string id, NumberType type)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A value tag requires a persistent ID.", nameof(id));
            persistentId = id.Trim();
            valueType = type;
        }
    }
}
