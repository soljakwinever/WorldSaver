using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class ClimateCoreData : ComponentDefinitionData
    {
        [Header("Climate")]
        [Range(-2f, 2f)] public float temperatureOffset = -0.5f;
        [Tooltip("Optional weather kept active throughout the influenced area.")]
        public Project.Scripts.DataTypes.WeatherData permanentWeather;

        [Header("Durability")]
        [Min(1)] public int maximumHealth = 500;

        [Header("Destruction Drop")]
        public ItemData droppedItem;
        [Min(1)] public int dropCount = 1;
        [Range(0f, 1f)] public float dropChance = 1f;
    }

    [CreateAssetMenu(
        fileName = "Climate Core",
        menuName = "World/Persistence/Climate Core")]
    public sealed class ClimateCoreDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(ClimateCoreData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (ClimateCoreData)data;
            PersistentHealth health = host.GetComponent<PersistentHealth>();
            if (health == null)
            {
                health =
                    container.InstantiateComponent<PersistentHealth>(host);
            }
            health.Initialize(configuration.maximumHealth);

            ClimateCore core =
                container.InstantiateComponent<ClimateCore>(host);
            core.Initialize(
                configuration.temperatureOffset,
                configuration.permanentWeather?.WeatherId,
                configuration.droppedItem,
                configuration.dropCount,
                configuration.dropChance);
        }
    }
}
