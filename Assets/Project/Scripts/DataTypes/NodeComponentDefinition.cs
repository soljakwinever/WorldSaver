using UnityEngine;
using Zenject;

namespace Project.Scripts.DataTypes
{
    public abstract class NodeComponentDefinition : ScriptableObject
    {
        public abstract void Install(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context);
    }
}