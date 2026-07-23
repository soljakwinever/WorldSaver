using System.IO;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public class PersistentProduction : MonoBehaviour, IPersistentComponent, IOfflineSimulatable
    {
        public const ushort TypeId = 6;
        
        [SerializeField] private long ticksPerCycle = 600;
        
        private long _lastProductionTick;
        private int _storedOutput;
        
        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => 1;

        public void SimulateOffline(long fromTick, long toTick, OfflineSimulationPolicy policy)
        {
            if(toTick <= _lastProductionTick)
                return;

            long elapsed = toTick - _lastProductionTick;
            long cycles = elapsed / ticksPerCycle;

            if (cycles <= 0)
                return;

            _storedOutput += checked((int)cycles);
            _lastProductionTick += cycles * ticksPerCycle;
        }

        public void WriteState(BinaryWriter writer)
        {
            writer.Write(_lastProductionTick);
            writer.Write(_storedOutput);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            _lastProductionTick = reader.ReadInt64();
            _storedOutput = reader.ReadInt32();
        }
        
        public bool IsAtBaseline()
        {
            return _lastProductionTick == 0 && _storedOutput == 0;
        }
    }
}