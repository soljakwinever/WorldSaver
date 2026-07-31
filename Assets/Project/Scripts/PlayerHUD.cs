using System;
using Project.Scripts.Bus;
using Project.Scripts.Core;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using Project.Scripts.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace Project.Scripts
{
    [RequireComponent(typeof(PanelRenderer))]
    public class PlayerHUD : MonoBehaviour
    {
        private PanelRenderer _uiDocument;
    
        [Inject] private PlayerDataController playerDataController;
        [Inject] private WorldGeneration worldGeneration;
        [Inject] private IInputManager inputManager;
        [Inject] private IRegionalWeatherService regionalWeatherService;
        [Inject] private Grid gameGrid;
        
        private PlayerToolbarController toolbarController;
        private PlayerNeedsController needsController;
        private PlayerBus playerBus;
        private Label levelLabel;
        private Label experienceLabel;
        private ProgressBar experienceBar;
        private ProgressBar healthBar;
        private ProgressBar manaBar;
        private ProgressBar hungerBar;
        private ProgressBar energyBar;
        private VisualElement levelUpPanel;
        private Label statPointsLabel;
        private readonly System.Collections.Generic.Dictionary<PlayerStat, Label>
            statValueLabels = new();
        private readonly System.Collections.Generic.List<Button>
            statButtons = new();
        
        private HotbarSlot[] hotbarSlots =
            new HotbarSlot[PlayerToolbarController.SlotCount];

        private BiomeDebugData debugData = new BiomeDebugData();
        
        [Serializable]
        public class BiomeDebugData
        {
            public Vector2Int chunkPosition;
            public Vector2Int cursorPosition;
            public float continentalness;
            public float temperature;
            public float moisture;
            public string biome;
            public int fps;
            public float regionalTemperature;
            public string currentWeather;
        }
        
        [Inject]
        public void Construct([Inject] PlayerBus playerBus)
        {
            this.playerBus = playerBus;
            playerBus.hotbarIndexChanged += PlayerBusOnhotbarIndexChanged;
            playerBus.hotbarActionSet += PlayerBusOnhotbarActionSet;
            playerBus.OnLevelUp += PlayerBusOnLevelUp;
            playerBus.OnExperienceChanged += PlayerBusOnExperienceChanged;
            playerBus.OnStatsChanged += PlayerBusOnStatsChanged;
        }

        private void OnDestroy()
        {
            if (playerBus == null)
                return;
            playerBus.hotbarIndexChanged -= PlayerBusOnhotbarIndexChanged;
            playerBus.hotbarActionSet -= PlayerBusOnhotbarActionSet;
            playerBus.OnLevelUp -= PlayerBusOnLevelUp;
            playerBus.OnExperienceChanged -= PlayerBusOnExperienceChanged;
            playerBus.OnStatsChanged -= PlayerBusOnStatsChanged;
        }

        private void PlayerBusOnLevelUp(int newLevel, int statPointsAwarded)
        {
            RefreshProgressionUI();
        }

        private void PlayerBusOnExperienceChanged(
            int level,
            int experience,
            int experienceToNextLevel)
        {
            RefreshProgressionUI();
        }

        private void PlayerBusOnStatsChanged()
        {
            RefreshProgressionUI();
        }

        private void PlayerBusOnhotbarActionSet(int index, IHotbarAction action)
        {
            if (index < 0 || index >= hotbarSlots.Length ||
                hotbarSlots[index] == null)
                return;

            hotbarSlots[index].Action = action;
        }

        private void PlayerBusOnhotbarIndexChanged(int index, IHotbarAction[] hotbarActions)
        {
            HandleHotBar(index);
        }

        private void HandleHotBar(int hotbarPressed)
        {
            for(int i = 0; i < hotbarSlots.Length; i++)
            {
                if (hotbarSlots[i] != null)
                    hotbarSlots[i].Active = hotbarPressed == i;
            }
        }

        private Fps fps;
        private Camera mainCamera;

        private void Awake()
        {
            _uiDocument = GetComponent<PanelRenderer>();
            toolbarController =
                playerDataController.GetComponent<PlayerToolbarController>();
            needsController =
                playerDataController.GetComponent<PlayerNeedsController>();
            fps = GetComponent<Fps>();
            mainCamera = Camera.main;
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            _uiDocument.RegisterUIReloadCallback(ReloadCallback);
        }

        private int pollingRate = 20;
        private int poll = 0;

        private void Update()
        {
            RefreshResourceUI();
            poll--;
            if (poll <= 0)
            {
                if (mainCamera == null)
                    mainCamera = Camera.main;
                if (mainCamera == null)
                    return;

                var screenMouse = inputManager.MousePosition;
                var mousePosition = new Vector3(screenMouse.x, screenMouse.y, -10);
                var worldMouse = mainCamera.ScreenToWorldPoint(mousePosition);

                var position = gameGrid.WorldToCell(worldMouse);

                var tileSample = worldGeneration.GetTerrainSample(position.x, position.y);

                int chunkX = Mathf.FloorToInt(worldMouse.x / ChunkBuildResult.ChunkSize);
                int chunkY = Mathf.FloorToInt(worldMouse.y / ChunkBuildResult.ChunkSize);


                debugData.biome = tileSample.biome.biomeName;
                debugData.continentalness = tileSample.height;
                debugData.temperature = tileSample.temperature;
                debugData.moisture = tileSample.moisture;
                debugData.chunkPosition = new Vector2Int(chunkX, chunkY);
                debugData.cursorPosition = new Vector2Int(position.x, position.y);
                debugData.fps = fps.FrameRate;

                WeatherSample sample =
                    regionalWeatherService.Sample(worldMouse);
                debugData.regionalTemperature =
                    ToCelsius(sample.AmbientTemperature);
                debugData.currentWeather =
                    $"{sample.WeatherId} ({sample.PhaseId}, {sample.Intensity})";

                poll = pollingRate;
            }
        }

        private static float ToCelsius(float normalizedTemperature)
        {
            // The simulation uses -1..1: zero is the freezing/snow threshold,
            // -1 is lethally cold, and 0.5 represents a comfortable warm day.
            return normalizedTemperature < 0f
                ? normalizedTemperature * 20f
                : normalizedTemperature * 42f;
        }

        private void ReloadCallback(PanelRenderer panel, VisualElement root)
        {
            root.Q("NeedsDisplay").dataSource = playerDataController;
            toolbarController ??=
                playerDataController.GetComponent<PlayerToolbarController>();

            var hotbar = root.Q("Toolbar");
            hotbar.Clear();
            
            root.Q("DebugPanel").dataSource = debugData;
            
            for (int i = 0; i < hotbarSlots.Length; i++)
            {
                var slot = new HotbarSlot();
                slot.Action = toolbarController.HotbarActions[i];
                
                hotbarSlots[i] = slot;

                hotbar.Add(slot);
            }

            HandleHotBar(toolbarController.SelectedIndex);
            BindProgressionUI(root);
            RefreshProgressionUI();
        }

        private void BindProgressionUI(VisualElement root)
        {
            healthBar = root.Q<ProgressBar>("HealthBar");
            manaBar = root.Q<ProgressBar>("ManaBar");
            hungerBar = root.Q<ProgressBar>("HungerBar");
            energyBar = root.Q<ProgressBar>("EnergyBar");
            levelLabel = root.Q<Label>("LevelLabel");
            experienceLabel = root.Q<Label>("ExperienceLabel");
            experienceBar = root.Q<ProgressBar>("ExperienceBar");
            levelUpPanel = root.Q("LevelUpPanel");
            statPointsLabel = root.Q<Label>("StatPointsLabel");

            statValueLabels.Clear();
            statButtons.Clear();
            ConfigureHudPicking(root);
            BindStat(root, PlayerStat.Strength, "Strength");
            BindStat(root, PlayerStat.Constitution, "Constitution");
            BindStat(root, PlayerStat.Dexterity, "Dexterity");
            BindStat(root, PlayerStat.Wisdom, "Wisdom");
            BindStat(root, PlayerStat.Intelligence, "Intelligence");
            BindStat(root, PlayerStat.Luck, "Luck");
        }

        private void RefreshResourceUI()
        {
            if (playerDataController == null)
                return;

            if (healthBar != null)
            {
                healthBar.highValue = playerDataController.MaxHealth;
                healthBar.value = playerDataController.Health;
                healthBar.title =
                    $"{playerDataController.Health} / " +
                    $"{playerDataController.MaxHealth}";
            }

            if (manaBar != null)
            {
                manaBar.highValue = playerDataController.MaxMana;
                manaBar.value = playerDataController.CurrentMana;
                manaBar.title =
                    $"{playerDataController.CurrentMana} / " +
                    $"{playerDataController.MaxMana}";
            }

            RefreshNormalizedBar(
                hungerBar,
                needsController != null
                    ? needsController.Hunger
                    : playerDataController.Hunger);
            RefreshNormalizedBar(
                energyBar,
                needsController != null
                    ? needsController.Energy
                    : playerDataController.Energy);
        }

        private static void RefreshNormalizedBar(
            ProgressBar progressBar,
            float normalizedValue)
        {
            if (progressBar == null)
                return;

            float value = Mathf.Clamp01(normalizedValue);
            progressBar.lowValue = 0f;
            progressBar.highValue = 1f;
            progressBar.value = value;
            progressBar.title = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        private void ConfigureHudPicking(VisualElement root)
        {
            // The debug and status containers cover large portions of the panel.
            // They are presentation-only and must not win pointer hit tests over
            // the level-up controls.
            SetPickingModeRecursive(root.Q("DebugPanel"), PickingMode.Ignore);
            SetPickingModeRecursive(root.Q("NeedsDisplay"), PickingMode.Ignore);
            SetPickingModeRecursive(
                root.Q("ProgressionDisplay"),
                PickingMode.Ignore);
            SetPickingModeRecursive(root.Q("Toolbar"), PickingMode.Ignore);

            if (levelUpPanel == null)
                return;

            levelUpPanel.pickingMode = PickingMode.Position;
            levelUpPanel.BringToFront();
        }

        private static void SetPickingModeRecursive(
            VisualElement element,
            PickingMode pickingMode)
        {
            if (element == null)
                return;

            element.pickingMode = pickingMode;
            foreach (VisualElement child in element.Children())
                SetPickingModeRecursive(child, pickingMode);
        }

        private void BindStat(
            VisualElement root,
            PlayerStat stat,
            string elementPrefix)
        {
            Label valueLabel = root.Q<Label>(elementPrefix + "Value");
            Button addButton = root.Q<Button>(elementPrefix + "Add");
            if (valueLabel != null)
                statValueLabels[stat] = valueLabel;
            if (addButton == null)
                return;

            addButton.pickingMode = PickingMode.Position;
            addButton.focusable = true;
            addButton.clicked += () =>
            {
                if (playerDataController.TrySpendStatPoint(stat))
                    RefreshProgressionUI();
            };
            statButtons.Add(addButton);
        }

        private void RefreshProgressionUI()
        {
            if (playerDataController == null)
                return;

            if (levelLabel != null)
                levelLabel.text = $"Level {playerDataController.Level}";
            if (experienceLabel != null)
            {
                experienceLabel.text =
                    $"{playerDataController.Experience} / " +
                    $"{playerDataController.ExperienceToNextLevel} EXP";
            }
            if (experienceBar != null)
            {
                experienceBar.highValue =
                    playerDataController.ExperienceToNextLevel;
                experienceBar.value = playerDataController.Experience;
            }

            bool hasPoints = playerDataController.UnspentStatPoints > 0;
            if (levelUpPanel != null)
            {
                levelUpPanel.style.display =
                    hasPoints ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (statPointsLabel != null)
            {
                statPointsLabel.text =
                    $"Points remaining: {playerDataController.UnspentStatPoints}";
            }

            foreach (var pair in statValueLabels)
                pair.Value.text = playerDataController.GetStat(pair.Key).ToString();
            foreach (Button button in statButtons)
                button.SetEnabled(hasPoints);

            RefreshResourceUI();
        }
    }
}
