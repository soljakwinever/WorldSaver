using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class FurnaceComponentData : ComponentDefinitionData
    {
        public string windowTitle = "Furnace";
        public string interactionPrompt = "Use furnace";
        public EntityTag fuelTag;
        [Tooltip("Recipes this furnace can match and craft automatically.")]
        public RecipeList recipeList;
        [Tooltip("Number of ingredient stacks shown as a vertical 1xX column.")]
        [Min(1)] public int ingredientSlots = 3;
        [Min(1)] public long ticksPerCycle = 600;
        [Min(1)] public int fuelValuePerCycle = 1;
    }

    [CreateAssetMenu(
        fileName = "Furnace Component Definition",
        menuName = "World/Components/Furnace")]
    public sealed class FurnaceComponentDefinition : NodeComponentDefinition
    {
        public override Type DataType => typeof(FurnaceComponentData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (FurnaceComponentData)data;
            FurnaceComponent furnace = host.GetComponent<FurnaceComponent>();
            if (furnace == null)
                furnace = container.InstantiateComponent<FurnaceComponent>(host);
            else
                container.Inject(furnace);

            furnace.Initialize(
                configuration.windowTitle,
                configuration.interactionPrompt,
                configuration.fuelTag,
                configuration.recipeList,
                configuration.ingredientSlots,
                configuration.ticksPerCycle,
                configuration.fuelValuePerCycle);
        }
    }
}
