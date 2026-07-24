using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    public class PlayerInteractionController : MonoBehaviour
    {
        [SerializeField] private float interactRadius = 1.5f;
        [SerializeField] private LayerMask interactableMask;
        [SerializeField] private Transform facingPoint;
        
        [Inject] private PlayerBus _playerBus;
        
        private bool _inputSubscribed;
        
        private IInputManager inputManager;
        
        private IInteractable focusedInteractable;
        private ItemData activeItem;
        private ItemData.Rarity activeItemRarity;

        [Inject]
        public void Construct(IInputManager inputManager)
        {
            UnsubscribeFromInput();
            this.inputManager = inputManager;

            if (isActiveAndEnabled)
                SubscribeToInput();
        }

        private void OnEnable()
        {
            SubscribeToInput();
        }

        private void OnDisable()
        {
            UnsubscribeFromInput();
        }

        private void Update()
        {
            UpdateFocusedInteractable();
        }

        private void UpdateFocusedInteractable()
        {
            Vector2 interactionCenter = facingPoint != null
                ? facingPoint.position
                : transform.position;
            InteractionContext context = CreateDirectInteractionContext();

            focusedInteractable = null;
            float closestDistanceSquared = float.PositiveInfinity;

            Collider2D[] colliders = Physics2D.OverlapCircleAll(
                interactionCenter,
                interactRadius,
                interactableMask);

            foreach (Collider2D candidateCollider in colliders)
            {
                Vector2 closestPoint = candidateCollider.ClosestPoint(interactionCenter);
                float distanceSquared = (closestPoint - interactionCenter).sqrMagnitude;
                if (distanceSquared >= closestDistanceSquared)
                    continue;

                IInteractable[] interactables =
                    candidateCollider.GetComponentsInChildren<IInteractable>();

                foreach (IInteractable candidate in interactables)
                {
                    if (candidate == null || !candidate.CanInteract(context))
                        continue;

                    closestDistanceSquared = distanceSquared;
                    focusedInteractable = candidate;
                    break;
                }
            }

            _playerBus.RaiseInteractableHovered(focusedInteractable, context);
        }
        
        private void TryDirectInteract()
        {
            if (focusedInteractable == null)
                return;

            InteractionContext context = CreateDirectInteractionContext();
            if (focusedInteractable.CanInteract(context))
                focusedInteractable.Interact(context);
        }

        public void SetActiveItem(
            ItemData item,
            ItemData.Rarity rarity = ItemData.Rarity.Common)
        {
            activeItem = item;
            activeItemRarity = rarity;
        }

        public bool CanUseActiveItem()
        {
            return activeItem != null &&
                   activeItem.action != null &&
                   activeItem.action.CanPerform(CreateItemActionContext());
        }

        public bool TryUseActiveItem()
        {
            if (!CanUseActiveItem())
                return false;

            ItemAction action = activeItem.action;
            PersistentInventory inventory = null;
            if (action.ConsumesItem)
            {
                inventory = GetComponent<PersistentInventory>();
                if (inventory == null ||
                    !inventory.Contains(activeItem, 1, activeItemRarity))
                    return false;
            }

            if (!action.Perform(CreateItemActionContext()))
                return false;

            return !action.ConsumesItem ||
                   inventory.TryRemove(activeItem, 1, activeItemRarity);
        }

        public bool CanUseTool(ToolData tool)
        {
            if (tool == null || focusedInteractable == null)
                return false;

            return focusedInteractable.CanInteract(
                new InteractionContext(gameObject, InteractionType.Tool, tool));
        }

        public bool TryUseTool(ToolData tool)
        {
            if (!CanUseTool(tool))
                return false;

            focusedInteractable.Interact(
                new InteractionContext(gameObject, InteractionType.Tool, tool));
            return true;
        }

        private ItemActionContext CreateItemActionContext()
        {
            Vector3 targetPosition = facingPoint != null
                ? facingPoint.position
                : transform.position;
            return new ItemActionContext(gameObject, targetPosition);
        }

        private InteractionContext CreateDirectInteractionContext()
        {
            return new InteractionContext(gameObject, InteractionType.Direct, null);
        }
        
        private void InputManagerOnInputPerformed(InputContext context)
        {
            if (context.InteractionPressed)
            {
                TryDirectInteract();
            }

            if (context.AttackPressed)
            {
                TryUseActiveItem();
            }

            if (context.HotBarPressed != InputContext.NoHotbarKeyPressed)
            {
                SetSelectedItem(context.HotBarPressed);
            }
        }

        private void SetSelectedItem(int contextHotBarPressed)
        {
            
        }


        private void SubscribeToInput()
        {
            if (_inputSubscribed || inputManager == null) return;
            
            inputManager.InputPerformed += InputManagerOnInputPerformed;
            _inputSubscribed = true;
        }

        private void UnsubscribeFromInput()
        {
            if(!_inputSubscribed  || inputManager == null) return;
            
            inputManager.InputPerformed -= InputManagerOnInputPerformed;
            _inputSubscribed = false;
        }
    }
}
