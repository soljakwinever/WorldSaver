using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using Project.Scripts.Utility;
using UnityEngine;
using Zenject;
using Random = UnityEngine.Random;

namespace Project.Scripts.Gameplay
{
    public class HarvestableObject : MonoBehaviour, IInteractable, IEntityComponent
    {
        [SerializeField] private InteractionType interactionType;
        [SerializeField] private ToolType toolType;
        [SerializeField] private ItemData itemData;
        [SerializeField, Min(1)] private int minItemsCreated = 1;
        [SerializeField, Min(1)] private int maxItemsCreated = 1;
        [SerializeField] private bool generateRarity = true;
        [SerializeField] private ItemData.Rarity rarity = ItemData.Rarity.Common;
        [SerializeField] private bool despawnOnHarvest = true;
        
        private IItemStackPickupPool _pickupPool;

        [Inject]
        public void Construct(IItemStackPickupPool pickupPool)
        {
            _pickupPool = pickupPool;
        }

        public void Initialize(
            InteractionType interactionType,
            ToolType toolType,
            ItemData itemData,
            int minItemsCreated,
            int maxItemsCreated = -1,
            bool generateRarity = true,
            ItemData.Rarity rarity = ItemData.Rarity.Common, 
            bool despawnOnHarvest = false)
        {
            this.interactionType = interactionType;
            this.toolType = toolType;

            this.itemData = itemData;

            this.minItemsCreated = Mathf.Max(1, minItemsCreated);
            this.maxItemsCreated = maxItemsCreated < 0
                ? this.minItemsCreated
                : Mathf.Max(this.minItemsCreated, maxItemsCreated);

            this.generateRarity = generateRarity;
            this.rarity = rarity;
            this.despawnOnHarvest = despawnOnHarvest;
        }

        public Vector3 GetPosition() => transform.position;

        public bool CanInteract(InteractionContext context)
        {
            return 
                context.interactionType == interactionType 
                && (context.interactionType != InteractionType.Direct
                    || toolType == (context.toolData?.ToolType ?? ToolType.None))
                && itemData != null
                && _pickupPool != null;
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context))
                return;

            int count = Random.Range(minItemsCreated, maxItemsCreated + 1);
            int luck =
                context.user != null
                    ? context.user
                        .GetComponentInParent<PlayerDataController>()?.Luck ?? 5
                    : 5;
            ItemData.Rarity spawnedRarity = generateRarity
                ? ItemRarityUtility.Generate(luck)
                : rarity;

            _pickupPool.Spawn(
                itemData,
                count,
                spawnedRarity,
                transform.position);

            if (despawnOnHarvest)
            {
                PersistentEntity.RemoveFromWorld();
            }
        }

        public string GetInteractionPrompt(InteractionContext context)
        {
            return itemData == null ? string.Empty : $"Harvest {itemData.name}";
        }

        private void OnValidate()
        {
            minItemsCreated = Mathf.Max(1, minItemsCreated);
            maxItemsCreated = Mathf.Max(minItemsCreated, maxItemsCreated);
        }

        public IPersistentEntity PersistentEntity { get; set; }
        public ItemData OutputItem => itemData;
        public int MinimumOutput => Mathf.Max(1, minItemsCreated);
        public int MaximumOutput => Mathf.Max(MinimumOutput, maxItemsCreated);
    }
}
