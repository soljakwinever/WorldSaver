using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [Serializable]
    public sealed class CraftingBenchComponentData : ComponentDefinitionData
    {
        public string windowTitle = "Crafting Bench";
        public string interactionPrompt = "Use crafting bench";
        public CraftingBenchLayout layout;
        public RecipeList recipeList;
    }

    [CreateAssetMenu(
        fileName = "Crafting Bench Component Definition",
        menuName = "World/Components/Crafting Bench")]
    public sealed class CraftingBenchComponentDefinition : NodeComponentDefinition
    {
        public override Type DataType =>
            typeof(CraftingBenchComponentData);

        protected override void InstallComponent(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context,
            ComponentDefinitionData data)
        {
            var configuration = (CraftingBenchComponentData)data;
            CraftingBenchComponent component =
                host.GetComponent<CraftingBenchComponent>();
            if (component == null)
            {
                component =
                    container.InstantiateComponent<CraftingBenchComponent>(host);
            }
            else
            {
                container.Inject(component);
            }

            component.Initialize(
                configuration.windowTitle,
                configuration.interactionPrompt,
                configuration.recipeList,
                configuration.layout);
        }
    }
}
