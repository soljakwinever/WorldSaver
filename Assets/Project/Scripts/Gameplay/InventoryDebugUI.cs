using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class InventoryDebugUI : MonoBehaviour
    {
        [Header("Inventory")]
        [SerializeField] private PersistentInventory inventory;

        [Header("Display")]
        [SerializeField] private bool visibleOnStart = true;
        [SerializeField] private Rect windowRect = new(16f, 96f, 420f, 360f);

        private IInventory _inventory;
        private IInputManager _inputManager;
        private Vector2 _scrollPosition;
        private bool _visible;
        private bool _subscribed;
        private string _inventoryBindingDisplay;

        public IInventory Target => _inventory;
        public bool IsVisible => _visible;

        private void Awake()
        {
            _visible = visibleOnStart;
            SetInventory(inventory != null
                ? inventory
                : GetComponentInParent<PersistentInventory>());

            _inventoryBindingDisplay = "Inventory action";
        }

        [Inject]
        public void Construct(IInputManager inputManager)
        {
            UnsubscribeFromInput();
            _inputManager = inputManager;

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

        private void OnGUI()
        {
            if (!_visible)
                return;

            windowRect = GUI.Window(5555654, windowRect, DrawWindow, "Inventory Debug");
        }

        public void SetInventory(IInventory target)
        {
            _inventory = target;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
        }

        private void DrawWindow(int id)
        {
            if (_inventory == null)
            {
                GUILayout.Label("No inventory assigned.");
                GUILayout.Label("Assign one in the Inspector or place this component under a PersistentInventory.");
                GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Slots: {_inventory.OccupiedSlots}/{_inventory.Size}");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Toggle: {_inventoryBindingDisplay}");
            GUILayout.EndHorizontal();

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            for (int slot = 0; slot < _inventory.Size; slot++)
            {
                if (slot < _inventory.Stacks.Count)
                    DrawStack(slot, _inventory.Stacks[slot]);
                else
                    DrawEmptySlot(slot);
            }

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
        }

        private void OnInputPerformed(InputContext context)
        {
            if (context.InventoryPressed)
                _visible = !_visible;
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

        private static void DrawStack(int slot, IItemStack stack)
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"[{slot + 1}] {GetDisplayName(stack)}", GUILayout.ExpandWidth(true));
            GUILayout.Label(stack.Rarity.ToString(), GUILayout.Width(90f));
            GUILayout.Label($"{stack.Count}/{stack.Capacity}", GUILayout.Width(64f));
            GUILayout.EndHorizontal();

            Rect progressRect = GUILayoutUtility.GetRect(1f, 5f, GUILayout.ExpandWidth(true));
            GUI.Box(progressRect, GUIContent.none);
            if (stack.Capacity > 0)
            {
                float fill = Mathf.Clamp01(stack.Count / (float)stack.Capacity);
                Rect fillRect = new(progressRect.x, progressRect.y, progressRect.width * fill, progressRect.height);
                Color previousColor = GUI.color;
                GUI.color = GetRarityColor(stack.Rarity);
                GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
                GUI.color = previousColor;
            }

            GUILayout.EndVertical();
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

            return string.IsNullOrWhiteSpace(stack.Item.name)
                ? stack.Item.persistentId
                : stack.Item.name;
        }

        private static Color GetRarityColor(Project.Scripts.DataTypes.ItemData.Rarity rarity)
        {
            return rarity switch
            {
                Project.Scripts.DataTypes.ItemData.Rarity.Uncommon => new Color(0.3f, 0.9f, 0.35f),
                Project.Scripts.DataTypes.ItemData.Rarity.Rare => new Color(0.25f, 0.55f, 1f),
                Project.Scripts.DataTypes.ItemData.Rarity.Epic => new Color(0.75f, 0.3f, 1f),
                Project.Scripts.DataTypes.ItemData.Rarity.Legendary => new Color(1f, 0.6f, 0.1f),
                _ => new Color(0.8f, 0.8f, 0.8f)
            };
        }
    }
}
