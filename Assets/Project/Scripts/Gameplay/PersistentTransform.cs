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
        private Vector3 _baselinePosition;
        private Quaternion _baselineRotation;
        private bool _alwaysPersist;

        public void Initialize(bool alwaysPersist)
        {
            target ??= transform;
            _baselinePosition = target.position;
            _baselineRotation = target.rotation;
            _alwaysPersist = alwaysPersist;
        }
        
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
            target.position = new Vector3(
                reader.ReadSingle(), 
                reader.ReadSingle(), 
                reader.ReadSingle());
            
            target.rotation = Quaternion.Euler(0, 0, reader.ReadSingle());
        }

        public bool IsAtBaseline()
        {
            target ??= transform;
            return !_alwaysPersist &&
                   target.position == _baselinePosition &&
                   target.rotation == _baselineRotation;
        }
    }
}
