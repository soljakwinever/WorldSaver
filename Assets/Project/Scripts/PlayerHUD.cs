using System;
using System.Collections.Generic;
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
    public class PlayerHUD : MonoBehaviour, IWorldActionUiBlocker, ISenseService
    {
        private PanelRenderer _uiDocument;
    
        [Inject] private PlayerDataController playerDataController;
        [Inject] private WorldGeneration worldGeneration;
        [Inject] private IInputManager inputManager;
        [Inject] private IRegionalWeatherService regionalWeatherService;
        [Inject] private Grid gameGrid;
        [Inject] private SkillCatalog skillCatalog;
        [Inject] private MapSignalBus mapSignalBus;
        [Inject] private Chunkloader chunkloader;
        [Inject] private WorldTilemapRenderer worldTilemapRenderer;
        
        private PlayerToolbarController toolbarController;
        private PlayerNeedsController needsController;
        private PlayerBus playerBus;
        private PlayerMenuController playerMenu;
        private WorldMapScreenController worldMapScreen;
        private UniversalToolTip universalToolTip;
        private InventoryDebugUI inventoryMenu;
        private VisualElement senseLayer;
        private readonly System.Collections.Generic.List<SenseIndicator> senseIndicators = new();
        private float senseExpiresAt;
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

        private sealed class SenseIndicator
        {
            public IFeatureData Feature;
            public VisualElement View;
        }
        
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
            ClearSenseIndicators();
            playerMenu?.Dispose();
            worldMapScreen?.Dispose();
            universalToolTip?.Dispose();
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
            inventoryMenu = FindFirstObjectByType<InventoryDebugUI>();
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
            worldMapScreen?.PollKeyboard();
            playerMenu?.PollKeyboard();
            RefreshResourceUI();
            UpdateSenseIndicators();
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
            playerMenu?.Dispose();
            worldMapScreen?.Dispose();
            universalToolTip?.Dispose();
            universalToolTip = new UniversalToolTip(root);
            CreateSenseLayer(root);
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
            playerMenu = new PlayerMenuController(
                root,
                playerDataController,
                skillCatalog,
                inputManager);
            worldMapScreen = new WorldMapScreenController(
                root,
                mapSignalBus,
                chunkloader,
                worldTilemapRenderer,
                playerDataController,
                worldGeneration);
        }

        public bool CanSense(GameObject user) =>
            user != null && worldGeneration is IFeatureSenseSource &&
            senseLayer?.panel != null;

        public void Reveal(GameObject user, float radius, float duration)
        {
            if (!CanSense(user) || radius <= 0f || duration <= 0f)
                return;

            ClearSenseIndicators();
            IReadOnlyList<IFeatureData> features =
                ((IFeatureSenseSource)worldGeneration).FindFeatures(
                    user.transform.position,
                    radius);
            foreach (IFeatureData feature in features)
            {
                if (feature == null)
                    continue;
                VisualElement view = CreateSenseIndicator(feature);
                senseLayer.Add(view);
                senseIndicators.Add(new SenseIndicator
                {
                    Feature = feature,
                    View = view
                });
            }
            senseExpiresAt = Time.time + duration;
            UpdateSenseIndicators();
        }

        private void CreateSenseLayer(VisualElement root)
        {
            ClearSenseIndicators();
            senseLayer?.RemoveFromHierarchy();
            senseLayer = new VisualElement
            {
                name = "SenseIndicators",
                pickingMode = PickingMode.Ignore
            };
            senseLayer.style.position = Position.Absolute;
            senseLayer.style.left = 0f;
            senseLayer.style.right = 0f;
            senseLayer.style.top = 0f;
            senseLayer.style.bottom = 0f;
            root.Add(senseLayer);
            senseLayer.BringToFront();
        }

        private static VisualElement CreateSenseIndicator(IFeatureData feature)
        {
            var view = new VisualElement { pickingMode = PickingMode.Ignore };
            view.style.position = Position.Absolute;
            view.style.alignItems = Align.Center;
            view.style.width = 96f;

            if (feature.Icon != null)
            {
                var icon = new Image
                {
                    sprite = feature.Icon,
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore
                };
                icon.style.width = 36f;
                icon.style.height = 36f;
                view.Add(icon);
            }
            else
            {
                var marker = new Label("◆") { pickingMode = PickingMode.Ignore };
                marker.style.fontSize = 24f;
                marker.style.color = new Color(0.55f, 0.9f, 1f);
                view.Add(marker);
            }

            if (!string.IsNullOrWhiteSpace(feature.Name))
            {
                var label = new Label(feature.Name) { pickingMode = PickingMode.Ignore };
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.color = Color.white;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                view.Add(label);
            }
            return view;
        }

        private void UpdateSenseIndicators()
        {
            if (senseIndicators.Count == 0)
                return;
            if (Time.time >= senseExpiresAt)
            {
                ClearSenseIndicators();
                return;
            }
            mainCamera ??= Camera.main;
            if (mainCamera == null || senseLayer?.panel == null)
                return;

            const float margin = 52f;
            foreach (SenseIndicator indicator in senseIndicators)
            {
                Vector3 projected = mainCamera.WorldToScreenPoint(indicator.Feature.Position);
                Vector2 edgeScreen = GetSenseEdgeScreenPosition(
                    projected,
                    new Vector2(Screen.width, Screen.height),
                    margin);
                Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
                    senseLayer.panel,
                    edgeScreen);
                indicator.View.style.left = panelPosition.x - 48f;
                indicator.View.style.top = panelPosition.y - 28f;
            }
        }

        public static Vector2 GetSenseEdgeScreenPosition(
            Vector2 featureScreenPosition,
            Vector2 screenSize,
            float margin)
        {
            Vector2 center = screenSize * 0.5f;
            Vector2 half = new(
                Mathf.Max(1f, center.x - Mathf.Max(0f, margin)),
                Mathf.Max(1f, center.y - Mathf.Max(0f, margin)));
            Vector2 direction = featureScreenPosition - center;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.up;
            float scale = Mathf.Min(
                half.x / Mathf.Max(0.0001f, Mathf.Abs(direction.x)),
                half.y / Mathf.Max(0.0001f, Mathf.Abs(direction.y)));
            return center + direction * scale;
        }

        private void ClearSenseIndicators()
        {
            foreach (SenseIndicator indicator in senseIndicators)
                indicator.View?.RemoveFromHierarchy();
            senseIndicators.Clear();
            senseExpiresAt = 0f;
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

            RefreshCapacityBar(
                hungerBar,
                needsController != null ? needsController.Hunger : playerDataController.Hunger,
                playerDataController.MaxHunger);
            RefreshCapacityBar(
                energyBar,
                needsController != null ? needsController.Energy : playerDataController.Energy,
                playerDataController.MaxEnergy);
        }

        private static void RefreshCapacityBar(
            ProgressBar progressBar,
            float normalizedValue,
            int maximum)
        {
            if (progressBar == null)
                return;
            float value = Mathf.Clamp01(normalizedValue);
            int safeMaximum = Mathf.Max(1, maximum);
            progressBar.lowValue = 0f;
            progressBar.highValue = safeMaximum;
            progressBar.value = Mathf.RoundToInt(value * safeMaximum);
            progressBar.title =
                $"{Mathf.RoundToInt(value * safeMaximum)} / {safeMaximum}";
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
            VisualElement toolbar = root.Q("Toolbar");
            if (toolbar != null)
                toolbar.pickingMode = PickingMode.Ignore;

            if (levelUpPanel == null)
                return;

            levelUpPanel.pickingMode = PickingMode.Position;
            levelUpPanel.BringToFront();
        }

        public bool IsPointerOverBlockingUi(Vector2 screenPosition)
        {
            inventoryMenu ??= FindFirstObjectByType<InventoryDebugUI>();
            if (inventoryMenu?.IsPointerOverBlockingUi(screenPosition) == true)
                return true;
            if (playerMenu?.IsVisible == true)
                return true;
            if (worldMapScreen?.IsVisible == true)
                return true;
            if (levelUpPanel == null || levelUpPanel.panel == null ||
                levelUpPanel.resolvedStyle.display == DisplayStyle.None)
            {
                return false;
            }

            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
                levelUpPanel.panel,
                screenPosition);
            return levelUpPanel.worldBound.Contains(panelPosition);
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
