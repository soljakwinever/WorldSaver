using UnityEngine;

namespace Project.Scripts.DataTypes
{
    /// <summary>
    /// Data-layer base type for an AI behavior-tree asset.
    /// Concrete tree implementations live in the AI assembly.
    /// </summary>
    public abstract class BehaviourTreeData : ScriptableObject
    {
    }
}
