using System.IO;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public class PersistentGrowth : MonoBehaviour, IPersistentComponent
    {
        public const ushort TypeId = 2;

        public ushort PersistentTypeId => TypeId;

        public ushort PersistentVersion => 1;

        private long _growthStartTick;

        private float MatureAfterTicks = 300;
        
        public void BeginGrowth(long currentTick)
        {
            _growthStartTick = currentTick;
        }

        public float GetGrowth(long currentTick)
        {
            long age = currentTick - _growthStartTick;
            return Mathf.Clamp01(age / (float)Mathf.Max(1, MatureAfterTicks));
        }
        
        public void WriteState(BinaryWriter writer)
        {
            writer.Write(_growthStartTick);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            _growthStartTick = reader.ReadInt64();
        }

        public bool IsAtBaseline()
        {
            return _growthStartTick == 0;
        }
    }
}