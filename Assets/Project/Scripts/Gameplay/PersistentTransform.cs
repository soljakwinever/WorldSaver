using System;
using System.IO;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public class PersistentTransform : MonoBehaviour, IPersistentComponent
    {
        public const ushort TypeId = 5;
        private const ushort CurrentVersion = 1;

        public ushort PersistentTypeId => TypeId;
        public ushort PersistentVersion => CurrentVersion;

        [SerializeField] private Transform target;
        private Vector3 _baselinePosition;
        private Quaternion _baselineRotation;
        private bool _alwaysPersist;

        public void Initialize(bool alwaysPersist, Transform host)
        {
            target = !host ? transform : host;
            _baselinePosition = target.position;
            _baselineRotation = target.rotation;
            _alwaysPersist = alwaysPersist;
        }

        private void Awake()
        {
            if (target == null)
                Initialize(alwaysPersist: false, transform);
        }
        
        public void WriteState(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            target ??= transform;
            Vector3 position = target.position;
            float rotation = target.rotation.eulerAngles.z;
            
            writer.Write(position.x);
            writer.Write(position.y);
            writer.Write(position.z);
            
            writer.Write(rotation);
        }

        public void ReadState(BinaryReader reader, ushort savedVersion)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (savedVersion != CurrentVersion)
                throw new InvalidDataException($"Unsupported transform state version {savedVersion}.");

            target ??= transform;
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
