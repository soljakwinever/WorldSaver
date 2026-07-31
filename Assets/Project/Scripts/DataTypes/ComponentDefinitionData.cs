using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    /// <summary>
    /// Base for polymorphic component configuration stored inline on a
    /// <see cref="NodeData"/>. The referenced definition supplies installation
    /// behaviour; derived records supply the per-node values.
    /// </summary>
    [Serializable]
    public abstract class ComponentDefinitionData
    {
        [Tooltip("Stateless installer associated with this component data.")]
        public NodeComponentDefinition componentDefinition;

        public NodeComponentDefinition ComponentDefinition =>
            componentDefinition;
    }
}
