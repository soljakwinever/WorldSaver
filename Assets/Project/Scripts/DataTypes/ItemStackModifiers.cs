using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Flags]
    public enum ItemStackFlags : byte
    {
        None = 0,
        HasModifiers = 1 << 0,
        HasUniqueName = 1 << 1,
        Broken = 1 << 2,
        HasPartialDurability = 1 << 3,
        ReservedMask = 0xf0
    }

    public enum ItemModifierType : byte
    {
        Stat, Damage, UseSpeed, HitboxSize, ElementalDamage,
        MovementSpeed, Defense, ElementalResistance,
        ProjectileOnUse, SkillOverride
    }

    public enum ModifierValueMode : byte { Flat, Percent }

    [Serializable]
    public readonly struct ItemModifier : IEquatable<ItemModifier>
    {
        public ItemModifierType Type { get; }
        public ModifierValueMode Mode { get; }
        public EquipmentStat Stat { get; }
        public SkillElement Element { get; }
        public float Amount { get; }
        public string ReferenceId { get; }

        public ItemModifier(ItemModifierType type, float amount,
            ModifierValueMode mode = ModifierValueMode.Flat,
            EquipmentStat stat = default, SkillElement element = default,
            string referenceId = null)
        {
            Type = type; Amount = amount; Mode = mode; Stat = stat;
            Element = element; ReferenceId = referenceId ?? string.Empty;
        }

        public bool Equals(ItemModifier other) => Type == other.Type &&
            Mode == other.Mode && Stat == other.Stat && Element == other.Element &&
            Amount.Equals(other.Amount) && ReferenceId == other.ReferenceId;
        public override bool Equals(object obj) => obj is ItemModifier value && Equals(value);
        public override int GetHashCode() => HashCode.Combine(Type, Mode, Stat, Element, Amount, ReferenceId);
    }

    public sealed class GeneratedItemData : IEquatable<GeneratedItemData>
    {
        private readonly ItemModifier[] _modifiers;
        public ulong Seed { get; }
        public string UniqueName { get; }
        public IReadOnlyList<ItemModifier> Modifiers => _modifiers;
        public bool HasUniqueName => !string.IsNullOrWhiteSpace(UniqueName);

        public GeneratedItemData(ulong seed, IReadOnlyList<ItemModifier> modifiers,
            string uniqueName = null)
        {
            if (modifiers == null || modifiers.Count == 0 || modifiers.Count > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(modifiers));
            Seed = seed; UniqueName = uniqueName ?? string.Empty;
            _modifiers = new ItemModifier[modifiers.Count];
            for (int i = 0; i < modifiers.Count; i++) _modifiers[i] = modifiers[i];
        }

        public bool Equals(GeneratedItemData other)
        {
            if (ReferenceEquals(other, null) || Seed != other.Seed || UniqueName != other.UniqueName ||
                _modifiers.Length != other._modifiers.Length) return false;
            for (int i = 0; i < _modifiers.Length; i++) if (!_modifiers[i].Equals(other._modifiers[i])) return false;
            return true;
        }
        public override bool Equals(object obj) => Equals(obj as GeneratedItemData);
        public override int GetHashCode() => HashCode.Combine(Seed, UniqueName);
    }

    public static class ItemStackDataCodec
    {
        public static ItemStackFlags GetFlags(byte durability, GeneratedItemData generated)
        {
            ItemStackFlags flags = generated != null ? ItemStackFlags.HasModifiers : 0;
            if (generated?.HasUniqueName == true) flags |= ItemStackFlags.HasUniqueName;
            if (durability == 0) flags |= ItemStackFlags.Broken;
            else if (durability < byte.MaxValue) flags |= ItemStackFlags.HasPartialDurability;
            return flags;
        }

        public static void Write(BinaryWriter writer, byte durability, GeneratedItemData generated)
        {
            ItemStackFlags flags = GetFlags(durability, generated);
            writer.Write((byte)flags);
            if ((flags & ItemStackFlags.HasModifiers) != 0)
            {
                writer.Write(generated.Seed); writer.Write((byte)generated.Modifiers.Count);
                foreach (ItemModifier modifier in generated.Modifiers)
                {
                    writer.Write((byte)modifier.Type); writer.Write((byte)modifier.Mode);
                    writer.Write((byte)modifier.Stat); writer.Write((byte)modifier.Element);
                    writer.Write(modifier.Amount); writer.Write(modifier.ReferenceId ?? string.Empty);
                }
            }
            if ((flags & ItemStackFlags.HasUniqueName) != 0) writer.Write(generated.UniqueName);
            if ((flags & ItemStackFlags.HasPartialDurability) != 0) writer.Write(durability);
        }

        public static GeneratedItemData Read(BinaryReader reader, out byte durability)
        {
            ItemStackFlags flags = (ItemStackFlags)reader.ReadByte();
            if ((flags & ItemStackFlags.ReservedMask) != 0 ||
                (flags & (ItemStackFlags.Broken | ItemStackFlags.HasPartialDurability)) ==
                (ItemStackFlags.Broken | ItemStackFlags.HasPartialDurability))
                throw new InvalidDataException($"Invalid item stack flags 0x{(byte)flags:x2}.");
            List<ItemModifier> modifiers = null; ulong seed = 0;
            if ((flags & ItemStackFlags.HasModifiers) != 0)
            {
                seed = reader.ReadUInt64(); int count = reader.ReadByte();
                if (count == 0) throw new InvalidDataException("A modified stack has no modifiers.");
                modifiers = new List<ItemModifier>(count);
                for (int i = 0; i < count; i++)
                {
                    var type = (ItemModifierType)reader.ReadByte(); var mode = (ModifierValueMode)reader.ReadByte();
                    var stat = (EquipmentStat)reader.ReadByte(); var element = (SkillElement)reader.ReadByte();
                    float amount = reader.ReadSingle(); string id = reader.ReadString();
                    if (!Enum.IsDefined(typeof(ItemModifierType), type) || !Enum.IsDefined(typeof(ModifierValueMode), mode) ||
                        !Enum.IsDefined(typeof(EquipmentStat), stat) || !Enum.IsDefined(typeof(SkillElement), element) ||
                        float.IsNaN(amount) || float.IsInfinity(amount)) throw new InvalidDataException("Invalid item modifier.");
                    modifiers.Add(new ItemModifier(type, amount, mode, stat, element, id));
                }
            }
            string name = (flags & ItemStackFlags.HasUniqueName) != 0 ? reader.ReadString() : string.Empty;
            if (!string.IsNullOrEmpty(name) && modifiers == null) throw new InvalidDataException("An unmodified stack cannot have a generated name.");
            durability = (flags & ItemStackFlags.Broken) != 0 ? (byte)0 :
                (flags & ItemStackFlags.HasPartialDurability) != 0 ? reader.ReadByte() : byte.MaxValue;
            if ((flags & ItemStackFlags.HasPartialDurability) != 0 &&
                (durability == 0 || durability == byte.MaxValue))
                throw new InvalidDataException("Invalid partial durability.");
            return modifiers != null ? new GeneratedItemData(seed, modifiers, name) : null;
        }
    }
}
