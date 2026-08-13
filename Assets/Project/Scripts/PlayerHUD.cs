using System;
using System.Collections.Generic;
using Project.Scripts.Bus;
using Project.Scripts.Core;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.DataTypes;
using Project.Scripts.Entities;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using Project.Scripts.TimeAndWeather;
using Project.Scripts.UI;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
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
        [Inject] private ItemCatalog itemCatalog;
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
        private VisualElement villagerHoverHud;
        private Label villagerHoverName;
        private Label villagerHoverNeeds;
        private Label villagerHoverJob;
        private Label villagerHoverStep;
        private Button townBuildButton;
        private VisualElement townBuildPanel;
        private ScrollView townBuildPalette;
        private VisualElement townBuildQueue;
        private Label townBuildStatus;
        private VisualElement townBuildMainView;
        private VisualElement townBedsView;
        private ScrollView townBedsList;
        private TownCore activeBuildTown;
        private TownConstructionQueue activeConstructionQueue;
        private ItemData selectedBuildItem;
        private bool townBuildModeActive;
        private Vector2Int lastPaintedBuildCell = new(int.MinValue, int.MinValue);
        private int placementArmedFrame;
        private float nextBuildUiRefresh;
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
            UpdateVillagerHoverHud();
            UpdateTownBuildUi();
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
            CreateVillagerHoverHud(root);
            CreateTownBuildUi(root);
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

        private void CreateVillagerHoverHud(VisualElement root)
        {
            villagerHoverHud?.RemoveFromHierarchy();
            villagerHoverHud = new VisualElement
            {
                name = "VillagerHoverHud",
                pickingMode = PickingMode.Ignore
            };
            villagerHoverHud.style.position = Position.Absolute;
            villagerHoverHud.style.display = DisplayStyle.None;
            villagerHoverHud.style.width = 230f;
            villagerHoverHud.style.paddingLeft = 12f;
            villagerHoverHud.style.paddingRight = 12f;
            villagerHoverHud.style.paddingTop = 9f;
            villagerHoverHud.style.paddingBottom = 9f;
            villagerHoverHud.style.backgroundColor = new Color(0.055f, 0.07f, 0.08f, 0.94f);
            villagerHoverHud.style.borderTopLeftRadius = 7f;
            villagerHoverHud.style.borderTopRightRadius = 7f;
            villagerHoverHud.style.borderBottomLeftRadius = 7f;
            villagerHoverHud.style.borderBottomRightRadius = 7f;
            Color border = new(0.55f, 0.72f, 0.6f, 0.9f);
            villagerHoverHud.style.borderLeftColor = border;
            villagerHoverHud.style.borderRightColor = border;
            villagerHoverHud.style.borderTopColor = border;
            villagerHoverHud.style.borderBottomColor = border;
            villagerHoverHud.style.borderLeftWidth = 1f;
            villagerHoverHud.style.borderRightWidth = 1f;
            villagerHoverHud.style.borderTopWidth = 1f;
            villagerHoverHud.style.borderBottomWidth = 1f;

            villagerHoverName = new Label { pickingMode = PickingMode.Ignore };
            villagerHoverName.style.fontSize = 17f;
            villagerHoverName.style.unityFontStyleAndWeight = FontStyle.Bold;
            villagerHoverName.style.color = new Color(0.75f, 1f, 0.78f);
            villagerHoverNeeds = CreateHoverLabel();
            villagerHoverNeeds.style.marginTop = 6f;
            villagerHoverJob = CreateHoverLabel();
            villagerHoverStep = CreateHoverLabel();
            villagerHoverHud.Add(villagerHoverName);
            villagerHoverHud.Add(villagerHoverNeeds);
            villagerHoverHud.Add(villagerHoverJob);
            villagerHoverHud.Add(villagerHoverStep);
            root.Add(villagerHoverHud);
            villagerHoverHud.BringToFront();
        }

        private static Label CreateHoverLabel()
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.fontSize = 13f;
            label.style.color = new Color(0.92f, 0.94f, 0.92f);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private void UpdateVillagerHoverHud()
        {
            if (villagerHoverHud?.panel == null || mainCamera == null)
                return;

            Vector2 screenPosition = inputManager.MousePosition;
            Vector3 worldPosition = mainCamera.ScreenToWorldPoint(screenPosition);
            Collider2D[] hits = Physics2D.OverlapPointAll(worldPosition);
            VillagerEntityBridge hovered = null;
            foreach (Collider2D hit in hits)
            {
                hovered = hit.GetComponentInParent<VillagerEntityBridge>();
                if (hovered != null) break;
            }
            if (hovered == null)
            {
                villagerHoverHud.style.display = DisplayStyle.None;
                return;
            }

            VillagerEntityBridge.HoverHudData data = hovered.GetHoverHudData();
            villagerHoverName.text = data.Name;
            villagerHoverNeeds.text = data.Needs;
            villagerHoverJob.text = $"Job: {data.Job}";
            villagerHoverStep.text = $"Step: {data.Step}";
            villagerHoverHud.style.display = DisplayStyle.Flex;
            villagerHoverHud.BringToFront();

            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
                villagerHoverHud.panel, screenPosition);
            float width = villagerHoverHud.resolvedStyle.width;
            float height = villagerHoverHud.resolvedStyle.height;
            float panelWidth = villagerHoverHud.panel.visualTree.resolvedStyle.width;
            float panelHeight = villagerHoverHud.panel.visualTree.resolvedStyle.height;
            villagerHoverHud.style.left = Mathf.Clamp(panelPosition.x + 18f, 8f,
                Mathf.Max(8f, panelWidth - width - 8f));
            villagerHoverHud.style.top = Mathf.Clamp(panelPosition.y + 18f, 8f,
                Mathf.Max(8f, panelHeight - height - 8f));
        }

        private void CreateTownBuildUi(VisualElement root)
        {
            townBuildButton?.RemoveFromHierarchy();
            townBuildPanel?.RemoveFromHierarchy();
            townBuildButton = new Button(() =>
            {
                townBuildModeActive = true;
                selectedBuildItem = null;
                townBuildPanel.style.display = DisplayStyle.Flex;
                RefreshTownBuildQueue();
            }) { text = "Build", name = "TownBuildButton" };
            townBuildButton.style.position = Position.Absolute;
            townBuildButton.style.right = 18f;
            townBuildButton.style.bottom = 18f;
            townBuildButton.style.width = 105f;
            townBuildButton.style.height = 38f;
            townBuildButton.style.display = DisplayStyle.None;
            root.Add(townBuildButton);

            townBuildPanel = new VisualElement { name = "TownBuildPanel" };
            townBuildPanel.style.position = Position.Absolute;
            townBuildPanel.style.right = 18f;
            townBuildPanel.style.bottom = 65f;
            townBuildPanel.style.width = 430f;
            townBuildPanel.style.height = 610f;
            townBuildPanel.style.paddingLeft = 12f;
            townBuildPanel.style.paddingRight = 12f;
            townBuildPanel.style.paddingTop = 10f;
            townBuildPanel.style.paddingBottom = 10f;
            townBuildPanel.style.backgroundColor = new Color(0.035f, 0.055f, 0.07f, 0.97f);
            townBuildPanel.style.display = DisplayStyle.None;
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            var title = new Label("Town Construction");
            title.style.fontSize = 20f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1f;
            var close = new Button(ExitTownBuildMode)
                { text = "×" };
            close.style.width = 34f;
            titleRow.Add(title); titleRow.Add(close); townBuildPanel.Add(titleRow);
            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            var buildTab = new Button(() => ShowTownBuildTab(false)) { text = "Build" };
            var bedsTab = new Button(() => ShowTownBuildTab(true)) { text = "Beds" };
            buildTab.style.flexGrow = 1f;
            bedsTab.style.flexGrow = 1f;
            tabs.Add(buildTab);
            tabs.Add(bedsTab);
            townBuildPanel.Add(tabs);

            townBuildMainView = new VisualElement();
            townBuildMainView.Add(new Label("Select an entry, then place it inside the town."));
            var search = new TextField { label = "Search" };
            search.RegisterValueChangedCallback(evt => PopulateBuildPalette(evt.newValue));
            townBuildMainView.Add(search);
            townBuildPalette = new ScrollView(ScrollViewMode.Vertical);
            townBuildPalette.style.height = 260f;
            PopulateBuildPalette(string.Empty);
            townBuildMainView.Add(townBuildPalette);
            var queueTitle = new Label("Construction Queue");
            queueTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            townBuildMainView.Add(queueTitle);
            var queueScroll = new ScrollView(ScrollViewMode.Vertical);
            queueScroll.style.flexGrow = 1f;
            townBuildQueue = new VisualElement();
            queueScroll.Add(townBuildQueue); townBuildMainView.Add(queueScroll);
            townBuildStatus = new Label();
            townBuildStatus.style.whiteSpace = WhiteSpace.Normal;
            townBuildMainView.Add(townBuildStatus);
            townBuildPanel.Add(townBuildMainView);

            townBedsView = new VisualElement();
            townBedsView.style.display = DisplayStyle.None;
            townBedsView.Add(new Label("Assign each indoor bed to one town resident."));
            townBedsList = new ScrollView(ScrollViewMode.Vertical);
            townBedsList.style.flexGrow = 1f;
            townBedsView.Add(townBedsList);
            townBuildPanel.Add(townBedsView);
            root.Add(townBuildPanel);
            townBuildButton.BringToFront(); townBuildPanel.BringToFront();
        }

        private void ShowTownBuildTab(bool beds)
        {
            townBuildMainView.style.display = beds ? DisplayStyle.None : DisplayStyle.Flex;
            townBedsView.style.display = beds ? DisplayStyle.Flex : DisplayStyle.None;
            if (beds) RefreshTownBeds();
            else RefreshTownBuildQueue();
        }

        private void RefreshTownBeds()
        {
            if (townBedsList == null) return;
            townBedsList.Clear();
            if (activeBuildTown == null) return;
            string townId = activeBuildTown.PersistentEntity?.Id.ToString() ?? string.Empty;
            var residents = new List<VillagerEntityBridge>();
            foreach (VillagerEntityBridge villager in VillagerEntityBridge.All)
                if (villager != null && string.Equals(villager.TownId, townId,
                        StringComparison.Ordinal)) residents.Add(villager);

            int bedNumber = 0;
            foreach (BedComponent bed in BedComponent.All)
            {
                if (bed == null || !string.Equals(bed.TownId, townId,
                        StringComparison.Ordinal)) continue;
                bedNumber++;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                string ownerName = "Unassigned";
                int ownerIndex = -1;
                for (int i = 0; i < residents.Count; i++)
                {
                    string id = residents[i].PersistentEntity?.Id.ToString();
                    if (!string.Equals(id, bed.OwnerVillagerId,
                            StringComparison.Ordinal)) continue;
                    ownerIndex = i;
                    ownerName = residents[i].VillagerName;
                    break;
                }
                var label = new Label($"Bed {bedNumber}  {(bed.IsIndoors ? "Indoor" : "Outdoor")}" +
                    (bed.IsOccupied ? "  Occupied" : string.Empty));
                label.style.flexGrow = 1f;
                int capturedIndex = ownerIndex;
                BedComponent capturedBed = bed;
                var assign = new Button(() =>
                {
                    int next = capturedIndex + 1;
                    if (next >= residents.Count) capturedBed.ClearOwner();
                    else capturedBed.TryAssign(residents[next], out _);
                    RefreshTownBeds();
                }) { text = ownerName };
                assign.style.width = 130f;
                assign.SetEnabled(bed.IsIndoors);
                row.Add(label);
                row.Add(assign);
                townBedsList.Add(row);
            }
            if (bedNumber == 0)
                townBedsList.Add(new Label("No completed beds in this town."));
        }

        private void PopulateBuildPalette(string filter)
        {
            if (townBuildPalette == null || itemCatalog == null) return;
            townBuildPalette.Clear();
            foreach (ItemData item in itemCatalog.Items)
            {
                if (item == null ||
                    !item.TryGetActionData(out PlaceTileItemActionData _) &&
                    !item.TryGetActionData(out PlacePersistentNodeItemActionData _)) continue;
                if (!string.IsNullOrWhiteSpace(filter) &&
                    item.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                ItemData captured = item;
                var button = new Button(() =>
                {
                    selectedBuildItem = captured;
                    placementArmedFrame = Time.frameCount + 1;
                    lastPaintedBuildCell = new Vector2Int(int.MinValue, int.MinValue);
                    townBuildPanel.style.display = DisplayStyle.None;
                }) { text = captured.name };
                button.style.height = 34f;
                townBuildPalette.Add(button);
            }
        }

        private void UpdateTownBuildUi()
        {
            if (townBuildButton == null || playerDataController == null) return;
            TownCore found = null; float nearest = float.PositiveInfinity;
            foreach (TownCore town in TownCoreRegistry.All)
            {
                if (town == null || !town.IsAvailable ||
                    !town.ContainsTownPosition(playerDataController.transform.position)) continue;
                float distance = (town.Position - playerDataController.transform.position).sqrMagnitude;
                if (distance < nearest) { found = town; nearest = distance; }
            }
            if (found != activeBuildTown)
            {
                activeBuildTown = found;
                activeConstructionQueue = found != null
                    ? found.GetComponent<TownConstructionQueue>() : null;
                selectedBuildItem = null;
                townBuildModeActive = false;
                townBuildPanel.style.display = DisplayStyle.None;
            }
            townBuildButton.style.display = activeConstructionQueue != null
                ? DisplayStyle.Flex : DisplayStyle.None;
            if (activeConstructionQueue == null) return;
            if (Time.time >= nextBuildUiRefresh &&
                townBuildPanel.resolvedStyle.display != DisplayStyle.None)
            {
                nextBuildUiRefresh = Time.time + 0.5f;
                if (townBedsView?.resolvedStyle.display != DisplayStyle.None)
                    RefreshTownBeds();
                else RefreshTownBuildQueue();
            }
            UpdateBlueprintPlacement();
        }

        private void UpdateBlueprintPlacement()
        {
            if (!townBuildModeActive || Mouse.current == null ||
                mainCamera == null) return;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                ExitTownBuildMode();
                return;
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                Vector2 screen = inputManager.MousePosition;
                if (IsPointerOverTownBuildPanel(screen) ||
                    inventoryMenu?.IsPointerOverBlockingUi(screen) == true ||
                    playerMenu?.IsVisible == true ||
                    worldMapScreen?.IsVisible == true)
                    return;

                Vector3 eraseWorld = mainCamera.ScreenToWorldPoint(screen);
                if (activeConstructionQueue.TryCancelAt(
                        eraseWorld,
                        out TownConstructionQueue.BlueprintRecord erased))
                {
                    string erasedName = itemCatalog.TryGet(
                        erased.ItemId, out ItemData erasedItem)
                        ? erasedItem.name
                        : erased.ItemId;
                    townBuildStatus.text = $"Removed {erasedName} blueprint.";
                    RefreshTownBuildQueue();
                    return;
                }

                selectedBuildItem = null;
                return;
            }

            if (selectedBuildItem == null ||
                Time.frameCount < placementArmedFrame) return;
            bool tile = selectedBuildItem.TryGetActionData(out PlaceTileItemActionData _);
            if (!(tile ? Mouse.current.leftButton.isPressed
                    : Mouse.current.leftButton.wasPressedThisFrame)) return;
            Vector3 world = mainCamera.ScreenToWorldPoint(inputManager.MousePosition);
            Vector2Int cell = Vector2Int.FloorToInt(world);
            if (tile && cell == lastPaintedBuildCell) return;
            if (activeConstructionQueue.TryAdd(selectedBuildItem, world,
                    out _, out string reason))
            {
                lastPaintedBuildCell = cell;
                townBuildStatus.text = $"Queued {selectedBuildItem.name}.";
                if (!tile) selectedBuildItem = null;
            }
            else townBuildStatus.text = reason;
        }

        private void ExitTownBuildMode()
        {
            townBuildModeActive = false;
            selectedBuildItem = null;
            if (townBuildPanel != null)
                townBuildPanel.style.display = DisplayStyle.None;
        }

        private bool IsPointerOverTownBuildPanel(Vector2 screenPosition)
        {
            if (townBuildPanel?.panel == null ||
                townBuildPanel.resolvedStyle.display == DisplayStyle.None)
                return false;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
                townBuildPanel.panel, screenPosition);
            return townBuildPanel.worldBound.Contains(panelPosition);
        }

        private void RefreshTownBuildQueue()
        {
            if (townBuildQueue == null) return;
            townBuildQueue.Clear();
            if (activeConstructionQueue == null) return;
            foreach (TownConstructionQueue.BlueprintRecord blueprint in
                     activeConstructionQueue.Blueprints)
            {
                string id = blueprint.Id;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                string itemName = itemCatalog.TryGet(blueprint.ItemId, out ItemData item)
                    ? item.name : blueprint.ItemId;
                var text = new Label(string.IsNullOrEmpty(blueprint.BlockedReason)
                    ? $"{itemName} — {blueprint.Priority}"
                    : $"{itemName}: {blueprint.BlockedReason}");
                text.style.flexGrow = 1f; text.style.whiteSpace = WhiteSpace.Normal;
                var priority = new Button(() =>
                {
                    TownJobPriority next = blueprint.Priority == TownJobPriority.High
                        ? TownJobPriority.Low : blueprint.Priority + 1;
                    activeConstructionQueue.SetPriority(id, next); RefreshTownBuildQueue();
                }) { text = "Priority" };
                var cancel = new Button(() =>
                { activeConstructionQueue.Cancel(id); RefreshTownBuildQueue(); }) { text = "×" };
                cancel.style.width = 32f;
                row.Add(text); row.Add(priority); row.Add(cancel); townBuildQueue.Add(row);
            }
            if (activeConstructionQueue.Blueprints.Count == 0)
                townBuildQueue.Add(new Label("No construction is queued."));
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
            if (townBuildModeActive)
                return true;
            inventoryMenu ??= FindFirstObjectByType<InventoryDebugUI>();
            if (inventoryMenu?.IsPointerOverBlockingUi(screenPosition) == true)
                return true;
            if (playerMenu?.IsVisible == true)
                return true;
            if (worldMapScreen?.IsVisible == true)
                return true;
            if (townBuildPanel?.panel != null &&
                townBuildPanel.resolvedStyle.display != DisplayStyle.None)
            {
                Vector2 townPanelPosition = RuntimePanelUtils.ScreenToPanel(
                    townBuildPanel.panel, screenPosition);
                if (townBuildPanel.worldBound.Contains(townPanelPosition))
                    return true;
            }
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
