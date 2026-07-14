using System;

namespace Project.Scripts.DataTypes.SaveData
{
    [Serializable]
    public sealed class WorldIdentityState
    {
        // Zero is reserved for an uninitialized/default NodeId.
        public ulong nextRuntimeEntityId = 1;
    }
}
