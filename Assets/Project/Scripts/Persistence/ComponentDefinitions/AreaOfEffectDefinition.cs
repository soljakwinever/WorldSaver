using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class AreaOfEffectData : ComponentDefinitionData
    {
        [Min(0f)] public float outerRadius = 64f;
        [Min(0f)] public float innerRadius = 16f;
        [Min(0f)] public float spreadRate = 0.05f;
    }

    [CreateAssetMenu(
        fileName = "Area Of Effect",
        menuName = "World/Persistence/Area Of Effect")]
    public sealed class AreaOfEffectDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(AreaOfEffectData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (AreaOfEffectData)data;
            AreaOfEffect component =
                container.InstantiateComponent<AreaOfEffect>(host);
            component.Initialize(
                configuration.outerRadius,
                configuration.innerRadius,
                configuration.spreadRate);
        }
    }
}
