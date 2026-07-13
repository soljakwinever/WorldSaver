using System.IO;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public class PersistentTransform : MonoBehaviour, IPersistentComponent
    {
        public const ushort TypeId = 5;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => 1;

        [SerializeField] private Transform target;
        
        public void WriteState(BinaryWriter writer)
        {
            Vector3 position = target.position;
            float rotation = target.rotation.eulerAngles.z;
            
            writer.Write(position.x);
            writer.Write(position.y);
            writer.Write(position.z);
            
            writer.Write(rotation);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            target.localPosition = new Vector3(
                reader.ReadSingle(), 
                reader.ReadSingle(), 
                reader.ReadSingle());
            
            target.localRotation = Quaternion.Euler(0, 0, reader.ReadSingle());
        }

        public bool IsAtBaseline()
        {
            return false;
        }
    }
}