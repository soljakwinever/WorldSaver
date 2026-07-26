using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using Project.Scripts.Utility;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class ItemStackPickup : MonoBehaviour, IInteractable, IItemStackPickup
    {
        [SerializeField] private ItemData item;
        [SerializeField, Min(1)] private int count = 1;
        [SerializeField] private bool generateRarity = true;
        [SerializeField] private ItemData.Rarity rarity = ItemData.Rarity.Common;

        private IItemStackPickupPool _pool;

        public void SetPool(IItemStackPickupPool pool)
        {
            _pool = pool;
        }

        public void Initialize(ItemData item, int count, ItemData.Rarity rarity)
        {
            this.item = item;
            this.count = count;
            this.rarity = rarity;

            if (TryGetComponent(out SpriteRenderer renderer))
            {
                renderer.color = ItemRarityUtility.GetRarityColor(rarity);
            }

            foreach (Transform child in transform)
            {
                if (child.TryGetComponent(out ParticleSystem particle))
                {
                    ParticleSystem.MainModule main = particle.main;
                    main.startColor =
                        ItemRarityUtility.GetRarityColor(rarity);
                }
            }
            
            this.generateRarity = false;
        }

        private void Awake()
        {
            if (generateRarity)
                rarity = ItemRarityUtility.Generate();
        }

        public Vector3 GetPosition()
        {
            return transform.position;
        }

        public bool CanInteract(InteractionContext context)
        {
            return context.interactionType == InteractionType.Direct
                && item != null
                && count > 0
                && TryGetInventory(context.user, out _);
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context) || !TryGetInventory(context.user, out IInventory inventory))
                return;

            inventory.TryAdd(item, count, out int remainder, rarity);
            count = remainder;

            if (count <= 0)
                RemovePickup();
        }

        public string GetInteractionPrompt(InteractionContext context)
        {
            if (item == null)
                return string.Empty;

            return $"Pick up {count}x {item.name} ({rarity})";
        }

        private void RemovePickup()
        {
            if (_pool != null)
                _pool.Despawn(this);
            else if (TryGetComponent(out IPersistentEntity persistentEntity))
                persistentEntity.RemoveFromWorld();
            else
                Destroy(gameObject);
        }

        private static bool TryGetInventory(GameObject user, out IInventory inventory)
        {
            inventory = null;
            if (user == null)
                return false;

            inventory = user.GetComponentInParent<PersistentInventory>();
            return inventory != null;
        }

        private void OnValidate()
        {
            if (item != null)
                count = Mathf.Clamp(count, 1, Mathf.Max(1, item.maxStack));
            else
                count = Mathf.Max(1, count);
        }
    }
}
