using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Persistence
{
    [CreateAssetMenu(
        fileName = "Furnace Component Definition",
        menuName = "World/Components/Furnace")]
    public sealed class FurnaceComponentDefinition : NodeComponentDefinition
    {
        [SerializeField] private string windowTitle = "Furnace";
        [SerializeField] private string interactionPrompt = "Use furnace";
        [SerializeField] private EntityTag fuelTag;
        [SerializeField] private ItemData outputItem;
        [SerializeField, Min(1)] private int itemsPerCycle = 1;
        [SerializeField, Min(1)] private long ticksPerCycle = 600;
        [SerializeField, Min(1)] private int fuelValuePerCycle = 1;
        [SerializeField] private ItemData.Rarity outputRarity =
            ItemData.Rarity.Common;

        public override void Install(
            GameObject host,
            DiContainer container,
            NodeComponentSpawnContext context)
        {
            FurnaceComponent furnace = host.GetComponent<FurnaceComponent>();
            if (furnace == null)
                furnace = container.InstantiateComponent<FurnaceComponent>(host);
            else
                container.Inject(furnace);

            furnace.Initialize(windowTitle, interactionPrompt, fuelTag,
                outputItem, itemsPerCycle, ticksPerCycle,
                fuelValuePerCycle, outputRarity);
        }
    }
}
