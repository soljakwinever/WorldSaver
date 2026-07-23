using System.IO;

namespace Project.Scripts.Interface
{
    public interface IPersistentRegionComponent
    {
        ushort PersistentTypeId { get; }
        ushort PersistentVersion { get; }
        
        void WriteState(BinaryWriter writer);
        void ReadState(BinaryReader reader, ushort savedVersion);
        
        bool IsAtBaseline();
    }
}