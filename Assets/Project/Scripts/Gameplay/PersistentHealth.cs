using System.IO;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;

namespace Project.Scripts.Core
{
    public class PersistentHealth : IHasHealth, IPersistentComponent
    {
        private int _health;
        private const ushort _persistentTypeId = 1;

        public int Health => _health;
        public int MaxHealth => 100;

        public void TakeDamage(int damage)
        {
            _health = Mathf.Max(0, _health - damage);
        }

        public void Heal(int amount)
        {
            throw new System.NotImplementedException();
        }

        public ushort PersistentTypeId => _persistentTypeId;

        public ushort PersistentVersion => 1;

        public void WriteState(BinaryWriter writer)
        {
            writer.Write(_health);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            _health = reader.ReadInt32();
        }

        public bool IsAtBaseline()
        {
            return _health == MaxHealth;
        }
    }
}