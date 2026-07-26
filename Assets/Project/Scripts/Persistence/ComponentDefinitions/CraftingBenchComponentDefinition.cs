using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(
        fileName = "Crafting Bench Component Definition",
        menuName = "World/Components/Crafting Bench")]
    public sealed class CraftingBenchComponentDefinition : NodeComponentDefinition
    {
        [SerializeField] private string windowTitle = "Crafting Bench";
        [SerializeField] private string interactionPrompt = "Use crafting bench";
        [SerializeField] private CraftingBenchLayout layout;
        [SerializeField] private RecipeList recipeList;

        public override void Install(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context)
        {
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
                windowTitle,
                interactionPrompt,
                recipeList,
                layout);
        }
    }
}
