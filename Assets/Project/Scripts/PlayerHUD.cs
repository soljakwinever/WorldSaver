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
        private PlayerBus playerBus;
        
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
            playerBus.hotbarIndexChanged += PlayerBusOnhotbarIndexChanged;
            playerBus.hotbarActionSet += PlayerBusOnhotbarActionSet;
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

                Vector2Int weatherRegion = WorldPartition.ChunkToRegion(
                    new Vector2Int(chunkX, chunkY));
                if (regionalWeatherService.TryGetCachedRegionSample(
                        weatherRegion,
                        out WeatherSample sample))
                {
                    debugData.regionalTemperature =
                        ToCelsius(sample.AmbientTemperature);
                    debugData.currentWeather =
                        $"{sample.WeatherId} ({sample.PhaseId}, {sample.Intensity})";
                }
                else
                {
                    debugData.regionalTemperature = 0f;
                    debugData.currentWeather = "Region climate not sampled";
                }

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
        }
    }
}
