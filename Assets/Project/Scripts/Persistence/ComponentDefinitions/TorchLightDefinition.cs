using System;
using Project.Scripts.DataTypes;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class TorchLightData : ComponentDefinitionData
    {
        [Min(0f)] public float intensity = 1.5f;
        [Min(0f)] public float outerRadius = 5f;
        public Color color = new(1f, 0.58f, 0.22f, 1f);
        public bool shadows = true;
    }

    [CreateAssetMenu(
        fileName = "Torch Light",
        menuName = "World/Components/Torch Light")]
    public sealed class TorchLightDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(TorchLightData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            TorchLightData configuration = (TorchLightData)data;
            Light2D light = host.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.intensity = Mathf.Max(0f, configuration.intensity);
            light.pointLightOuterRadius =
                Mathf.Max(0f, configuration.outerRadius);
            light.color = configuration.color;
            light.shadowsEnabled = configuration.shadows;
        }
    }
}
