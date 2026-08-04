using Project.Scripts.Bus;
using Project.Scripts.DataTypes;
using System;
using System.Collections.Generic;
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
        [SerializeField, Min(0)] private int attackForce = 1;

        [Header("Placement Cursor")]
        [SerializeField] private Color validCursorColor =
            new(0.25f, 1f, 0.25f, 1f);
        [SerializeField] private Color invalidCursorColor =
            new(1f, 0.25f, 0.25f, 1f);
        [SerializeField] private int cursorSortingOrder = 1000;

        [Header("Inventory Crafting")]
        [SerializeField] private string craftingWindowTitle =
            "Inventory Crafting";
        [SerializeField] private CraftingBenchLayout craftingLayout;
        [SerializeField] private RecipeList craftingRecipeList;
        
        [Inject] private PlayerBus _playerBus;
        [InjectOptional] private IWaterTileQuery _waterTileQuery;
        
        private bool _inputSubscribed;
        
        private IInputManager inputManager;
        
        private IInteractable focusedInteractable;
        private PlayerToolbarController _toolbarController;
        private PersistentInventory _inventory;
        private IComponentWindowService _windowService;
        private ICraftingService _craftingService;
        private IItemStackPickupPool _pickupPool;
        private IAttackService _attackService;
        private IRoomVisibility _roomVisibility;
        private IWorldActionUiBlocker _uiBlocker;
        private bool _inventoryCraftingOpen;
        private SpriteRenderer _placementCursorRenderer;
        private Material _placementCursorMaterial;
        private float _nextRepeatedActionTime;
        private bool _repeatActionBlockedUntilRelease;

        private void Awake()
        {
            _toolbarController = GetComponent<PlayerToolbarController>();
            _inventory = GetComponent<PersistentInventory>();
            CreatePlacementCursor();
        }

        [Inject]
        public void Construct(
            IInputManager inputManager,
            IComponentWindowService windowService,
            ICraftingService craftingService,
            IItemStackPickupPool pickupPool,
            IAttackService attackService,
            IRoomVisibility roomVisibility,
            IWorldActionUiBlocker uiBlocker)
        {
            UnsubscribeFromInput();
            this.inputManager = inputManager;
            _windowService = windowService;
            _craftingService = craftingService;
            _pickupPool = pickupPool;
            _attackService = attackService;
            _roomVisibility = roomVisibility;
            _uiBlocker = uiBlocker;

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
            SetPlacementCursorVisible(false);
            _nextRepeatedActionTime = 0f;
            _repeatActionBlockedUntilRelease = false;
            if (_inventoryCraftingOpen)
                _windowService?.Close();
        }

        private void Update()
        {
            UpdateFocusedInteractable();
            UpdatePlacementCursor();
            UpdateRepeatedAction();
        }

        private void UpdateRepeatedAction()
        {
            if (inputManager == null ||
                !inputManager.AttackHeld)
            {
                _nextRepeatedActionTime = 0f;
                _repeatActionBlockedUntilRelease = false;
                return;
            }

            if (_repeatActionBlockedUntilRelease ||
                _toolbarController?.SelectedItemAction is not
                    IRepeatsWhileHeld repeatable ||
                repeatable.RepeatInterval <= 0f)
            {
                _nextRepeatedActionTime = 0f;
                return;
            }

            if (Time.time < _nextRepeatedActionTime)
                return;

            _nextRepeatedActionTime =
                Time.time + repeatable.RepeatInterval;
            TryPerformSelectedAction();
        }

        private void CreatePlacementCursor()
        {
            var cursorObject = new GameObject("Placement Cursor");
            cursorObject.transform.SetParent(transform);
            _placementCursorRenderer =
                cursorObject.AddComponent<SpriteRenderer>();
            _placementCursorRenderer.sortingLayerName = "Default";
            _placementCursorRenderer.sortingOrder = cursorSortingOrder;
            Shader unlitShader = Shader.Find(
                "Universal Render Pipeline/2D/Sprite-Unlit-Default");
            unlitShader ??= Shader.Find("Sprites/Default");
            if (unlitShader != null)
            {
                _placementCursorMaterial = new Material(unlitShader)
                {
                    name = "Placement Cursor Unlit Material",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _placementCursorRenderer.sharedMaterial =
                    _placementCursorMaterial;
            }
            _placementCursorRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (_placementCursorMaterial != null)
                Destroy(_placementCursorMaterial);
        }

        private void UpdatePlacementCursor()
        {
            if (_placementCursorRenderer == null ||
                _toolbarController == null ||
                _toolbarController.SelectedItemAction is not
                    IUsesCursor cursorAction ||
                !cursorAction.TryGetCursor(
                    CreateItemActionContext(),
                    out PlacementCursorData cursor) ||
                cursor.Sprite == null)
            {
                SetPlacementCursorVisible(false);
                return;
            }

            _placementCursorRenderer.transform.position = cursor.Position;
            _placementCursorRenderer.sprite = cursor.Sprite;
            bool isValid = cursor.IsValid &&
                           !IsPointerBlockingWorldAction();
            Color color = isValid
                ? validCursorColor
                : invalidCursorColor;
            color.a *= cursor.Opacity;
            _placementCursorRenderer.color = color;
            _placementCursorRenderer.enabled = true;
        }

        private void SetPlacementCursorVisible(bool visible)
        {
            if (_placementCursorRenderer != null)
                _placementCursorRenderer.enabled = visible;
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
            if (TryPerformSelectedDirectTileAction())
                return;

            if (TryRefillSelectedWateringCan())
                return;

            if (focusedInteractable == null)
                return;

            InteractionContext context = CreateDirectInteractionContext();
            if (focusedInteractable.CanInteract(context))
                focusedInteractable.Interact(context);
        }

        private bool TryPerformSelectedDirectTileAction()
        {
            if (_toolbarController?.SelectedItemAction is not
                ItemActionBinding
                {
                    PerformsOnDirectTileInteraction: true
                } binding)
            {
                return false;
            }

            ActionContext context = new(
                gameObject,
                transform.position,
                spawnItemDrop: SpawnItemDrop);
            return binding.CanPerform(context) && binding.Perform(context);
        }

        private bool TryRefillSelectedWateringCan()
        {
            if (_toolbarController?.SelectedItemAction is not ItemActionBinding binding ||
                binding.ItemData == null ||
                !binding.ItemData.TryGetActionData(out WaterTileToolActionData _) ||
                _inventory == null)
            {
                return false;
            }

            Vector3Int cell = Vector3Int.FloorToInt(transform.position);
            return _waterTileQuery?.IsWaterTile(cell) == true &&
                   _inventory.TryRefillDurability(binding.ItemData);
        }

        public bool CanPerformSelectedAction()
        {
            if (IsPointerBlockingWorldAction())
                return false;

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
            if (IsPointerBlockingWorldAction())
                return false;

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
            if (IsPointerBlockingWorldAction() ||
                tool == null || focusedInteractable == null)
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

        private bool TryAttackDamageable()
        {
            if (_attackService == null)
                return false;

            Vector2 attackCenter = facingPoint != null
                ? facingPoint.position
                : transform.position;
            Collider2D[] colliders = Physics2D.OverlapCircleAll(
                attackCenter,
                interactRadius,
                interactableMask);

            IDamageable closestTarget = null;
            float closestDistanceSquared = float.PositiveInfinity;
            foreach (Collider2D candidateCollider in colliders)
            {
                IDamageable candidate =
                    candidateCollider.GetComponentInParent<IDamageable>() ??
                    candidateCollider.GetComponentInChildren<IDamageable>();
                if (candidate == null ||
                    candidate is Component component &&
                    component.gameObject == gameObject)
                    continue;

                Vector2 closestPoint =
                    candidateCollider.ClosestPoint(attackCenter);
                float distanceSquared =
                    (closestPoint - attackCenter).sqrMagnitude;
                if (distanceSquared >= closestDistanceSquared)
                    continue;

                closestDistanceSquared = distanceSquared;
                closestTarget = candidate;
            }

            ToolData selectedTool = GetSelectedTool();
            if (closestTarget == null && selectedTool?.WeaponSwing == null)
                return false;

            AttackContext attack = new(
                gameObject,
                selectedTool,
                attackForce);
            if (selectedTool?.WeaponSwing != null)
            {
                WeaponSwingController swing =
                    GetComponent<WeaponSwingController>() ??
                    gameObject.AddComponent<WeaponSwingController>();
                HashSet<IDamageable> damaged = new();
                return swing.TryPlay(
                    selectedTool.WeaponSwing,
                    facingPoint != null
                        ? facingPoint.position
                        : transform.position + (Vector3)Vector2.down,
                    hit =>
                    {
                        IDamageable target =
                            hit.GetComponentInParent<IDamageable>() ??
                            hit.GetComponentInChildren<IDamageable>();
                        if (target != null && damaged.Add(target))
                            _attackService.Attack(target, attack);
                    });
            }

            int delivered = _attackService.Attack(closestTarget, attack);
            return delivered > 0;
        }

        private ToolData GetSelectedTool()
        {
            if (_toolbarController.SelectedItemAction is
                    ItemActionBinding binding &&
                binding.ItemData != null &&
                binding.ItemData.TryGetActionData(
                    out ToolHotbarActionData data))
            {
                return data.tool;
            }

            return null;
        }

        private ActionContext CreateItemActionContext()
        {
            Vector3 targetPosition;
            Camera mainCamera = Camera.main;
            if (mainCamera != null && inputManager != null)
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
                spawnItemDrop: SpawnItemDrop,
                target: ResolveSkillTarget(targetPosition));
        }

        private GameObject ResolveSkillTarget(Vector3 position)
        {
            Collider2D[] hits = Physics2D.OverlapPointAll(position, interactableMask);
            foreach (Collider2D hit in hits)
            {
                IDamageable damageable = hit.GetComponentInParent<IDamageable>() ??
                                         hit.GetComponentInChildren<IDamageable>();
                if (damageable is Component component && component.gameObject != gameObject)
                    return component.gameObject;
            }
            return null;
        }

        private bool IsPointerBlockingWorldAction()
        {
            if (inputManager == null)
                return false;

            Vector2 screenPosition = inputManager.MousePosition;
            if (_windowService?.IsPointerOverWindow(screenPosition) == true ||
                _uiBlocker?.IsPointerOverBlockingUi(screenPosition) == true)
            {
                return true;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null || _roomVisibility == null)
                return false;

            Vector3 worldPosition = mainCamera.ScreenToWorldPoint(
                new Vector3(
                    screenPosition.x,
                    screenPosition.y,
                    -mainCamera.transform.position.z));
            return _roomVisibility.IsWorldPositionMasked(worldPosition);
        }

        private void SpawnItemDrop(ItemData item, Vector3 position)
        {
            if (item == null || _pickupPool == null)
                return;

            _pickupPool.Spawn(
                item,
                1,
                ItemRarityUtility.Generate(
                    GetComponent<PlayerDataController>()?.Luck ?? 5),
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

            if (context.AttackPressed && !IsPointerBlockingWorldAction())
            {
                bool skillSelected =
                    _toolbarController.SelectedItemAction is SkillActionBinding ||
                    _toolbarController.SelectedItemAction is ItemActionBinding
                    {
                        ItemData: not null
                    } itemSkill &&
                    itemSkill.ItemData.TryGetActionData(
                        out UseSkillItemActionData _);
                ToolData selectedTool = GetSelectedTool();
                bool ignoresEntities = selectedTool != null && selectedTool.IgnoreEntities;
                if (skillSelected || ignoresEntities
                        ? TryPerformSelectedAction()
                        : TryAttackDamageable())
                {
                    _repeatActionBlockedUntilRelease = true;
                }
                else if (!skillSelected && !ignoresEntities)
                {
                    TryPerformSelectedAction();

                    if (_toolbarController.SelectedItemAction is
                            IRepeatsWhileHeld repeatable &&
                        repeatable.RepeatInterval > 0f)
                    {
                        _nextRepeatedActionTime =
                            Time.time + repeatable.RepeatInterval;
                    }
                }
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
            attackForce = Mathf.Max(0, attackForce);
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
