using System;
using UnityEngine;
using Zenject;

namespace Project.Scripts.DataTypes
{
    public abstract class NodeComponentDefinition : ScriptableObject
    {
        public abstract Type DataType { get; }

        public void Install(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            if (container == null)
                throw new ArgumentNullException(nameof(container));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.ComponentDefinition != this)
            {
                throw new InvalidOperationException(
                    $"{data.GetType().Name} references a different component definition.");
            }
            if (!DataType.IsInstanceOfType(data))
            {
                throw new InvalidOperationException(
                    $"{name} expects {DataType.Name}, but received {data.GetType().Name}.");
            }

            InstallComponent(host, container, context, data);
        }

        protected abstract void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data);
    }
}
