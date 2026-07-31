using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    /// <summary>
    /// Immediate-mode Town Core window used by the shared component-window
    /// service. Inventory values are read each draw so mana consumption and
    /// deposits remain live while the window is open.
    /// </summary>
    public sealed class TownCoreWindowSection : IComponentWindowSection
    {
        private enum TownTab
        {
            Overview,
            WorkPolicy
        }

        private readonly TownCore _town;
        private readonly IInventory _playerInventory;
        private readonly PlayerDataController _player;

        private TownTab _activeTab;
        private Vector2 _offeringScroll;
        private Vector2 _playerScroll;
        private bool _editingName;
        private string _editedName;

        public TownCoreWindowSection(
            TownCore town,
            IInventory playerInventory,
            PlayerDataController player)
        {
            _town = town ?? throw new ArgumentNullException(nameof(town));
            _playerInventory = playerInventory ??
                throw new ArgumentNullException(nameof(playerInventory));
            _player = player;
            _editedName = town.TownName;
        }

        public void Draw(ComponentWindowContext context)
        {
            GUILayout.BeginHorizontal();
            DrawOfferingPanel(context);
            GUILayout.Space(10f);
            DrawSidePanel(context);
            GUILayout.EndHorizontal();
        }

        private void DrawOfferingPanel(ComponentWindowContext context)
        {
            GUILayout.BeginVertical(
                GUI.skin.box,
                GUILayout.Width(510f),
                GUILayout.ExpandHeight(true));

            GUILayout.Label("Offering Box Inventory", HeaderStyle());
            GUILayout.Label(
                "Items placed here are consumed over time to generate mana.");

            _offeringScroll = GUILayout.BeginScrollView(
                _offeringScroll,
                GUI.skin.box,
                GUILayout.Height(235f));
            DrawInventoryGrid(
                _town.OfferingInventory,
                "The offering box is empty.",
                stack =>
                {
                    context.StatusMessage = TryTransfer(
                        _town.OfferingInventory,
                        _playerInventory,
                        stack)
                        ? $"Returned {stack.Item.name} to your inventory."
                        : "Your inventory does not have enough space.";
                });
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            GUILayout.Label(
                "Player Inventory — click a stack to offer it",
                GUI.skin.box);
            _playerScroll = GUILayout.BeginScrollView(
                _playerScroll,
                GUI.skin.box,
                GUILayout.Height(185f));
            DrawInventoryGrid(
                _playerInventory,
                "Your inventory is empty.",
                stack =>
                {
                    context.StatusMessage = TryTransfer(
                        _playerInventory,
                        _town.OfferingInventory,
                        stack)
                        ? $"Offered {stack.Item.name} x{stack.Count}."
                        : "The offering box cannot accept that stack.";
                });
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }

        private void DrawSidePanel(ComponentWindowContext context)
        {
            GUILayout.BeginVertical(
                GUI.skin.box,
                GUILayout.Width(260f),
                GUILayout.ExpandHeight(true));

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(
                    _activeTab == TownTab.Overview,
                    "Town",
                    GUI.skin.button))
                _activeTab = TownTab.Overview;
            if (GUILayout.Toggle(
                    _activeTab == TownTab.WorkPolicy,
                    "Work Policy",
                    GUI.skin.button))
                _activeTab = TownTab.WorkPolicy;
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            if (_activeTab == TownTab.WorkPolicy)
                DrawWorkPolicyStub();
            else
                DrawOverview(context);

            GUILayout.EndVertical();
        }

        private void DrawOverview(ComponentWindowContext context)
        {
            DrawNameEditor(context);
            GUILayout.Space(10f);

            // Population remains intentionally disconnected from villager
            // assignment until the job system owns residency.
            DrawStat("Population", "0/10");
            DrawMana();
            DrawCurrentEffect(context);

            GUILayout.Space(12f);
            GUILayout.Label("Player Spawn Point", GUI.skin.box);
            GUILayout.Label("Places the spawn directly beneath this shrine.");

            bool wasEnabled = GUI.enabled;
            GUI.enabled = _player != null;
            bool isCurrentSpawnPoint = IsCurrentSpawnPoint();
            string spawnButtonLabel = isCurrentSpawnPoint
                ? "You will spawn here"
                : "Set Spawn Position";
            if (GUILayout.Button(spawnButtonLabel, GUILayout.Height(30f)))
            {
                if (!isCurrentSpawnPoint)
                {
                    _player.SetSpawnTown(_town);
                    context.StatusMessage =
                        $"Spawn point set at {_town.TownName}.";
                }
            }
            GUI.enabled = wasEnabled;
            if (_player == null)
                GUILayout.Label("Player save data is unavailable.");

            GUILayout.Space(12f);
            GUILayout.Label("Upgrade Cost", GUI.skin.box);
            GUILayout.Label("Coming soon");
            GUI.enabled = false;
            GUILayout.Button("Upgrade Town", GUILayout.Height(30f));
            GUI.enabled = wasEnabled;
        }

        private void DrawNameEditor(ComponentWindowContext context)
        {
            GUILayout.Label("Town Name", GUI.skin.box);
            if (!_editingName)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    _town.TownName,
                    HeaderStyle(),
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Edit", GUILayout.Width(54f)))
                {
                    _editedName = _town.TownName;
                    _editingName = true;
                }
                GUILayout.EndHorizontal();
                return;
            }

            _editedName = GUILayout.TextField(
                _editedName ?? string.Empty,
                40);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                if (string.IsNullOrWhiteSpace(_editedName))
                {
                    context.StatusMessage =
                        "A town name cannot be empty.";
                }
                else
                {
                    _town.SetName(_editedName);
                    _editedName = _town.TownName;
                    _editingName = false;
                    context.StatusMessage = "Town renamed.";
                }
            }
            if (GUILayout.Button("Cancel"))
            {
                _editedName = _town.TownName;
                _editingName = false;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawMana()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Town Mana", GUI.skin.box);
            float maximum = Mathf.Max(0f, _town.MaxMana);
            float progress = maximum <= 0f
                ? 0f
                : Mathf.Clamp01(_town.ManaPool / maximum);
            Rect rect = GUILayoutUtility.GetRect(
                1f,
                24f,
                GUILayout.ExpandWidth(true));
            GUI.Box(rect, string.Empty);
            Rect fill = rect;
            fill.width *= progress;
            GUI.Box(
                fill,
                $"{_town.ManaPool:0.##} / {maximum:0.##}");
        }

        private void DrawCurrentEffect(ComponentWindowContext context)
        {
            GUILayout.Space(8f);
            GUILayout.Label("Current Town Effect", GUI.skin.box);
            TownEffect effect = _town.CurrentEffect;
            if (effect == null)
            {
                GUILayout.Label("None");
                GUILayout.Label("Mana cost: 0 per active tick");
            }
            else
            {
                GUILayout.Label(
                    string.IsNullOrWhiteSpace(effect.name)
                        ? effect.PersistentId
                        : effect.name);
                GUILayout.Label(
                    $"Mana cost: {effect.ManaCostPerTick:0.##} per active tick");
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("None", GUILayout.Height(24f)))
            {
                SelectEffect(null);
                context.StatusMessage = "Town effect disabled.";
            }

            foreach (TownEffect available in _town.AvailableEffects)
            {
                if (available == null)
                    continue;

                string label = string.IsNullOrWhiteSpace(available.name)
                    ? available.PersistentId
                    : available.name;
                if (!GUILayout.Button(label, GUILayout.Height(24f)))
                    continue;

                SelectEffect(available);
                context.StatusMessage = $"{label} selected.";
            }
            GUILayout.EndHorizontal();
        }

        private void SelectEffect(TownEffect selected)
        {
            foreach (TownEffect effect in _town.AvailableEffects)
            {
                if (effect != null)
                {
                    _town.SetEffectActive(
                        effect.PersistentId,
                        effect == selected);
                }
            }
        }

        private static void DrawWorkPolicyStub()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "Work Policy",
                HeaderStyle(),
                GUILayout.ExpandWidth(true));
            GUILayout.Label(
                "Coming soon",
                HeaderStyle(),
                GUILayout.ExpandWidth(true));
            GUILayout.FlexibleSpace();
        }

        private static void DrawStat(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, GUI.skin.box);
            GUILayout.EndHorizontal();
        }

        private static void DrawInventoryGrid(
            IInventory inventory,
            string emptyMessage,
            Action<IItemStack> clicked)
        {
            var stacks = new List<IItemStack>(inventory.Stacks);
            if (stacks.Count == 0)
            {
                GUILayout.Label(emptyMessage, GUILayout.Height(44f));
                return;
            }

            const int columns = 4;
            for (int index = 0; index < stacks.Count; index += columns)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    int stackIndex = index + column;
                    if (stackIndex >= stacks.Count)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }

                    IItemStack stack = stacks[stackIndex];
                    string rarity = stack.Rarity ==
                                    ItemData.Rarity.Common
                        ? string.Empty
                        : $"\n{stack.Rarity}";
                    if (GUILayout.Button(
                            $"{stack.Item.name}\nx{stack.Count}{rarity}",
                            GUILayout.Width(112f),
                            GUILayout.Height(62f)))
                    {
                        clicked(stack);
                        GUILayout.EndHorizontal();
                        return;
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        public static bool TryTransfer(
            IInventory source,
            IInventory destination,
            IItemStack stack)
        {
            if (source == null || destination == null || stack == null ||
                !source.Contains(stack))
                return false;

            var removal = new[]
            {
                new InventoryChange(
                    stack.Item,
                    -stack.Count,
                    stack.Rarity,
                    stack.Durability)
            };
            var addition = new[]
            {
                new InventoryChange(
                    stack.Item,
                    stack.Count,
                    stack.Rarity,
                    stack.Durability)
            };

            if (!source.CanApplyChanges(removal) ||
                !destination.CanApplyChanges(addition) ||
                !source.TryApplyChanges(removal))
                return false;

            if (destination.TryApplyChanges(addition))
                return true;

            // Both changes were preflighted, but restore the source if another
            // system changed the destination between validation and commit.
            source.TryApplyChanges(addition);
            return false;
        }

        private bool IsCurrentSpawnPoint()
        {
            return _player != null &&
                   _player.IsSpawnTown(_town);
        }

        private static GUIStyle HeaderStyle()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            return style;
        }
    }
}
