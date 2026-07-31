using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Utility;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class InventoryDebugUI : MonoBehaviour
    {
        [Header("Inventory")]
        [SerializeField] private PersistentInventory inventory;
        [SerializeField] private PlayerToolbarController toolbarController;

        [Header("Display")]
        [SerializeField] private Rect windowRect = new(16f, 96f, 420f, 360f);
        [SerializeField] private Rect otherWindowRect = new(452f, 96f, 420f, 360f);
        [SerializeField, Min(0f)] private float dropImpulse = 1.5f;

        private IInventory _otherInventory;
        private PersistentInventory _playerInventory;
        private IInputManager _inputManager;
        private IItemStackPickupPool _pickupPool;
        private Vector2 _playerScrollPosition;
        private Vector2 _otherScrollPosition;
        private IItemStack _hoveredPlayerStack;
        private bool _visible;
        private bool _subscribed;
        private string _inventoryBindingDisplay;
        private string _transferMessage;

        public IInventory Target => _otherInventory;
        public bool IsVisible => _visible;

        private void Awake()
        {
            _playerInventory = inventory != null
                ? inventory
                : GetComponentInParent<PersistentInventory>();
            if (toolbarController == null && _playerInventory != null)
                toolbarController =
                    _playerInventory.GetComponent<PlayerToolbarController>();

            _inventoryBindingDisplay = "I";
        }

        [Inject]
        public void Construct(
            IInputManager inputManager,
            IItemStackPickupPool pickupPool)
        {
            UnsubscribeFromInput();
            _inputManager = inputManager;
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
        }

        private void Update()
        {
            if (!_visible ||
                Keyboard.current?.escapeKey.wasPressedThisFrame != true)
                return;

            if (_otherInventory != null)
            {
                _otherInventory = null;
                _otherScrollPosition = Vector2.zero;
                _transferMessage = null;
                return;
            }

            _visible = false;
        }

        private void OnGUI()
        {
            if (!_visible)
                return;

            windowRect = GUI.Window(
                5555654,
                windowRect,
                DrawPlayerWindow,
                "Player Inventory");
            if (_otherInventory != null)
            {
                otherWindowRect = GUI.Window(
                    5555655,
                    otherWindowRect,
                    DrawOtherWindow,
                    GetOtherWindowTitle());
            }
        }

        public void SetInventory(IInventory target)
        {
            _otherInventory = target;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
        }

        private void DrawPlayerWindow(int id)
        {
            if (Event.current.type == EventType.Repaint)
                _hoveredPlayerStack = null;

            DrawInventory(
                _playerInventory,
                _otherInventory,
                ref _playerScrollPosition,
                "Transfer to other",
                ">");
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
        }

        private void DrawOtherWindow(int id)
        {
            DrawInventory(
                _otherInventory,
                _playerInventory,
                ref _otherScrollPosition,
                "Transfer to player",
                "<");
            GUI.DragWindow(new Rect(0f, 0f, otherWindowRect.width, 24f));
        }

        private void DrawInventory(
            IInventory source,
            IInventory destination,
            ref Vector2 scrollPosition,
            string transferTooltip,
            string transferLabel)
        {
            if (source == null)
            {
                GUILayout.Label("No inventory assigned.");
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Slots: {source.OccupiedSlots}/{source.Size}");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Open: {_inventoryBindingDisplay} | Close: Escape");
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_transferMessage))
                GUILayout.Label(_transferMessage);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            for (int slot = 0; slot < source.Size; slot++)
            {
                if (slot < source.Stacks.Count)
                    DrawStack(
                        slot,
                        source.Stacks[slot],
                        source,
                        destination,
                        transferTooltip,
                        transferLabel);
                else
                    DrawEmptySlot(slot);
            }

            GUILayout.EndScrollView();
        }

        private void OnInputPerformed(InputContext context)
        {
            if (_visible &&
                context.HotBarPressed != InputContext.NoHotbarKeyPressed)
            {
                TryAssignHoveredStackToToolbar(
                    _hoveredPlayerStack,
                    context.HotBarPressed);
            }

            if (context.InventoryPressed)
            {
                PersistentInventory hoveredInventory = GetInventoryUnderMouse();
                SetInventory(hoveredInventory);
                _otherScrollPosition = Vector2.zero;
                _transferMessage = null;
                _visible = true;
            }
        }

        private PersistentInventory GetInventoryUnderMouse()
        {
            if (Mouse.current == null || Camera.main == null)
                return null;

            Vector2 worldPosition =
                Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            Collider2D[] colliders = Physics2D.OverlapPointAll(worldPosition);

            foreach (Collider2D hoveredCollider in colliders)
            {
                PersistentInventory hoveredInventory =
                    hoveredCollider.GetComponentInParent<PersistentInventory>();
                if (hoveredInventory == null)
                {
                    hoveredInventory =
                        hoveredCollider.GetComponentInChildren<PersistentInventory>();
                }

                if (hoveredInventory != null &&
                    hoveredInventory != _playerInventory)
                    return hoveredInventory;
            }

            return null;
        }

        private void SubscribeToInput()
        {
            if (_subscribed || _inputManager == null)
                return;

            _inputManager.InputPerformed += OnInputPerformed;
            _subscribed = true;
        }

        private void UnsubscribeFromInput()
        {
            if (!_subscribed || _inputManager == null)
                return;

            _inputManager.InputPerformed -= OnInputPerformed;
            _subscribed = false;
        }

        private void DrawStack(
            int slot,
            IItemStack stack,
            IInventory source,
            IInventory destination,
            string transferTooltip,
            string transferLabel)
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            Color previousColor = GUI.color;
            GUI.color = ItemRarityUtility.GetRarityColor(stack.Rarity);
            GUILayout.Label($"[{slot + 1}] {GetDisplayName(stack)}", GUILayout.ExpandWidth(true));
            GUILayout.Label(stack.Rarity.ToString(), GUILayout.Width(90f));
            GUI.color = previousColor;
            GUILayout.Label($"{stack.Count}/{stack.Capacity}", GUILayout.Width(64f));
            if (destination != null &&
                GUILayout.Button(
                    new GUIContent(transferLabel, transferTooltip),
                    GUILayout.Width(32f)))
            {
                TransferStack(source, destination, stack);
                GUIUtility.ExitGUI();
            }
            if (ReferenceEquals(source, _playerInventory) &&
                GUILayout.Button(
                    new GUIContent("Drop", "Drop this stack on the ground"),
                    GUILayout.Width(48f)))
            {
                DropStack(stack);
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();

            Rect progressRect = GUILayoutUtility.GetRect(1f, 5f, GUILayout.ExpandWidth(true));
            GUI.Box(progressRect, GUIContent.none);
            if (stack.Capacity > 0)
            {
                float fill = Mathf.Clamp01(stack.Count / (float)stack.Capacity);
                Rect fillRect = new(progressRect.x, progressRect.y, progressRect.width * fill, progressRect.height);
                previousColor = GUI.color;
                GUI.color = ItemRarityUtility.GetRarityColor(stack.Rarity);
                GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
                GUI.color = previousColor;
            }

            GUILayout.EndVertical();

            Rect stackRect = GUILayoutUtility.GetLastRect();
            if (ReferenceEquals(source, _playerInventory) &&
                Event.current.type == EventType.Repaint &&
                stackRect.Contains(Event.current.mousePosition))
            {
                _hoveredPlayerStack = stack;
            }
        }

        private void TryAssignHoveredStackToToolbar(
            IItemStack stack,
            int hotbarIndex)
        {
            if (toolbarController == null || stack?.Item == null)
                return;

            if (stack.Item.action == null)
                return;

            // Bind the shared action to this item before it enters the hotbar.
            IHotbarAction hotbarAction =
                new ItemActionBinding(stack.Item);

            toolbarController.SetHotbarAction(hotbarIndex, hotbarAction);
            _transferMessage =
                $"Assigned {GetDisplayName(stack)} to toolbar slot {hotbarIndex + 1}.";
        }

        private void TransferStack(
            IInventory source,
            IInventory destination,
            IItemStack stack)
        {
            if (source == null || destination == null || stack == null ||
                ReferenceEquals(source, destination))
                return;

            int originalCount = stack.Count;
            if (!source.TryRemove(stack))
            {
                _transferMessage = "The transfer could not be completed.";
                return;
            }

            int remainder;
            try
            {
                destination.TryAdd(
                    stack.Item,
                    originalCount,
                    out remainder,
                    stack.Rarity,
                    stack.Durability);
            }
            catch (Exception exception)
            {
                source.TryAdd(
                    stack.Item,
                    originalCount,
                    out _,
                    stack.Rarity,
                    stack.Durability);
                _transferMessage = $"Transfer failed: {exception.Message}";
                return;
            }

            if (remainder > 0)
                source.TryAdd(
                    stack.Item,
                    remainder,
                    out _,
                    stack.Rarity,
                    stack.Durability);

            int transferred = originalCount - remainder;
            if (transferred == 0)
            {
                _transferMessage = "The destination inventory is full.";
                return;
            }

            _transferMessage = remainder == 0
                ? $"Transferred {originalCount} {GetDisplayName(stack)}."
                : $"Transferred {transferred}/{originalCount} {GetDisplayName(stack)}.";
        }

        private void DropStack(IItemStack stack)
        {
            if (_playerInventory == null || _pickupPool == null ||
                stack?.Item == null || stack.Count <= 0)
                return;

            // Snapshot every value before removal because the inventory mutates
            // (and may invalidate) the stack passed in by the UI.
            ItemData item = stack.Item;
            ItemData.Rarity rarity = stack.Rarity;
            byte durability = stack.Durability;
            int count = stack.Count;

            if (!_playerInventory.TryRemove(stack))
            {
                _transferMessage = "The stack could not be dropped.";
                return;
            }

            try
            {
                _pickupPool.Spawn(
                    item,
                    count,
                    rarity,
                    _playerInventory.transform.position,
                    GetDropDirection() * dropImpulse,
                    durability);
                _transferMessage =
                    $"Dropped {count} {GetDisplayName(item)} ({rarity}).";
            }
            catch (Exception exception)
            {
                _playerInventory.TryAdd(
                    item,
                    count,
                    out _,
                    rarity,
                    durability);
                _transferMessage = $"Drop failed: {exception.Message}";
            }
        }

        private Vector2 GetDropDirection()
        {
            if (Mouse.current == null || Camera.main == null ||
                _playerInventory == null)
                return Vector2.down;

            Vector2 mouseWorld =
                Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            Vector2 direction =
                mouseWorld - (Vector2)_playerInventory.transform.position;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector2.down;
        }

        private string GetOtherWindowTitle()
        {
            if (_otherInventory is Component component)
                return $"{component.gameObject.name} Inventory";

            return "Other Inventory";
        }

        private static void DrawEmptySlot(int slot)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, 0.45f);
            GUILayout.Label($"[{slot + 1}] Empty", GUI.skin.box);
            GUI.color = previousColor;
        }

        private static string GetDisplayName(IItemStack stack)
        {
            if (stack?.Item == null)
                return "Missing Item";

            return GetDisplayName(stack.Item);
        }

        private static string GetDisplayName(ItemData item)
        {
            if (item == null)
                return "Missing Item";

            return string.IsNullOrWhiteSpace(item.name)
                ? item.persistentId
                : item.name;
        }


    }
}
