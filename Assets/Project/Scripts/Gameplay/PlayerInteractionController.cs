using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using System;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using Project.Scripts.Utility;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [RequireComponent(typeof(PlayerToolbarController))]
    public class PlayerInteractionController : MonoBehaviour
    {
        [SerializeField] private float interactRadius = 1.5f;
        [SerializeField] private LayerMask interactableMask;
        [SerializeField] private Transform facingPoint;

        [Header("Inventory Crafting")]
        [SerializeField] private string craftingWindowTitle =
            "Inventory Crafting";
        [SerializeField] private CraftingBenchLayout craftingLayout;
        [SerializeField] private RecipeList craftingRecipeList;
        
        [Inject] private PlayerBus _playerBus;
        
        private bool _inputSubscribed;
        
        private IInputManager inputManager;
        
        private IInteractable focusedInteractable;
        private PlayerToolbarController _toolbarController;
        private PersistentInventory _inventory;
        private IComponentWindowService _windowService;
        private ICraftingService _craftingService;
        private IItemStackPickupPool _pickupPool;
        private bool _inventoryCraftingOpen;

        private void Awake()
        {
            _toolbarController = GetComponent<PlayerToolbarController>();
            _inventory = GetComponent<PersistentInventory>();
        }

        [Inject]
        public void Construct(
            IInputManager inputManager,
            IComponentWindowService windowService,
            ICraftingService craftingService,
            IItemStackPickupPool pickupPool)
        {
            UnsubscribeFromInput();
            this.inputManager = inputManager;
            _windowService = windowService;
            _craftingService = craftingService;
            _pickupPool = pickupPool;

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
            if (_inventoryCraftingOpen)
                _windowService?.Close();
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

        public bool CanPerformSelectedAction()
        {
            IHotbarAction action = _toolbarController.SelectedItemAction;
            if (action == null ||
                !action.CanPerform(CreateItemActionContext()))
                return false;

            // Consumption belongs here; action assets only report success.
            return action is not ItemActionBinding { ConsumesItem: true } binding ||
                   TryGetConsumableStack(binding.ItemData, out _);
        }

        public bool TryPerformSelectedAction()
        {
            IHotbarAction action = _toolbarController.SelectedItemAction;
            if (action == null)
                return false;

            IItemStack consumableStack = null;
            // Resolve the exact bound item before performing the action.
            if (action is ItemActionBinding { ConsumesItem: true } binding &&
                !TryGetConsumableStack(binding.ItemData, out consumableStack))
            {
                return false;
            }

            ActionContext context = CreateItemActionContext();
            if (!action.CanPerform(context) || !action.Perform(context))
                return false;

            return consumableStack == null ||
                   _inventory.TryRemove(
                       consumableStack.Item,
                       1,
                       consumableStack.Rarity);
        }

        private bool TryGetConsumableStack(
            ItemData item,
            out IItemStack consumableStack)
        {
            if (_inventory != null && item != null)
            {
                foreach (IItemStack stack in _inventory.Stacks)
                {
                    if (stack.Item == item && stack.Count > 0)
                    {
                        consumableStack = stack;
                        return true;
                    }
                }
            }

            consumableStack = null;
            return false;
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

        private ActionContext CreateItemActionContext()
        {
            Vector3 targetPosition;
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Vector2 screenPosition = inputManager.MousePosition;
                targetPosition = mainCamera.ScreenToWorldPoint(
                    new Vector3(
                        screenPosition.x,
                        screenPosition.y,
                        -mainCamera.transform.position.z));
            }
            else
            {
                targetPosition = facingPoint != null
                    ? facingPoint.position
                    : transform.position;
            }

            return new ActionContext(
                gameObject,
                targetPosition,
                spawnItemDrop: SpawnItemDrop);
        }

        private void SpawnItemDrop(ItemData item, Vector3 position)
        {
            if (item == null || _pickupPool == null)
                return;

            _pickupPool.Spawn(
                item,
                1,
                ItemRarityUtility.Generate(),
                position);
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
                TryPerformSelectedAction();
            }

            if (context.CraftingPressed)
            {
                ToggleInventoryCrafting();
            }
        }

        private void ToggleInventoryCrafting()
        {
            if (_inventoryCraftingOpen)
            {
                _windowService.Close();
                return;
            }

            if (_windowService == null || _craftingService == null ||
                _inventory == null)
                return;

            InventoryCraftingWindowSection section =
                new(
                    craftingRecipeList?.Recipes ??
                    Array.Empty<CraftingRecipeData>(),
                    _inventory,
                    _craftingService,
                    _windowService.Close,
                    craftingLayout);

            _inventoryCraftingOpen = true;
            _windowService.Open(new ComponentWindowRequest(
                craftingWindowTitle,
                section.PreferredSize,
                section,
                () =>
                {
                    _inventoryCraftingOpen = false;
                    section.Dispose();
                }));
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(craftingWindowTitle))
                craftingWindowTitle = "Inventory Crafting";
        }
#endif
    }

    public sealed class InventoryCraftingWindowSection :
        IComponentWindowSection,
        IDisposable
    {
        private readonly CraftingBenchLayout _layout;
        private readonly CraftingBenchWindowContext _craftingContext;
        private readonly DefaultCraftingBenchLayout _ownedDefaultLayout;

        public Vector2 PreferredSize => _layout.DefaultWindowSize;

        public InventoryCraftingWindowSection(
            System.Collections.Generic.IReadOnlyList<CraftingRecipeData> recipes,
            IInventory inventory,
            ICraftingService craftingService,
            Action close,
            CraftingBenchLayout layout = null)
        {
            if (recipes == null)
                throw new ArgumentNullException(nameof(recipes));
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (craftingService == null)
                throw new ArgumentNullException(nameof(craftingService));
            if (close == null)
                throw new ArgumentNullException(nameof(close));

            if (layout == null)
            {
                _ownedDefaultLayout =
                    ScriptableObject.CreateInstance<DefaultCraftingBenchLayout>();
                _ownedDefaultLayout.hideFlags = HideFlags.HideAndDontSave;
                _layout = _ownedDefaultLayout;
            }
            else
            {
                _layout = layout;
            }

            CraftingBenchWindowContext craftingContext = null;
            craftingContext = new CraftingBenchWindowContext(
                recipes,
                inventory,
                inventory,
                recipe => craftingService.CanCraft(
                    recipe, inventory, inventory),
                recipe =>
                {
                    craftingService.TryCraft(
                        recipe, inventory, inventory,
                        out CraftResult result);
                    craftingContext.StatusMessage = result.Succeeded
                        ? DescribeSuccess(result)
                        : DescribeFailure(result.FailureReason);
                    return result;
                },
                close);
            _craftingContext = craftingContext;
        }

        public void Draw(ComponentWindowContext context)
        {
            _layout.Draw(_craftingContext);
            context.StatusMessage = _craftingContext.StatusMessage;
        }

        public void Dispose()
        {
            if (_ownedDefaultLayout == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_ownedDefaultLayout);
            else
                UnityEngine.Object.DestroyImmediate(_ownedDefaultLayout);
        }

        private static string DescribeSuccess(CraftResult result)
        {
            int total = 0;
            for (int i = 0; i < result.Outputs.Count; i++)
                total = checked(total + result.Outputs[i].Count);
            return total == 0
                ? "Crafted successfully."
                : $"Crafted {total} item{(total == 1 ? "" : "s")}.";
        }

        private static string DescribeFailure(CraftFailureReason reason)
        {
            return reason switch
            {
                CraftFailureReason.InvalidRecipe => "This recipe is invalid.",
                CraftFailureReason.MissingIngredients => "Missing ingredients.",
                CraftFailureReason.InsufficientOutputSpace =>
                    "Not enough inventory space.",
                _ => "Crafting failed."
            };
        }
    }
}
