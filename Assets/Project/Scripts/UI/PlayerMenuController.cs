using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Project.Scripts.UI
{
    /// <summary>Drives the tabbed UI Toolkit player menu embedded in PlayerHUD.</summary>
    public sealed class PlayerMenuController : IDisposable
    {
        private readonly PlayerDataController _player;
        private readonly PlayerToolbarController _toolbar;
        private readonly PersistentInventory _inventory;
        private readonly PlayerEquipmentController _equipment;
        private readonly IReadOnlyList<SkillData> _skills;
        private readonly IReadOnlyDictionary<SkillCategory, SkillTreeData> _trees;
        private readonly IInputManager _input;
        private readonly VisualElement _menu;
        private readonly VisualElement _characterPage;
        private readonly VisualElement _skillsPage;
        private readonly VisualElement _inventoryPage;
        private readonly VisualElement _skillsList;
        private readonly VisualElement _inventoryGrid;
        private readonly VisualElement _skillTreeCanvas;
        private readonly List<SkillCategory> _treeCategories = new();
        private int _treeCategoryIndex;
        private int _builtSkillCount = -1;
        private SkillData _hoveredSkill;
        private bool _visible;

        private sealed class SkillTreeConnections : VisualElement
        {
            private readonly List<(Vector2 From, Vector2 To, bool Unlocked)> _lines;

            public SkillTreeConnections(
                List<(Vector2 From, Vector2 To, bool Unlocked)> lines)
            {
                _lines = lines;
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 0f;
                style.top = 0f;
                style.right = 0f;
                style.bottom = 0f;
                generateVisualContent += DrawConnections;
            }

            private void DrawConnections(MeshGenerationContext context)
            {
                Painter2D painter = context.painter2D;
                painter.lineWidth = 3f;
                foreach ((Vector2 from, Vector2 to, bool unlocked) in _lines)
                {
                    painter.strokeColor = unlocked
                        ? new Color32(255, 196, 70, 255)
                        : new Color32(77, 91, 112, 255);
                    painter.BeginPath();
                    painter.MoveTo(from);
                    painter.LineTo(to);
                    painter.Stroke();
                }
            }
        }

        public bool IsVisible => _visible;

        public PlayerMenuController(
            VisualElement root,
            PlayerDataController player,
            SkillCatalog skillCatalog,
            IInputManager input)
        {
            _player = player;
            _toolbar = player.GetComponent<PlayerToolbarController>();
            _inventory = player.GetComponent<PersistentInventory>();
            _equipment = player.GetComponent<PlayerEquipmentController>();
            _skills = skillCatalog?.Skills ?? Array.Empty<SkillData>();
            _trees = skillCatalog?.Trees ??
                     new Dictionary<SkillCategory, SkillTreeData>();
            _input = input;
            _menu = root.Q("PlayerMenu");
            _characterPage = root.Q("CharacterPage");
            _skillsPage = root.Q("SkillsPage");
            _inventoryPage = root.Q("InventoryPage");
            _skillsList = root.Q("LearnedSkillsList");
            _inventoryGrid = root.Q("InventoryGrid");
            _skillTreeCanvas = root.Q("SkillTreeCanvas");

            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            {
                Label equipmentLabel = _menu.Q<Label>($"Equipment{slot}");
                UniversalToolTip.Bind(equipmentLabel, () => GetEquippedToolTip(slot));
            }

            foreach (SkillCategory category in Enum.GetValues(typeof(SkillCategory)))
                if (_trees.TryGetValue(category, out SkillTreeData tree) && tree?.root != null)
                    _treeCategories.Add(category);

            root.Q<Button>("CharacterTab").clicked += () => SelectPage(_characterPage, "CharacterTab");
            root.Q<Button>("SkillsTab").clicked += () => SelectPage(_skillsPage, "SkillsTab");
            root.Q<Button>("InventoryTab").clicked += () => SelectPage(_inventoryPage, "InventoryTab");
            root.Q<Button>("PreviousSkillTree").clicked += () => ChangeTree(-1);
            root.Q<Button>("NextSkillTree").clicked += () => ChangeTree(1);
            _input.InputPerformed += OnInputPerformed;
            BuildSkillList();
            BuildSkillTree();
            SelectPage(_characterPage, "CharacterTab");
            SetVisible(false);
        }

        public void Dispose() => _input.InputPerformed -= OnInputPerformed;

        public void PollKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.tabKey.wasPressedThisFrame)
                SetVisible(!_visible);
            else if (_visible && keyboard.escapeKey.wasPressedThisFrame)
                SetVisible(false);

            if (_visible)
                Refresh();
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            _menu.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible)
            {
                _menu.BringToFront();
                _menu.Focus();
                Refresh();
            }
            else
                _hoveredSkill = null;
        }

        private void SelectPage(VisualElement page, string tabName)
        {
            _characterPage.style.display = page == _characterPage ? DisplayStyle.Flex : DisplayStyle.None;
            _skillsPage.style.display = page == _skillsPage ? DisplayStyle.Flex : DisplayStyle.None;
            _inventoryPage.style.display = page == _inventoryPage ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (string name in new[] { "CharacterTab", "SkillsTab", "InventoryTab" })
                _menu.Q<Button>(name).EnableInClassList("player-menu__tab--active", name == tabName);
            if (_visible)
                Refresh();
        }

        private void Refresh()
        {
            if (_builtSkillCount != _player.UnlockedSkills.Count)
                BuildSkillList();
            SetText("CharacterLevel", $"Level {_player.Level}");
            SetText("CharacterExperience", $"{_player.Experience} / {_player.ExperienceToNextLevel} EXP");
            ProgressBar xp = _menu.Q<ProgressBar>("CharacterExperienceBar");
            xp.highValue = _player.ExperienceToNextLevel;
            xp.value = _player.Experience;
            SetText("HealthValue", $"{_player.Health} / {_player.MaxHealth}");
            SetText("ManaValue", $"{_player.CurrentMana} / {_player.MaxMana}");
            SetText("EnergyValue", $"{_player.CurrentEnergy} / {_player.MaxEnergy}");
            SetText("HungerValue", $"{_player.CurrentHunger} / {_player.MaxHunger}");
            SetText("StrengthValueMenu", _player.Strength.ToString());
            SetText("ConstitutionValueMenu", _player.Constitution.ToString());
            SetText("DexterityValueMenu", _player.Dexterity.ToString());
            SetText("WisdomValueMenu", _player.Wisdom.ToString());
            SetText("IntelligenceValueMenu", _player.Intelligence.ToString());
            SetText("LuckValueMenu", _player.Luck.ToString());
            SetText("DefenseValueMenu", _player.Defense.ToString());
            SetText("SkillPoints", $"{_player.UnspentSkillPoints} skill point" +
                                   (_player.UnspentSkillPoints == 1 ? string.Empty : "s"));
            RefreshEquipment();
            RefreshInventory();
        }

        private void RefreshEquipment()
        {
            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            {
                Label label = _menu.Q<Label>($"Equipment{slot}");
                if (label == null)
                    continue;
                label.text = _equipment != null && _equipment.TryGetEquipped(slot, out IItemStack stack)
                    ? stack.Item.name
                    : "Empty";
            }
        }

        private void BuildSkillList()
        {
            _builtSkillCount = _player.UnlockedSkills.Count;
            _skillsList.Clear();
            SkillCategory? category = null;
            foreach (SkillData skill in _skills)
            {
                if (!_player.HasSkill(skill))
                    continue;
                if (category != skill.category)
                {
                    category = skill.category;
                    var header = new Label(skill.category.ToString());
                    header.AddToClassList("skill-category");
                    _skillsList.Add(header);
                }

                var row = new VisualElement();
                row.AddToClassList("learned-skill");
                var icon = new Image { sprite = skill.sprite, pickingMode = PickingMode.Ignore };
                icon.AddToClassList("learned-skill__icon");
                var details = new VisualElement { pickingMode = PickingMode.Ignore };
                details.AddToClassList("learned-skill__details");
                var name = new Label(skill.name);
                name.AddToClassList("learned-skill__name");
                details.Add(name);
                if (skill.staminaCost > 0f)
                    details.Add(new Label($"{skill.staminaCost:0.#} Stamina"));
                if (skill.manaCost > 0f)
                    details.Add(new Label($"{skill.manaCost:0.#} Mana"));
                var element = new Label(skill.element.ToString());
                element.AddToClassList("learned-skill__element");
                details.Add(element);
                row.Add(icon);
                row.Add(details);
                UniversalToolTip.Bind(row, () => skill);
                row.RegisterCallback<PointerEnterEvent>(_ => _hoveredSkill = skill);
                row.RegisterCallback<PointerLeaveEvent>(_ => { if (_hoveredSkill == skill) _hoveredSkill = null; });
                _skillsList.Add(row);
            }
            if (_player.UnlockedSkills.Count == 0)
                _skillsList.Add(new Label("No skills learned yet."));
        }

        private void ChangeTree(int direction)
        {
            if (_treeCategories.Count <= 1)
                return;
            _treeCategoryIndex =
                (_treeCategoryIndex + direction + _treeCategories.Count) % _treeCategories.Count;
            BuildSkillTree();
        }

        private void BuildSkillTree()
        {
            _skillTreeCanvas.Clear();
            Button previous = _menu.Q<Button>("PreviousSkillTree");
            Button next = _menu.Q<Button>("NextSkillTree");
            bool canSwitch = _treeCategories.Count > 1;
            previous?.SetEnabled(canSwitch);
            next?.SetEnabled(canSwitch);
            if (_treeCategories.Count == 0)
            {
                SetText("SkillTreeCategory", "NO SKILL TREES");
                _skillTreeCanvas.Add(new Label("Create SkillTreeData assets under Resources/SkillTrees."));
                return;
            }

            SkillCategory category = _treeCategories[_treeCategoryIndex];
            SkillTreeData tree = _trees[category];
            SetText("SkillTreeCategory", category.ToString().ToUpperInvariant());

            IReadOnlyDictionary<SkillTreeNode, Vector2Int> layout;
            try
            {
                layout = tree.EvaluateLayout();
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError(exception.Message, tree);
                _skillTreeCanvas.Add(new Label(exception.Message));
                return;
            }

            const float unit = 92f;
            const float nodeSize = 68f;
            const float padding = 38f;
            int minX = 0, maxX = 0, minY = 0, maxY = 0;
            foreach (Vector2Int point in layout.Values)
            {
                minX = Math.Min(minX, point.x); maxX = Math.Max(maxX, point.x);
                minY = Math.Min(minY, point.y); maxY = Math.Max(maxY, point.y);
            }
            float width = (maxX - minX) * unit + nodeSize + padding * 2f;
            float height = (maxY - minY) * unit + nodeSize + padding * 2f;
            _skillTreeCanvas.style.width = width;
            _skillTreeCanvas.style.height = height;

            var connections = new List<(Vector2 From, Vector2 To, bool Unlocked)>();
            foreach (KeyValuePair<SkillTreeNode, Vector2Int> pair in layout)
            {
                SkillTreeNode parent = pair.Key;
                Vector2 parentCenter = ToCanvas(pair.Value);
                foreach (SkillTreeNode child in parent.connections)
                {
                    Vector2 childCenter = ToCanvas(layout[child]);
                    bool unlocked = _player.HasUnlockedNode(parent) &&
                                    _player.HasUnlockedNode(child);
                    connections.Add((parentCenter, childCenter, unlocked));
                }
            }
            _skillTreeCanvas.Add(new SkillTreeConnections(connections));

            foreach (KeyValuePair<SkillTreeNode, Vector2Int> pair in layout)
            {
                SkillTreeNode node = pair.Key;
                Vector2 center = ToCanvas(pair.Value);
                var button = new Button(() => UnlockNode(node));
                button.AddToClassList("skill-tree-node");
                bool unlocked = _player.HasUnlockedNode(node);
                bool available = !unlocked && _player.UnspentSkillPoints > 0 &&
                                 (node.Parent == null || _player.HasUnlockedNode(node.Parent));
                if (unlocked) button.AddToClassList("skill-tree-node--unlocked");
                else if (available) button.AddToClassList("skill-tree-node--available");
                button.SetEnabled(available);
                UniversalToolTip.Bind(button, () => node.skill);
                button.style.left = center.x - nodeSize * .5f;
                button.style.top = center.y - nodeSize * .5f;
                var icon = new Image { sprite = node.skill.sprite, pickingMode = PickingMode.Ignore };
                icon.AddToClassList("skill-tree-node__icon");
                button.Add(icon);
                _skillTreeCanvas.Add(button);
            }

            Vector2 ToCanvas(Vector2Int point) => new(
                padding + (point.x - minX) * unit + nodeSize * .5f,
                padding + (maxY - point.y) * unit + nodeSize * .5f);
        }

        private void UnlockNode(SkillTreeNode node)
        {
            if (!_player.TryUnlockSkillTreeNode(node))
                return;
            BuildSkillList();
            BuildSkillTree();
        }

        private void RefreshInventory()
        {
            _inventoryGrid.Clear();
            long gold = 0;
            if (_inventory != null)
            {
                foreach (IItemStack stack in _inventory.Stacks)
                {
                    gold += (long)stack.Item.GetGoldValue() * stack.Count;
                    var slot = new VisualElement();
                    slot.AddToClassList("inventory-slot");
                    UniversalToolTip.Bind(slot, () => stack);
                    var icon = new Image { sprite = stack.Item.sprite, pickingMode = PickingMode.Ignore };
                    icon.AddToClassList("inventory-slot__icon");
                    slot.Add(icon);
                    if (stack.Count > 1)
                    {
                        var count = new Label(stack.Count.ToString());
                        count.AddToClassList("inventory-slot__count");
                        slot.Add(count);
                    }
                    _inventoryGrid.Add(slot);
                }
            }
            SetText("GoldValue", gold.ToString("N0"));
            SetText("InventoryCapacity", $"{_inventory?.OccupiedSlots ?? 0} / {_inventory?.Size ?? 0} slots");
        }

        private void OnInputPerformed(InputContext context)
        {
            if (!_visible || _hoveredSkill == null ||
                context.HotBarPressed == InputContext.NoHotbarKeyPressed)
                return;
            _toolbar.SetSkill(context.HotBarPressed, _hoveredSkill);
        }

        private IToolTipData GetEquippedToolTip(EquipmentSlot slot) =>
            _equipment != null && _equipment.TryGetEquipped(slot, out IItemStack stack)
                ? stack
                : null;

        private void SetText(string name, string value)
        {
            Label label = _menu.Q<Label>(name);
            if (label != null)
                label.text = value;
        }
    }
}
