using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Enums;
using Project.Scripts.Interface;
using Project.Scripts.ProceduralGeneration;
using UnityEngine;
using Zenject;


public class WorldGeneration : IWorldGenerator, IFeatureSenseSource
{
    FastNoiseLite continentalNoise;
    FastNoiseLite moistureNoise;
    FastNoiseLite temperatureNoise;
    FastNoiseLite erosionNoise;
    FastNoiseLite peakValleyNoise;
    FastNoiseLite valleyNoise;
    FastNoiseLite roughnessNoise;
    FastNoiseLite grassHeightNoise;
    FastNoiseLite smallPoolsNoise;
    
    private FastNoiseLite outcropNoise;
    private FastNoiseLite outcropEdgeNoise;

    private FastNoiseLite propNoise;
    private FastNoiseLite caveChamberNoise;
    private FastNoiseLite caveWarpNoise;
    private FastNoiseLite caveCrevasseMaskNoise;
    
    FastNoiseLite hillNoise;
    FastNoiseLite bumpNoise;
    FastNoiseLite cliffNoise;
    FastNoiseLite cliffMaskNoise;
    
    FastNoiseLite volcanoNoise;
    FastNoiseLite volcanoRidgeNoise;
    
    private readonly WorldData worldData;
    private readonly WorldGenerationSelection selection;
    private readonly WorldGenerationPresetData preset;
    private readonly ClimateLayerData climateLayer;
    private readonly ElevationLayerData elevationLayer;
    private readonly LakeLayerData lakeLayer;
    private readonly SmallPoolLayerData smallPoolLayer;
    private readonly ValleyLayerData valleyLayer;
    private readonly BiomeMicroTerrainLayerData microTerrainLayer;
    private readonly OutcropLayerData outcropLayer;
    private readonly FeatureCellLayerData featureLayer;
    private readonly SurfaceDetailLayerData surfaceLayer;
    private readonly CaveLayoutLayerData caveLayer;
    private readonly CaveBiomeMapLayerData caveBiomeMapLayer;
    private readonly ITimeController timeController;
    private readonly BiomeData[] biomeLibrary;
    private readonly BiomeData[] terrainBiomeLibrary;
    private readonly BiomeData[] waterBiomeLibrary;
    private readonly Dictionary<long, FeatureInstance> featureInstances = new();
    private readonly object featureInstanceLock = new();
    private readonly Dictionary<Vector2Int, Lazy<Dictionary<string, FeatureInstance>>>
        featureBuildingWinners = new();
    private readonly object featureBuildingWinnerLock = new();
    private readonly Dictionary<RoadKey, Lazy<Vector2[]>> roadPaths = new();
    private readonly Dictionary<RoadBranchKey, Lazy<Vector2[]>> roadBranchPaths = new();
    private readonly object roadPathLock = new();
    private Lazy<Vector2[]> guaranteedTownRoad;
    private readonly Dictionary<long, Lazy<TownLayout>> townLayouts = new();
    private readonly object townLayoutLock = new();
    private readonly object guaranteedTownLock = new();
    private bool guaranteedTownResolved;
    private FeatureInstance guaranteedTown;
    private const int CaveCellBlockSize = 64;
    private const int CaveCellCacheCapacity = 128;
    private const int CaveBiomeSiteCacheCapacity = 1024;
    private readonly Dictionary<long, Lazy<byte[]>> caveCellBlocks = new();
    private readonly Queue<long> caveCellBlockOrder = new();
    private readonly object caveCellBlockLock = new();
    private readonly Dictionary<long, BiomeData> caveBiomeSites = new();
    private readonly Queue<long> caveBiomeSiteOrder = new();
    private readonly object caveBiomeSiteLock = new();
    private readonly object spawnPositionLock = new();
    private readonly int featureNeighborRange;
    private volatile bool hasWorldSpawnPosition;
    private Vector2Int worldSpawnPosition;

    private uint seed;

    public uint Seed => seed;
    public Vector2Int WorldSpawnPosition
    {
        get
        {
            if (hasWorldSpawnPosition)
                return worldSpawnPosition;

            lock (spawnPositionLock)
            {
                if (!hasWorldSpawnPosition)
                {
                    worldSpawnPosition = FindSafeSpawnPosition(
                        minHeight: Mathf.Min(
                            elevationLayer.beachHeight + 0.1f,
                            elevationLayer.mountainHeight),
                        maxHeight: elevationLayer.mountainHeight);
                    hasWorldSpawnPosition = true;
                }
            }

            return worldSpawnPosition;
        }
    }
    public FeatureData WorldSpawnFeature => worldData.worldSpawnFeature;
    public WorldGenerationPresetData Preset => preset;
    public ElevationLayerData Elevation => elevationLayer;
    public bool UsesCaveLayout => caveLayer != null;
    public IReadOnlyList<PropSpawnRule> PropSpawnRules =>
        surfaceLayer.useLegacyWorldPropRules
            ? worldData.propSpawnRules ?? Array.Empty<PropSpawnRule>()
            : surfaceLayer.propSpawnRules ?? Array.Empty<PropSpawnRule>();
    public IReadOnlyList<EnemySpawnRule> EnemySpawnRules =>
        preset.useLegacyWorldNPCSpawnRules
            ? worldData.enemySpawnRules ?? Array.Empty<EnemySpawnRule>()
            : preset.enemySpawnRules ?? Array.Empty<EnemySpawnRule>();
    public bool AllowEventNPCSpawnRules =>
        preset.allowEventNPCSpawnRules;

    public IReadOnlyList<FeatureBuildingData> FeatureBuildings =>
        AllFeatureBuildings
            .OfType<FeatureBuildingData>()
            .Where(building => building.IsConfigured)
            .ToArray() ?? Array.Empty<FeatureBuildingData>();
    public IReadOnlyList<FeatureBuildingData> AllFeatureBuildings =>
        featureLayer.features?
            .OfType<FeatureBuildingData>()
            .Where(building => building != null)
            .ToArray() ?? Array.Empty<FeatureBuildingData>();
    
    public WorldGeneration(WorldData worldData)
        : this(
            worldData,
            new WorldGenerationSelection(
                worldData.seed,
                WorldGenerationPresetDefaults.CreateFromLegacy(worldData)),
            null)
    {
    }

    [Inject]
    public WorldGeneration(
        WorldData worldData,
        WorldGenerationSelection selection,
        ITimeController timeController)
    {
        this.worldData = worldData;
        this.selection = selection ??
            throw new ArgumentNullException(nameof(selection));
        preset = selection.Preset;
        if (!preset.IsComplete)
        {
            throw new InvalidOperationException(
                $"World generation preset '{preset.name}' is missing one or more required layers.");
        }

        climateLayer = preset.climate;
        elevationLayer = preset.elevation;
        lakeLayer = preset.lakes;
        smallPoolLayer = preset.smallPools;
        valleyLayer = preset.valleys;
        microTerrainLayer = preset.microTerrain;
        outcropLayer = preset.outcrops;
        featureLayer = preset.features;
        surfaceLayer = preset.surfaceDetails;
        caveLayer = preset.caveLayout;
        caveBiomeMapLayer = preset.caveBiomeMap;
        this.timeController = timeController;
        seed = unchecked((uint)selection.Seed);
        biomeLibrary = climateLayer.useLegacyResourceBiomes
            ? Resources.LoadAll<BiomeData>("Biomes")
            : climateLayer.biomes ?? Array.Empty<BiomeData>();
        if (biomeLibrary.Length == 0)
            throw new InvalidOperationException($"Preset '{preset.name}' has no biomes.");
        terrainBiomeLibrary = Array.FindAll(
            biomeLibrary,
            biome => biome != null &&
                     biome.biomePlacement != BiomePlacement.WaterOnly);
        waterBiomeLibrary = Array.FindAll(
            biomeLibrary,
            biome => biome != null &&
                     biome.biomePlacement == BiomePlacement.WaterOnly);
        if (terrainBiomeLibrary.Length == 0)
            throw new InvalidOperationException(
                $"Preset '{preset.name}' has no biome capable of shaping land.");
        float largestFeatureRadius = 0f;
        foreach (FeatureData feature in featureLayer.features ?? Array.Empty<FeatureData>())
        {
            if (feature != null)
            {
                largestFeatureRadius = Mathf.Max(
                    largestFeatureRadius,
                    feature.maximumRadius,
                    GetMaximumEntityReach(feature));
            }
        }
        featureNeighborRange = Mathf.Max(
            1,
            Mathf.CeilToInt(
                largestFeatureRadius / Mathf.Max(1, featureLayer.cellSize)) + 1);
        
        continentalNoise = new FastNoiseLite();
        continentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        continentalNoise.SetSeed(145679 + selection.Seed);
        continentalNoise.SetFractalType(FastNoiseLite.FractalType.None);
        
        
        moistureNoise = new FastNoiseLite();
        moistureNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        moistureNoise.SetSeed(645745 + selection.Seed);
        moistureNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        moistureNoise.SetFractalOctaves(3);
        moistureNoise.SetFractalLacunarity(2.720f);
        moistureNoise.SetFractalGain(0.45f);
        
        temperatureNoise = new FastNoiseLite();
        temperatureNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        temperatureNoise.SetSeed(324234 + selection.Seed);
        temperatureNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        temperatureNoise.SetFractalOctaves(3);
        temperatureNoise.SetFractalLacunarity(2.720f);
        temperatureNoise.SetFractalGain(0.45f);
        
        erosionNoise = new FastNoiseLite();
        erosionNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        erosionNoise.SetSeed(877555 + selection.Seed);
        erosionNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        erosionNoise.SetFractalOctaves(3);
        erosionNoise.SetFractalLacunarity(2.720f);
        erosionNoise.SetFractalGain(0.45f);
        
        roughnessNoise = new FastNoiseLite();
        roughnessNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        roughnessNoise.SetSeed(843221 + selection.Seed);
        roughnessNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        roughnessNoise.SetFractalOctaves(2);
        roughnessNoise.SetFractalGain(0.45f);

        // High-frequency FBm produces irregular, rough-edged patches.
        grassHeightNoise = new FastNoiseLite();
        grassHeightNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        grassHeightNoise.SetSeed(314159 + selection.Seed);
        grassHeightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        grassHeightNoise.SetFractalOctaves(5);
        grassHeightNoise.SetFractalLacunarity(2.65f);
        grassHeightNoise.SetFractalGain(0.58f);

        smallPoolsNoise = new FastNoiseLite();
        smallPoolsNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        smallPoolsNoise.SetSeed(271828 + selection.Seed);
        smallPoolsNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        smallPoolsNoise.SetFractalOctaves(3);
        smallPoolsNoise.SetFractalLacunarity(2.2f);
        smallPoolsNoise.SetFractalGain(0.5f);
        
        peakValleyNoise = new FastNoiseLite();
        peakValleyNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        peakValleyNoise.SetSeed(455534 + selection.Seed);
        
        volcanoNoise = new FastNoiseLite();
        volcanoNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        volcanoNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        volcanoNoise.SetFractalOctaves(3);
        volcanoNoise.SetFractalLacunarity(2.1f);
        volcanoNoise.SetFractalGain(0.45f);
        volcanoNoise.SetSeed(676767 + selection.Seed);
        
        volcanoRidgeNoise = new FastNoiseLite();
        volcanoRidgeNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        volcanoRidgeNoise.SetSeed(696969 + selection.Seed);
        volcanoRidgeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        volcanoRidgeNoise.SetFractalOctaves(2);
        
        valleyNoise = new FastNoiseLite();
        valleyNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        valleyNoise.SetFractalType(FastNoiseLite.FractalType.PingPong);
        valleyNoise.SetFractalOctaves(2);
        valleyNoise.SetSeed(969696 + selection.Seed);
        
        hillNoise = new FastNoiseLite();
        hillNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        hillNoise.SetSeed(81231 + selection.Seed);
        hillNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        hillNoise.SetFractalOctaves(3);
        hillNoise.SetFractalLacunarity(2.0f);
        hillNoise.SetFractalGain(0.45f);

        bumpNoise = new FastNoiseLite();
        bumpNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        bumpNoise.SetSeed(55991 + selection.Seed);
        bumpNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        bumpNoise.SetFractalOctaves(2);

        cliffNoise = new FastNoiseLite();
        cliffNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        cliffNoise.SetSeed(77128 + selection.Seed);
        cliffNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        cliffNoise.SetFractalOctaves(2);

        cliffMaskNoise = new FastNoiseLite();
        cliffMaskNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        cliffMaskNoise.SetSeed(91277 + selection.Seed);
        cliffMaskNoise.SetFractalType(FastNoiseLite.FractalType.None);
        
        outcropNoise = new FastNoiseLite();
        outcropNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        outcropNoise.SetSeed(882311 + selection.Seed);
        outcropNoise.SetFractalType(FastNoiseLite.FractalType.PingPong);
        outcropNoise.SetFractalOctaves(3);
        outcropNoise.SetFractalLacunarity(2.0f);
        outcropNoise.SetFractalGain(0.45f);

        outcropEdgeNoise = new FastNoiseLite();
        outcropEdgeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        outcropEdgeNoise.SetSeed(449812 + selection.Seed);
        outcropEdgeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        outcropEdgeNoise.SetFractalOctaves(2);
        outcropEdgeNoise.SetFractalLacunarity(2.0f);
        outcropEdgeNoise.SetFractalGain(0.45f);

        propNoise = new FastNoiseLite(667766 + selection.Seed);

        caveChamberNoise = new FastNoiseLite(741103 + selection.Seed);
        caveChamberNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        caveChamberNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        caveChamberNoise.SetFractalOctaves(3);
        caveWarpNoise = new FastNoiseLite(741107 + selection.Seed);
        caveWarpNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        caveCrevasseMaskNoise = new FastNoiseLite(741109 + selection.Seed);
        caveCrevasseMaskNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
    }

    private float ContinentalNoise(float x, float y) => Mathf.InverseLerp(-0.5f, 0.5f,continentalNoise.GetNoise(x / climateLayer.continentalNoiseScale, y / climateLayer.continentalNoiseScale));
    private float MoistureNoise(float x, float y) => Mathf.InverseLerp(-.5f,.5f,moistureNoise.GetNoise(x / climateLayer.moistureNoiseScale, y / climateLayer.moistureNoiseScale));
    private float TemperatureNoise(float x, float y) => Mathf.InverseLerp(-.5f, .5f,temperatureNoise.GetNoise(x / climateLayer.temperatureNoiseScale, y / climateLayer.temperatureNoiseScale));
    
    private float ErosionNoise(float x, float y) => Mathf.InverseLerp(-1f,1f,erosionNoise.GetNoise(x / elevationLayer.erosionNoiseScale, y / elevationLayer.erosionNoiseScale));

    private float PeakValleyNoise(float x, float y) => Mathf.InverseLerp(-0.5f, .5f,peakValleyNoise.GetNoise(x / elevationLayer.peakValleyNoiseScale, y / elevationLayer.peakValleyNoiseScale));
    private float LakeNoise(float x, float y) => Mathf.InverseLerp(-0.5f, .5f,peakValleyNoise.GetNoise(x / elevationLayer.peakValleyNoiseScale*2, y / elevationLayer.peakValleyNoiseScale*2));
    
        
    private float ValleyRaw(float x, float y)
    {
        return valleyNoise.GetNoise(x / valleyLayer.noiseScale, y / valleyLayer.noiseScale);
    }
    
    private float GetRoughness(float x, float y)
    {
        return roughnessNoise.GetNoise(x / 12f, y / 12f);
    }
    
    public float PropNoise(int worldX, int worldY, float ruleNoiseScale)
    {
        return propNoise.GetNoise(worldX / ruleNoiseScale, worldY / ruleNoiseScale);
    }

    public float GrassHeightNoise(int worldX, int worldY)
    {
        float scale = Mathf.Max(0.01f, surfaceLayer.grassHeightNoiseScale);
        return Mathf.InverseLerp(
            -0.25f,
            0.5f,
            grassHeightNoise.GetNoise(worldX / scale, worldY / scale));
    }

    private int GetTileIndex(float continentalNoise)
    {
        return 0;
    }
    
    public int GetTile(int x, int y, out BiomeBlend biomeData, out float height, out float moisture, out float temperature)
    {
        return GetTile(
            x,
            y,
            out biomeData,
            out height,
            out moisture,
            out temperature,
            out _);
    }

    public int GetTile(
        int x,
        int y,
        out BiomeBlend biomeData,
        out float height,
        out float moisture,
        out float temperature,
        out TileData floorTile)
    {
        
        height = GetHeight(
            x,
            y,
            out biomeData,
            out float baseHeight,
            out moisture,
            out temperature,
            out floorTile);

        if (preset.heightMapDebug)
        {
            height = SelectNoiseLayer(x,y,moisture,temperature, biomeData);
            return 0;
        }

        return GetTileIndex(height);
    }

    public int GetTileForChunk(
        int x,
        int y,
        out BiomeBlend biomeData,
        out float height,
        out float moisture,
        out float temperature,
        out TileData floorTile,
        out TerrainKind terrainKind,
        out ChunkBuildResult.IsCliff cliff)
    {
        TerrainKind? knownCaveKind = caveLayer != null
            ? GetTerrainKind(x, y)
            : null;
        terrainKind = knownCaveKind ?? TerrainKind.Floor;
        height = GetHeight(
            x,
            y,
            out biomeData,
            out _,
            out moisture,
            out temperature,
            out floorTile,
            sampleRadius: -1,
            knownCaveKind: knownCaveKind);
        cliff = caveLayer != null
            ? new ChunkBuildResult.IsCliff(
                terrainKind == TerrainKind.Wall,
                terrainKind == TerrainKind.Wall)
            : default;

        if (preset.heightMapDebug)
        {
            height = SelectNoiseLayer(x, y, moisture, temperature, biomeData);
            return 0;
        }
        return GetTileIndex(height);
    }

    public TerrainSample GetTerrainSample(int x, int y)
        => GetTerrainSample(x, y, includePathData: true);

    public TerrainSample GetTerrainSample(
        int x,
        int y,
        bool includePathData)
    {
        TerrainKind? knownCaveKind = caveLayer != null
            ? GetTerrainKind(x, y)
            : null;
        TerrainKind terrainKind = knownCaveKind ?? TerrainKind.Floor;
        int terrainSampleRadius = caveLayer != null
            ? climateLayer.blendSampleRadius
            : 8;
        float height = GetHeight(
            x,
            y,
            out BiomeBlend biomeData,
            out _,
            out float moisture,
            out float temperature,
            out _,
            terrainSampleRadius,
            knownCaveKind);
        TerrainSample sample = new()
        {
            terrainKind = terrainKind,
            biome = biomeData.dominantBiome,
            biomeBlend = biomeData,
            height = height,
            moisture = moisture,
            temperature = temperature,
                    
            isCliff = terrainKind == TerrainKind.Wall ||
                      caveLayer == null && IsSmallCliff(x,y),
            isWater = terrainKind == TerrainKind.Floor && height <= elevationLayer.waterHeight,
            isRoad = includePathData && IsProceduralRoad(x, y),
            isTrail = false
        };
        if (TryGetTownCell(x, y, out _, out TownCellKind townCell))
        {
            if (includePathData &&
                (townCell == TownCellKind.Street ||
                 townCell == TownCellKind.TownCorePlaza))
                sample.isRoad = true;
            if (townCell == TownCellKind.BuildingWall)
            {
                sample.terrainKind = TerrainKind.Wall;
                sample.isCliff = true;
            }
            else if (townCell != TownCellKind.None)
            {
                sample.terrainKind = TerrainKind.Floor;
                sample.isCliff = false;
                sample.isWater = false;
            }
        }
        return sample;
    }

    private float SelectNoiseLayer(int x, int y, float moisture, float temperature, BiomeBlend biomeData = default)
    {
        float height = Mathf.Lerp(
            elevationLayer.waterHeight,
            elevationLayer.mountainHeight,
            0.5f);
        switch (preset.previewLayer)
        {
            case WorldGenerationPreviewLayer.PeakValley:
                height = PeakValleyNoise(x, y);
                break;
            case WorldGenerationPreviewLayer.Height:
                return GetHeight(x, y, out BiomeBlend _, out float _, out float _, out float _);
            case WorldGenerationPreviewLayer.Lakes:
                height=ApplyLakes(x, y, height, biomeData.lakeStrength);
                break;
            case WorldGenerationPreviewLayer.GrassHeight:
                return GrassHeightNoise(x, y);
            case WorldGenerationPreviewLayer.SmallPools:
                return SmallPoolsNoise(x, y);
            case WorldGenerationPreviewLayer.Moisture:
                return moisture;
            case WorldGenerationPreviewLayer.Temperature:
                return temperature;
        }
        return height;
    }

    /// <summary>
    /// Calculates the height of a terrain at the specified world position, including biome blending and adjustments
    /// for local features like valleys, roughness, and erosion.
    /// </summary>
    /// <param name="x">The x-coordinate of the world position.</param>
    /// <param name="y">The y-coordinate of the world position.</param>
    /// <param name="biomeData">Outputs the blended biome data for the specified location.</param>
    /// <param name="baseHeight">Outputs the unmodified base height of the terrain.</param>
    /// <param name="moisture">Outputs the normalized moisture value at the specified position.</param>
    /// <param name="temperature">Outputs the normalized temperature value at the specified position.</param>
    /// <param name="sampleRadius">The radius used for terrain sampling when blending biome data. Defaults to 2.</param>
    /// <returns>Returns the final computed height of the terrain, normalized between 0 and 1.</returns>
    private float GetHeight(int x, int y, out BiomeBlend biomeData, out float baseHeight, out float moisture, out float temperature, int sampleRadius = -1)
    {
        return GetHeight(
            x,
            y,
            out biomeData,
            out baseHeight,
            out moisture,
            out temperature,
            out _,
            sampleRadius);
    }

    private float GetHeight(
        int x,
        int y,
        out BiomeBlend biomeData,
        out float baseHeight,
        out float moisture,
        out float temperature,
        out TileData floorTile,
        int sampleRadius = -1,
        TerrainKind? knownCaveKind = null,
        bool applyFeatureRecipes = true)
    {
        if (sampleRadius < 0)
            sampleRadius = climateLayer.blendSampleRadius;

        Vector3 blendedValues = caveLayer != null && biomeLibrary.Length == 1
            ? new Vector3(
                biomeLibrary[0].height,
                MoistureNoise(x, y),
                TemperatureNoise(x, y))
            : SampleBlendedTerrainValues(x, y, sampleRadius);

        float height = baseHeight = blendedValues.x;
        moisture = blendedValues.y;
        temperature = blendedValues.z;
        
        biomeData = SelectBiomeBlend(x, y, blendedValues);
        if (caveLayer != null)
        {
            TerrainKind kind = knownCaveKind ?? GetTerrainKind(x, y);
            float caveFloorHeight = caveLayer.floorHeight +
                                    GetRoughness(x, y) * 0.025f;
            BiomeData dominantBiome = biomeData.dominantBiome;
            bool allowsUndergroundLake = dominantBiome != null &&
                                         (dominantBiome.overrideWaterTile != null ||
                                          dominantBiome.overrideBeachTile != null);
            if (kind == TerrainKind.Floor && allowsUndergroundLake)
            {
                caveFloorHeight = ApplyLakes(
                    x,
                    y,
                    caveFloorHeight,
                    biomeData.lakeStrength);
                caveFloorHeight = ApplySmallPools(
                    x,
                    y,
                    caveFloorHeight,
                    biomeData.SmallPoolsStrength);
            }

            height = kind switch
            {
                TerrainKind.Wall => caveLayer.wallHeight,
                TerrainKind.Crevasse => caveLayer.crevasseHeight,
                _ => caveFloorHeight
            };
            floorTile = kind switch
            {
                TerrainKind.Wall => caveLayer.wallTile,
                TerrainKind.Crevasse => caveLayer.crevasseTile,
                _ => null
            };
            moisture = Mathf.Clamp01(moisture);
            temperature = Mathf.Clamp01(temperature);
            SeasonalBiomeTint.Apply(
                ref biomeData,
                worldData,
                timeController,
                x,
                y);
            return Mathf.Clamp01(height);
        }

        float peakValleyNoise = PeakValleyNoise(x, y);
        float erosionNoise = ErosionNoise(x, y);
        //Local Height Adjustment
        //Less Eroded = More Height
        float mountainStrength = (1f - erosionNoise) * biomeData.mountainStrength;
        float peakHeight = (peakValleyNoise - 0.5f) * 0.35f * mountainStrength;

        float erosionEffect = erosionNoise * 0.08f * biomeData.erosionStrength;
        
        float roughness = GetRoughness(x, y) * 0.08f * biomeData.roughnessStrength;

        height = baseHeight;
        
        //Apply Shaping
        height *= biomeData.heightMultiplier;
        height += biomeData.heightOffset;
        
        height += peakHeight;
        height += roughness;
        height += erosionEffect;

        height = ApplyLakes(x, y, height, biomeData.lakeStrength);
        height = ApplySmallPools(
            x,
            y,
            height,
            biomeData.SmallPoolsStrength);
        
        height = ApplyValleys(x, y, height, biomeData.valleyStrength);
        
        height = ApplyBiomeMicroTerrain(x, y, height, biomeData);
        height = ApplyMountainIslands(x, y, height, biomeData.dominantBiome);

        TerrainGenerationState terrain = new()
        {
            height = height,
            baseHeight = baseHeight,
            moisture = moisture,
            temperature = temperature,
            biomeData = biomeData
        };
        if (applyFeatureRecipes)
            ApplyFeatures(x, y, ref terrain);
        floorTile = terrain.floorTile;
        height = Mathf.InverseLerp(
            elevationLayer.normalizationMinimum,
            elevationLayer.normalizationMaximum,
            terrain.height);
        moisture = Mathf.Clamp01(terrain.moisture);
        temperature = Mathf.Clamp01(terrain.temperature);
        biomeData = terrain.biomeData;
        if (height <= elevationLayer.waterHeight &&
            waterBiomeLibrary.Length > 0)
        {
            // Water biomes decorate water produced by the terrain pass. They
            // cannot lower terrain to satisfy their own placement condition.
            biomeData = BiomeSelector.GetBiomeBlend(
                waterBiomeLibrary,
                baseHeight,
                moisture,
                temperature);
        }
        SeasonalBiomeTint.Apply(
            ref biomeData,
            worldData,
            timeController,
            x,
            y);

        return height;
    }
    
    public ChunkBuildResult.IsCliff IsSmallCliff(int x, int y)
    {
        if (caveLayer != null)
        {
            bool wall = GetTerrainKind(x, y) == TerrainKind.Wall;
            return new ChunkBuildResult.IsCliff(wall, wall);
        }

        GetTile(x, y, out BiomeBlend _, out float h, out float _, out float _);

        GetTile(x + 1, y, out BiomeBlend _, out float hR, out float _, out float _);
        GetTile(x - 1, y, out BiomeBlend _, out float hL, out float _, out float _);
        GetTile(x, y + 1, out BiomeBlend _, out float hU, out float _, out float _);
        GetTile(x, y - 1, out BiomeBlend _, out float hD, out float _, out float _);

        GetTile(x, y + 2, out BiomeBlend _, out float hU2, out float _, out float _);
        
        float highestNeighbor = Mathf.Max(hR, hL, hU, Mathf.Max(hD, hU2));

        // Draw the cliff on the lower tile next to a higher tile.
        float upwardJump = highestNeighbor - h;

        return new ChunkBuildResult.IsCliff(
            upwardJump > elevationLayer.cliffHeight &&
            h > elevationLayer.waterHeight + 0.04f,
            Mathf.Approximately(highestNeighbor, hD) ||
            Mathf.Approximately(highestNeighbor, hU));
    }

    public Vector2Int FindSafeSpawnPosition(
        int searchRadius = 512,
        int maxAttempts = 5000,
        int safetyRadius = 3,
        float minHeight = 0.075f,
        float maxHeight = 0.7f)
    {
        searchRadius = Mathf.Clamp(
            searchRadius,
            1,
            int.MaxValue / 2);
        maxAttempts = Mathf.Max(0, maxAttempts);
        safetyRadius = Mathf.Max(0, safetyRadius);

        Vector2Int bestPosition = Vector2Int.zero;
        float bestScore = float.MinValue;
        bool found = false;
        
        for (int i = 0; i < maxAttempts; i++)
        {
            int x = GetSpawnSearchCoordinate(i, 0xA511E9B3u, searchRadius);
            int y = GetSpawnSearchCoordinate(i, 0x63D83595u, searchRadius);
            
            if(!IsSpawnSafe(x, y, safetyRadius, minHeight, maxHeight))
                continue;
            
            float score = ScoreSpawnPoint(x, y);
            
            if(score > bestScore)
            {
                bestPosition = new Vector2Int(x, y);
                bestScore = score;
                found = true;
            }
        }
        
        if(found)
            return bestPosition;
        
        return FindSpawnPointSpiral(minHeight, maxHeight);
    }

    public bool TryFindSafePortalPosition(
        Vector2Int requested,
        out Vector2Int safePosition,
        int searchRadius = 64,
        int clearanceRadius = 2)
    {
        safePosition = default;
        searchRadius = Mathf.Max(0, searchRadius);
        clearanceRadius = Mathf.Max(0, clearanceRadius);
        for (int radius = 0; radius <= searchRadius; radius++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (TryPortalCandidate(
                        requested.x + x,
                        requested.y + radius,
                        clearanceRadius,
                        out safePosition))
                    return true;
                if (radius > 0 &&
                    TryPortalCandidate(
                        requested.x + x,
                        requested.y - radius,
                        clearanceRadius,
                        out safePosition))
                    return true;
            }
            for (int y = -radius + 1; y < radius; y++)
            {
                if (TryPortalCandidate(
                        requested.x + radius,
                        requested.y + y,
                        clearanceRadius,
                        out safePosition))
                    return true;
                if (radius > 0 &&
                    TryPortalCandidate(
                        requested.x - radius,
                        requested.y + y,
                        clearanceRadius,
                        out safePosition))
                    return true;
            }
        }

        return false;
    }

    public bool TryFindSafePortalPosition(
        PlaneData destinationPlane,
        Vector2Int requestedCell,
        out Vector2Int safeCell,
        int searchRadius = 64,
        int clearanceRadius = 2)
    {
        if (destinationPlane == null ||
            destinationPlane.generationPreset == null)
        {
            safeCell = default;
            return false;
        }

        WorldGeneration destinationGeneration = new(
            worldData,
            new WorldGenerationSelection(
                unchecked((int)Seed),
                destinationPlane.generationPreset),
            timeController);
        return destinationGeneration.TryFindSafePortalPosition(
            requestedCell,
            out safeCell,
            searchRadius,
            clearanceRadius);
    }

    private bool TryPortalCandidate(
        int x,
        int y,
        int clearanceRadius,
        out Vector2Int candidate)
    {
        candidate = default;
        if (!IsPortalAreaSafe(x, y, clearanceRadius))
            return false;
        candidate = new Vector2Int(x, y);
        return true;
    }

    private bool IsPortalAreaSafe(int centerX, int centerY, int radius)
    {
        if (!GetTerrainSample(centerX, centerY).IsWalkable)
            return false;
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (x == 0 && y == 0)
                continue;
            TerrainSample sample = GetTerrainSample(centerX + x, centerY + y);
            if (!sample.IsWalkable)
                return false;
        }
        return true;
    }

    private int GetSpawnSearchCoordinate(
        int attempt,
        uint salt,
        int radius)
    {
        unchecked
        {
            uint value =
                seed ^
                (uint)attempt * 0x9E3779B9u ^
                salt;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;

            uint diameter = (uint)Math.Min(
                (long)radius * 2L,
                uint.MaxValue);
            return (int)((long)(value % diameter) - radius);
        }
    }


    #region Spawn Point Discovery

    private Vector2Int FindSpawnPointSpiral(float minHeight, float maxHeight, int maxRadius = 1024)
    {
        for (int radius = 0; radius < maxRadius; radius++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if(IsValidSpawnHeight(x, radius, minHeight, maxHeight))
                    return new Vector2Int(x, radius);
                if(radius > 0 &&
                   IsValidSpawnHeight(x, -radius, minHeight, maxHeight))
                    return new Vector2Int(x, -radius);
            }

            for (int y = -radius + 1; y < radius; y++)
            {
                if(IsValidSpawnHeight(radius, y, minHeight, maxHeight))
                    return new Vector2Int(radius, y);
                if(radius > 0 &&
                   IsValidSpawnHeight(-radius, y, minHeight, maxHeight))
                    return new Vector2Int(-radius, y);
            }
        }
        
        return Vector2Int.zero;
    }

    private bool IsValidSpawnHeight(
        int x,
        int y,
        float minHeight,
        float maxHeight
    )
    {
        float height = ContinentalNoise(x, y);
        return height > minHeight && height < maxHeight;
    }

    private float ScoreSpawnPoint(int x, int y)
    {
        float height = ContinentalNoise(x, y);
        float moisture = MoistureNoise(x, y);
        float temperature = TemperatureNoise(x, y);
        
        float score = 0f;
        
        score += 1f - Mathf.Abs(height - 0.25f);
        
        //Not too moist or dry
        score += 1f - Mathf.Abs(moisture - 0.5f);
        
        //Not too hot or cold
        score += 1f - Mathf.Abs(temperature - 0.5f);
        
        score += 1f - GetLocalHeightVariance(x, y, 3);
        
        return score;
    }
    
    private bool IsSpawnSafe(int centerX, int centerY, int safetyRadius, float minHeight, float maxHeight)
    {
        for (int y = -safetyRadius; y <= safetyRadius; y++)
        {
            for (int x = -safetyRadius; x <= safetyRadius; x++)
            {
                int sampleX = centerX + x;
                int sampleY = centerY + y;
                
                float height = GetHeight(sampleX, sampleY, out BiomeBlend biomeData, out float _, out float _, out float _, 1);
                
                if (height < minHeight || height > maxHeight)
                    return false;
            }
        }
        return true;
    }
    
    private float GetLocalHeightVariance(int centerX, int centerY, int radius)
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                float height = GetHeight(centerX + x, centerY + y, out BiomeBlend biomeData, out float _, out float _, out float _, 1);

                if (height < min)
                    min = height;

                if (height > max)
                    max = height;
            }
        }

        return max - min;
    }

    #endregion
    
    private Vector3 SampleBlendedTerrainValues(int worldX, int worldY, int radius)
    {
        // With radius one, every non-center sample lies exactly on the edge and
        // receives zero weight. Return the mathematically identical center
        // sample without evaluating four discarded sets of climate noise.
        if (radius <= 1)
        {
            return new Vector3(
                ContinentalNoise(worldX, worldY),
                MoistureNoise(worldX, worldY),
                TemperatureNoise(worldX, worldY));
        }

        float totalHeight = 0f;
        float totalMoisture = 0f;
        float totalTemperature = 0f;
        float totalWeight = 0f;

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                int sampleX = worldX + x;
                int sampleY = worldY + y;

                float distance = Mathf.Sqrt(x * x + y * y);

                if (distance > radius)
                    continue;

                // Higher weight near the center, lower weight farther away.
                float weight = 1f - (distance / radius);
                weight = Mathf.SmoothStep(0f, 1f, weight);

                float height = ContinentalNoise(sampleX, sampleY);
                float moisture = MoistureNoise(sampleX, sampleY);
                float temperature = TemperatureNoise(sampleX, sampleY);

                totalHeight += height * weight;
                totalMoisture += moisture * weight;
                totalTemperature += temperature * weight;

                totalWeight += weight;
            }
        }

        return new Vector3(
            totalHeight / totalWeight,
            totalMoisture / totalWeight,
            totalTemperature / totalWeight
        );
    }

    private BiomeBlend SelectBiomeBlend(
        int worldX,
        int worldY,
        Vector3 climate)
    {
        if (caveLayer == null || caveBiomeMapLayer == null)
        {
            return BiomeSelector.GetBiomeBlend(
                terrainBiomeLibrary,
                climate.x,
                climate.y,
                climate.z);
        }

        return SelectCellularCaveBiome(worldX, worldY);
    }

    private BiomeBlend SelectCellularCaveBiome(int worldX, int worldY)
    {
        int cellSize = Mathf.Max(32, caveBiomeMapLayer.cellSize);
        int baseCellX = FloorDiv(worldX, cellSize);
        int baseCellY = FloorDiv(worldY, cellSize);
        float nearestDistance = float.MaxValue;
        float secondDistance = float.MaxValue;
        Vector2Int nearestCell = default;
        Vector2Int secondCell = default;
        Vector2 nearestSite = default;
        Vector2 secondSite = default;

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        for (int offsetX = -1; offsetX <= 1; offsetX++)
        {
            int cellX = baseCellX + offsetX;
            int cellY = baseCellY + offsetY;
            Vector2 site = GetCaveBiomeSite(cellX, cellY, cellSize);
            float distance = new Vector2(
                worldX - site.x,
                worldY - site.y).sqrMagnitude;

            if (distance < nearestDistance)
            {
                secondDistance = nearestDistance;
                secondCell = nearestCell;
                secondSite = nearestSite;
                nearestDistance = distance;
                nearestCell = new Vector2Int(cellX, cellY);
                nearestSite = site;
            }
            else if (distance < secondDistance)
            {
                secondDistance = distance;
                secondCell = new Vector2Int(cellX, cellY);
                secondSite = site;
            }
        }

        BiomeData nearestBiome = SelectCaveSiteBiome(
            nearestCell.x,
            nearestCell.y,
            nearestSite);
        BiomeData secondBiome = SelectCaveSiteBiome(
            secondCell.x,
            secondCell.y,
            secondSite);
        if (nearestBiome == secondBiome || secondBiome == null)
            return BiomeSelector.CreateSingleBiomeBlend(nearestBiome);

        float blendWidth = Mathf.Max(0f, caveBiomeMapLayer.edgeBlendWidth);
        if (blendWidth <= 0f)
            return BiomeSelector.CreateSingleBiomeBlend(nearestBiome);

        float edgeDistance = Mathf.Max(
            0f,
            Mathf.Sqrt(secondDistance) - Mathf.Sqrt(nearestDistance));
        float nearestWeight = 0.5f +
                              0.5f * Mathf.SmoothStep(
                                  0f,
                                  1f,
                                  Mathf.Clamp01(edgeDistance / blendWidth));
        return BiomeSelector.BlendBiomes(
            secondBiome,
            nearestBiome,
            nearestWeight);
    }

    private Vector2 GetCaveBiomeSite(int cellX, int cellY, int cellSize)
    {
        int salt = unchecked((int)seed) ^ caveBiomeMapLayer.seedOffset;
        float jitter = Mathf.Clamp(caveBiomeMapLayer.siteJitter, 0f, 0.45f);
        float jitterX = (Util.Hash01(cellX, cellY, salt ^ 0x2f6e2b1) - 0.5f) *
                        2f * jitter;
        float jitterY = (Util.Hash01(cellX, cellY, salt ^ 0x68bc91d) - 0.5f) *
                        2f * jitter;
        return new Vector2(
            (cellX + 0.5f + jitterX) * cellSize,
            (cellY + 0.5f + jitterY) * cellSize);
    }

    private BiomeData SelectCaveSiteBiome(
        int cellX,
        int cellY,
        Vector2 site)
    {
        if (biomeLibrary.Length == 1)
            return biomeLibrary[0];

        long siteKey = PackCoordinates(cellX, cellY);
        lock (caveBiomeSiteLock)
        {
            if (caveBiomeSites.TryGetValue(siteKey, out BiomeData cached))
                return cached;
        }

        int sampleX = Mathf.FloorToInt(site.x);
        int sampleY = Mathf.FloorToInt(site.y);
        float continentalness = ContinentalNoise(sampleX, sampleY);
        float moisture = MoistureNoise(sampleX, sampleY);
        float temperature = TemperatureNoise(sampleX, sampleY);
        float climateInfluence = Mathf.Clamp01(
            caveBiomeMapLayer.climateInfluence);
        float totalWeight = 0f;

        for (int i = 0; i < biomeLibrary.Length; i++)
        {
            BiomeData biome = biomeLibrary[i];
            if (biome == null)
                continue;

            float suitability = 1f / (1f + BiomeSelector.GetBiomeDistance(
                biome,
                continentalness,
                moisture,
                temperature));
            totalWeight += Mathf.Lerp(1f, suitability, climateInfluence);
        }

        if (totalWeight <= 0f)
            return biomeLibrary[0];

        int salt = unchecked((int)seed) ^
                   caveBiomeMapLayer.seedOffset ^
                   0x17c7a53;
        float roll = Util.Hash01(cellX, cellY, salt) * totalWeight;
        BiomeData fallback = biomeLibrary[0];
        for (int i = 0; i < biomeLibrary.Length; i++)
        {
            BiomeData biome = biomeLibrary[i];
            if (biome == null)
                continue;

            fallback = biome;
            float suitability = 1f / (1f + BiomeSelector.GetBiomeDistance(
                biome,
                continentalness,
                moisture,
                temperature));
            roll -= Mathf.Lerp(1f, suitability, climateInfluence);
            if (roll <= 0f)
                return CacheCaveSiteBiome(siteKey, biome);
        }

        return CacheCaveSiteBiome(siteKey, fallback);
    }

    private BiomeData CacheCaveSiteBiome(long siteKey, BiomeData biome)
    {
        lock (caveBiomeSiteLock)
        {
            if (caveBiomeSites.TryGetValue(siteKey, out BiomeData cached))
                return cached;

            caveBiomeSites.Add(siteKey, biome);
            caveBiomeSiteOrder.Enqueue(siteKey);
            while (caveBiomeSites.Count > CaveBiomeSiteCacheCapacity)
            {
                long oldest = caveBiomeSiteOrder.Dequeue();
                caveBiomeSites.Remove(oldest);
            }

            return biome;
        }
    }

    #region Noise Generation
    
    private float ApplyLakes(int x, int y, float height, float lakeStrength)
    {
        float landMask = SmoothStep(
            elevationLayer.beachHeight + 0.1f,
            elevationLayer.mountainHeight - 0.1f,
            height);

        var lakeNoise = Mathf.InverseLerp(
            -1f,
            1f,
            peakValleyNoise.GetNoise(
                x / lakeLayer.noiseScale,
                y / lakeLayer.noiseScale));
        
        float lake = Mathf.Lerp(
            -(lakeLayer.depth * lakeStrength),
            0,
            lakeNoise);
        
        height += lake * landMask;
        
        return height;
    }

    private float SmallPoolsNoise(int x, int y)
    {
        float scale = Mathf.Max(0.01f, smallPoolLayer.noiseScale);
        float normalized = Mathf.InverseLerp(
            -1f,
            1f,
            smallPoolsNoise.GetNoise(x / scale, y / scale));

        // Concentrate the effect around noise minima to form isolated pools.
        return Mathf.Pow(1f - normalized, 4f);
    }

    private float ApplySmallPools(
        int x,
        int y,
        float height,
        float biomeStrength)
    {
        float depth = SmallPoolsNoise(x, y) *
                      Mathf.Max(0f, smallPoolLayer.strength) *
                      Mathf.Max(0f, biomeStrength);

        // This layer is deliberately subtractive and can never raise terrain.
        return height - depth;
    }

    private float ApplyMountainIslands(
        int x,
        int y,
        float height,
        BiomeData biome)
    {
        // Only allow these on land, not water.
        float landMask = SmoothStep(
            elevationLayer.waterHeight,
            elevationLayer.mountainHeight - 0.1f,
            height);
        
        var noise = outcropNoise.GetNoise(
            x / outcropLayer.noiseScale,
            y / outcropLayer.noiseScale);
        var inter = Mathf.InverseLerp(-1f, 1f, noise);

        if (inter > outcropLayer.threshold)
        {
            height += outcropLayer.strength * biome.cliffStrength * landMask;
        }
        
        return height;
    }
    
    private float ApplyBiomeMicroTerrain(int x, int y, float height, BiomeBlend biomeData)
    {
        var offset = Mathf.Lerp(
            elevationLayer.waterHeight,
            elevationLayer.mountainHeight,
            0.65f);
        float landMask = SmoothStep(elevationLayer.waterHeight, offset, height);
        
        float mountainFade =
            1f - SmoothStep(elevationLayer.mountainHeight, 1, height);
        
        float localTerrainMask = landMask * mountainFade;
        
        if(localTerrainMask <= 0f)
            return height;
        
        float hills = hillNoise.GetNoise(x/biomeData.hillScale, y/biomeData.hillScale);
        
        hills = Mathf.InverseLerp(-1f, 1f, hills);
        hills = hills * 2f - 1f;

        height +=
            hills *
            microTerrainLayer.hillStrength *
            biomeData.hillStrength *
            localTerrainMask;

        float bumps = bumpNoise.GetNoise(
            x / biomeData.bumpScale,
            y / biomeData.bumpScale
        );

        bumps = Mathf.InverseLerp(-1f, 1f, bumps);
        bumps = bumps * 2f - 1f;
        
        height +=
            bumps *
            microTerrainLayer.bumpStrength *
            biomeData.bumpStrength *
            localTerrainMask;
        
        height = ApplySmallCliffs(x,y,height,biomeData, localTerrainMask);
        
        return height;
    }

    private float ApplySmallCliffs(int x, int y, float height, BiomeBlend biomeData, float localTerrainMask)
    {
        if(biomeData.cliffStrength<= 0f || biomeData.cliffChance <= 0f)
            return height;
        
        float cliffMask = cliffMaskNoise.GetNoise(x/biomeData.cliffScale, y/biomeData.cliffScale);
        
        cliffMask = Mathf.InverseLerp(-1f, 1f, cliffMask);
        
        if(cliffMask > biomeData.cliffChance)
            return height;
        
        float cliff = cliffNoise.GetNoise(x/biomeData.cliffScale, y/biomeData.cliffScale);
        
        cliff = Mathf.InverseLerp(-1f, 1f, cliff);

        int steps = 4;
        
        float stepped = Mathf.Floor(cliff * steps) / steps;

        float cliffDelta = stepped - cliff;
        
        height += cliffDelta * biomeData.cliffStrength * localTerrainMask;
        
        return height;
    }

    
    private float ApplyValleys(int x, int y, float height, float biomeValleyStrength)
    {
        float n = Mathf.Abs(ValleyRaw(x, y));

        float valleyMask = 1f - SmoothStep(0.02f, valleyLayer.width, n);

        float highlandMask = SmoothStep(
            elevationLayer.beachHeight,
            elevationLayer.mountainHeight,
            height);

        height -=
            valleyMask *
            highlandMask *
            valleyLayer.depth *
            biomeValleyStrength;

        return height;
    }
    
    private void ApplyFeatures(
        int x,
        int y,
        ref TerrainGenerationState terrain)
    {
        if (featureLayer.chancePerCell > 0f &&
            featureLayer.features != null &&
            featureLayer.features.Length > 0)
        {
            int cellSize = Mathf.Max(1, featureLayer.cellSize);
            int cellX = Mathf.FloorToInt((float)x / cellSize);
            int cellY = Mathf.FloorToInt((float)y / cellSize);

            for (int offsetX = -featureNeighborRange;
                 offsetX <= featureNeighborRange;
                 offsetX++)
            {
                for (int offsetY = -featureNeighborRange;
                     offsetY <= featureNeighborRange;
                     offsetY++)
                {
                    FeatureInstance instance = GetFeatureInstance(
                        cellX + offsetX,
                        cellY + offsetY);
                    if (!instance.exists)
                        continue;

                    ApplyFeatureInstance(x, y, instance, ref terrain);
                }
            }
        }

        FeatureInstance worldSpawn = GetWorldSpawnFeatureInstance();
        if (worldSpawn.exists)
            ApplyFeatureInstance(x, y, worldSpawn, ref terrain);
        FeatureInstance requiredTown = GetGuaranteedTownInstance();
        if (requiredTown.exists)
            ApplyFeatureInstance(x, y, requiredTown, ref terrain);
    }

    private FeatureInstance GetWorldSpawnFeatureInstance()
    {
        FeatureData feature = WorldSpawnFeature;
        if (feature == null || !hasWorldSpawnPosition)
            return default;

        return new FeatureInstance
        {
            exists = true,
            feature = feature,
            center = WorldSpawnPosition,
            radius = Mathf.Max(1f, feature.maximumRadius),
            aspect = Mathf.Max(0.1f, feature.maximumAspect),
            rotation = 0f
        };
    }

    private FeatureInstance GetFeatureInstance(int cellX, int cellY)
    {
        long key = ((long)cellX << 32) ^ (uint)cellY;
        lock (featureInstanceLock)
        {
            if (featureInstances.TryGetValue(key, out FeatureInstance cached))
                return cached;

            FeatureInstance created = CreateFeatureInstance(cellX, cellY);
            featureInstances.Add(key, created);
            return created;
        }
    }

    public bool IsProceduralRoad(int x, int y)
    {
        if (caveLayer != null)
            return false;

        FeatureInstance requiredTown = GetGuaranteedTownInstance();
        if (requiredTown.exists && IsGuaranteedTownRoad(x, y, requiredTown))
            return true;
        if (!featureLayer.generateRoads)
            return false;

        float maximumDistance = Mathf.Max(8f, featureLayer.maximumRoadDistance);
        float width = Mathf.Max(0.5f, featureLayer.roadWidth);
        int cellSize = Mathf.Max(1, featureLayer.cellSize);
        // Include the neighbors of both possible endpoints as well as the
        // A* search margin. Connection limiting must see the same candidate
        // set from every tile along a road, including across chunk borders.
        int range = Mathf.CeilToInt(maximumDistance * 3f / cellSize) + 1;
        int cellX = Mathf.FloorToInt((float)x / cellSize);
        int cellY = Mathf.FloorToInt((float)y / cellSize);
        var nearby = new List<RoadFeature>();

        for (int oy = -range; oy <= range; oy++)
        for (int ox = -range; ox <= range; ox++)
        {
            int candidateX = cellX + ox;
            int candidateY = cellY + oy;
            FeatureInstance instance = GetFeatureInstance(candidateX, candidateY);
            if (instance.exists)
            {
                if (!instance.feature.connectToRoads)
                    continue;
                nearby.Add(new RoadFeature(
                    PackCoordinates(candidateX, candidateY),
                    instance,
                    GetRoadConnectionPoint(instance)));
            }
        }

        FeatureInstance spawn = GetWorldSpawnFeatureInstance();
        if (spawn.exists && spawn.feature.connectToRoads &&
            Vector2.Distance(new Vector2(x, y), spawn.center) <= maximumDistance * 2f)
        {
            nearby.Add(new RoadFeature(
                long.MinValue,
                spawn,
                GetRoadConnectionPoint(spawn)));
        }
        if (requiredTown.exists &&
            Vector2.Distance(new Vector2(x, y), requiredTown.center) <= maximumDistance * 2f)
        {
            nearby.Add(new RoadFeature(
                long.MinValue + 1,
                requiredTown,
                GetRoadConnectionPoint(requiredTown)));
        }

        Vector2 point = new(x, y);
        float maximumDistanceSquared = maximumDistance * maximumDistance;
        List<Vector2[]> paths = GetRoadNetworkPaths(nearby, maximumDistanceSquared);
        for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            Vector2[] path = paths[pathIndex];
            for (int i = 1; i < path.Length; i++)
            {
                if (DistanceToSegment(point, path[i - 1], path[i]) <= width * 0.5f)
                    return true;
            }
        }
        return false;
    }

    public bool IsWithinRestrictedPathArea(int x, int y)
    {
        int clearance = Mathf.Max(0, featureLayer.entityClearanceFromPaths);
        int clearanceSquared = clearance * clearance;
        for (int offsetY = -clearance; offsetY <= clearance; offsetY++)
        for (int offsetX = -clearance; offsetX <= clearance; offsetX++)
        {
            if (offsetX * offsetX + offsetY * offsetY > clearanceSquared)
                continue;
            int sampleX = x + offsetX;
            int sampleY = y + offsetY;
            if (IsProceduralRoad(sampleX, sampleY))
                return true;
            if (TryGetTownCell(
                    sampleX,
                    sampleY,
                    out _,
                    out TownCellKind townCell) &&
                (townCell == TownCellKind.Street ||
                 townCell == TownCellKind.TownCorePlaza))
            {
                return true;
            }
        }
        return false;
    }

    public void BuildRoadMasks(
        Vector2Int chunkPosition,
        bool[] roadMask,
        bool[] restrictedMask)
    {
        if (roadMask == null || roadMask.Length < ChunkBuildResult.ChunkSize * ChunkBuildResult.ChunkSize)
            throw new ArgumentException("Road mask is smaller than a chunk.", nameof(roadMask));
        if (restrictedMask == null || restrictedMask.Length < roadMask.Length)
            throw new ArgumentException("Restricted mask is smaller than the road mask.", nameof(restrictedMask));
        Array.Clear(roadMask, 0, roadMask.Length);
        Array.Clear(restrictedMask, 0, restrictedMask.Length);
        if (caveLayer != null)
            return;

        int size = ChunkBuildResult.ChunkSize;
        int originX = chunkPosition.x * size;
        int originY = chunkPosition.y * size;
        int clearance = Mathf.Max(0, featureLayer.entityClearanceFromPaths);
        float halfWidth = Mathf.Max(0.5f, featureLayer.roadWidth) * 0.5f;
        var paths = new List<Vector2[]>();

        FeatureInstance requiredTown = GetGuaranteedTownInstance();
        if (requiredTown.exists)
        {
            Lazy<Vector2[]> lazy;
            lock (roadPathLock)
            {
                guaranteedTownRoad ??= new Lazy<Vector2[]>(
                    () => BuildRoadPath(WorldSpawnPosition, GetRoadConnectionPoint(requiredTown)),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                lazy = guaranteedTownRoad;
            }
            AddPathIfRelevant(lazy.Value, originX, originY, size, halfWidth + clearance, paths);
        }

        if (featureLayer.generateRoads)
        {
            float maximumDistance = Mathf.Max(8f, featureLayer.maximumRoadDistance);
            float maximumDistanceSquared = maximumDistance * maximumDistance;
            int cellSize = Mathf.Max(1, featureLayer.cellSize);
            float graphMargin = maximumDistance * 3f;
            int minimumCellX = Mathf.FloorToInt((originX - graphMargin) / cellSize);
            int minimumCellY = Mathf.FloorToInt((originY - graphMargin) / cellSize);
            int maximumCellX = Mathf.FloorToInt((originX + size - 1 + graphMargin) / cellSize);
            int maximumCellY = Mathf.FloorToInt((originY + size - 1 + graphMargin) / cellSize);
            var candidates = new List<RoadFeature>();
            for (int cellY = minimumCellY; cellY <= maximumCellY; cellY++)
            for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
            {
                FeatureInstance instance = GetFeatureInstance(cellX, cellY);
                if (instance.exists && instance.feature.connectToRoads)
                    candidates.Add(new RoadFeature(
                        PackCoordinates(cellX, cellY), instance, GetRoadConnectionPoint(instance)));
            }
            FeatureInstance spawn = GetWorldSpawnFeatureInstance();
            if (spawn.exists && spawn.feature.connectToRoads)
                candidates.Add(new RoadFeature(long.MinValue, spawn, GetRoadConnectionPoint(spawn)));
            if (requiredTown.exists)
                candidates.Add(new RoadFeature(long.MinValue + 1, requiredTown, GetRoadConnectionPoint(requiredTown)));

            List<Vector2[]> networkPaths = GetRoadNetworkPaths(candidates, maximumDistanceSquared);
            for (int pathIndex = 0; pathIndex < networkPaths.Count; pathIndex++)
            {
                AddPathIfRelevant(
                    networkPaths[pathIndex],
                    originX,
                    originY,
                    size,
                    halfWidth + clearance,
                    paths);
            }
        }

        for (int localY = 0; localY < size; localY++)
        for (int localX = 0; localX < size; localX++)
        {
            Vector2 point = new(originX + localX, originY + localY);
            float nearest = float.MaxValue;
            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
            {
                Vector2[] path = paths[pathIndex];
                for (int segment = 1; segment < path.Length; segment++)
                    nearest = Mathf.Min(nearest, DistanceToSegment(point, path[segment - 1], path[segment]));
            }
            int index = localX + localY * size;
            roadMask[index] = nearest <= halfWidth;
            restrictedMask[index] = nearest <= halfWidth + clearance;
        }

        // Town streets are already raster data. Visit the small padded chunk
        // once and stamp their clearance into the mask instead of querying all
        // neighboring cells again for every prop candidate.
        int clearanceSquared = clearance * clearance;
        for (int worldY = originY - clearance; worldY < originY + size + clearance; worldY++)
        for (int worldX = originX - clearance; worldX < originX + size + clearance; worldX++)
        {
            if (!TryGetTownCell(worldX, worldY, out _, out TownCellKind kind) ||
                (kind != TownCellKind.Street && kind != TownCellKind.TownCorePlaza))
                continue;
            for (int oy = -clearance; oy <= clearance; oy++)
            for (int ox = -clearance; ox <= clearance; ox++)
            {
                if (ox * ox + oy * oy > clearanceSquared) continue;
                int localX = worldX + ox - originX;
                int localY = worldY + oy - originY;
                if ((uint)localX >= size || (uint)localY >= size) continue;
                restrictedMask[localX + localY * size] = true;
            }
        }
    }

    private static void AddPathIfRelevant(
        Vector2[] path,
        int originX,
        int originY,
        int size,
        float margin,
        List<Vector2[]> destination)
    {
        if (path == null || path.Length < 2) return;
        float maximumX = originX + size - 1;
        float maximumY = originY + size - 1;
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 a = path[i - 1];
            Vector2 b = path[i];
            if (Mathf.Max(a.x, b.x) >= originX - margin &&
                Mathf.Min(a.x, b.x) <= maximumX + margin &&
                Mathf.Max(a.y, b.y) >= originY - margin &&
                Mathf.Min(a.y, b.y) <= maximumY + margin)
            {
                destination.Add(path);
                return;
            }
        }
    }

    private bool IsGuaranteedTownRoad(
        int x,
        int y,
        FeatureInstance town)
    {
        Lazy<Vector2[]> lazy;
        lock (roadPathLock)
        {
            if (guaranteedTownRoad == null)
            {
                Vector2 start = WorldSpawnPosition;
                Vector2 end = GetRoadConnectionPoint(town);
                guaranteedTownRoad = new Lazy<Vector2[]>(
                    () => BuildRoadPath(start, end),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }
            lazy = guaranteedTownRoad;
        }

        Vector2 point = new(x, y);
        float halfWidth = Mathf.Max(0.5f, featureLayer.roadWidth) * 0.5f;
        Vector2[] path = lazy.Value;
        for (int i = 1; i < path.Length; i++)
        {
            if (DistanceToSegment(point, path[i - 1], path[i]) <= halfWidth)
                return true;
        }
        return false;
    }

    private static Vector2 GetRoadConnectionPoint(FeatureInstance instance)
    {
        float sine = Mathf.Sin(instance.rotation);
        float cosine = Mathf.Cos(instance.rotation);
        Vector2 offset = instance.feature.roadConnectionOffset;
        if (instance.feature is TownFeatureData && offset == Vector2.zero)
            offset = Vector2.right * Mathf.Max(1f, instance.radius - 2f);
        Vector2 rotatedOffset = new(
            offset.x * cosine - offset.y * sine,
            offset.x * sine + offset.y * cosine);
        return instance.center + rotatedOffset;
    }

    private static bool CanConnectRoadFeatures(RoadFeature first, RoadFeature second) =>
        AllowsRoadDestination(first.feature, second.feature) &&
        AllowsRoadDestination(second.feature, first.feature);

    private static bool AllowsRoadDestination(FeatureData source, FeatureData destination)
    {
        FeatureData[] allowed = source.allowedRoadConnections;
        if (allowed == null || allowed.Length == 0)
            return true;
        for (int i = 0; i < allowed.Length; i++)
        {
            if (allowed[i] == destination)
                return true;
        }
        return false;
    }

    private static bool IsSelectedRoadConnection(
        RoadFeature source,
        RoadFeature destination,
        List<RoadFeature> candidates,
        float maximumDistanceSquared)
    {
        // Both endpoints must select the edge. This keeps each feature's
        // configured maximum deterministic and prevents the other endpoint
        // from exceeding its own limit.
        return SelectsRoadConnection(source, destination, candidates, maximumDistanceSquared) &&
               SelectsRoadConnection(destination, source, candidates, maximumDistanceSquared);
    }

    private static bool SelectsRoadConnection(
        RoadFeature source,
        RoadFeature destination,
        List<RoadFeature> candidates,
        float maximumDistanceSquared)
    {
        int maximum = source.feature.maximumRoadConnections;
        if (maximum <= 0)
            return true;

        int closer = 0;
        float targetDistance = (source.center - destination.center).sqrMagnitude;
        for (int i = 0; i < candidates.Count; i++)
        {
            RoadFeature candidate = candidates[i];
            if (candidate.id == source.id ||
                !CanConnectRoadFeatures(source, candidate))
                continue;
            float distance = (source.center - candidate.center).sqrMagnitude;
            if (distance > maximumDistanceSquared)
                continue;
            if (distance < targetDistance ||
                Mathf.Approximately(distance, targetDistance) && candidate.id < destination.id)
            {
                closer++;
                if (closer >= maximum)
                    return false;
            }
        }
        return true;
    }

    private Vector2[] GetRoadPath(RoadFeature first, RoadFeature second)
    {
        RoadKey key = new(first.id, second.id);
        Lazy<Vector2[]> lazy;
        lock (roadPathLock)
        {
            if (!roadPaths.TryGetValue(key, out lazy))
            {
                RoadFeature capturedFirst = first;
                RoadFeature capturedSecond = second;
                lazy = new Lazy<Vector2[]>(
                    () => BuildRoadPath(capturedFirst.center, capturedSecond.center),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                roadPaths.Add(key, lazy);
            }
        }
        return lazy.Value;
    }

    private List<Vector2[]> GetRoadNetworkPaths(
        List<RoadFeature> candidates,
        float maximumDistanceSquared)
    {
        candidates.Sort((a, b) => a.id.CompareTo(b.id));
        var routes = new List<RoadRoute>();
        var connected = new HashSet<long>();
        for (int firstIndex = 0; firstIndex < candidates.Count; firstIndex++)
        for (int secondIndex = firstIndex + 1; secondIndex < candidates.Count; secondIndex++)
        {
            RoadFeature first = candidates[firstIndex];
            RoadFeature second = candidates[secondIndex];
            if ((first.center - second.center).sqrMagnitude > maximumDistanceSquared ||
                !CanConnectRoadFeatures(first, second) ||
                !IsSelectedRoadConnection(first, second, candidates, maximumDistanceSquared))
                continue;
            Vector2[] path = GetRoadPath(first, second);
            if (path.Length < 2) continue;
            routes.Add(new RoadRoute(first, second, path));
            connected.Add(first.id);
            connected.Add(second.id);
        }

        if (featureLayer.generateRoadBranches && routes.Count > 0)
        {
            int primaryRouteCount = routes.Count;
            float branchDistance = Mathf.Max(1f, featureLayer.maximumRoadBranchDistance);
            float branchDistanceSquared = branchDistance * branchDistance;
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                RoadFeature candidate = candidates[candidateIndex];
                if (connected.Contains(candidate.id)) continue;
                RoadRoute nearestRoute = default;
                Vector2 nearestPoint = default;
                float nearestDistanceSquared = branchDistanceSquared;
                bool found = false;
                for (int routeIndex = 0; routeIndex < primaryRouteCount; routeIndex++)
                {
                    RoadRoute route = routes[routeIndex];
                    if (route.first.id == candidate.id || route.second.id == candidate.id ||
                        !CanBranchToRoute(candidate, route))
                        continue;
                    for (int segment = 1; segment < route.path.Length; segment++)
                    {
                        Vector2 point = ClosestPointOnSegment(
                            candidate.center,
                            route.path[segment - 1],
                            route.path[segment]);
                        float distanceSquared = (candidate.center - point).sqrMagnitude;
                        if (distanceSquared >= nearestDistanceSquared) continue;
                        nearestDistanceSquared = distanceSquared;
                        nearestRoute = route;
                        nearestPoint = point;
                        found = true;
                    }
                }
                if (!found) continue;
                Vector2[] branch = GetRoadBranchPath(candidate, nearestRoute, nearestPoint);
                if (branch.Length >= 2)
                {
                    routes.Add(new RoadRoute(candidate, nearestRoute.first, branch));
                    connected.Add(candidate.id);
                }
            }
        }

        var paths = new List<Vector2[]>(routes.Count);
        for (int i = 0; i < routes.Count; i++) paths.Add(routes[i].path);
        return paths;
    }

    private static bool CanBranchToRoute(RoadFeature candidate, RoadRoute route) =>
        CanConnectRoadFeatures(candidate, route.first) ||
        CanConnectRoadFeatures(candidate, route.second);

    private Vector2[] GetRoadBranchPath(
        RoadFeature feature,
        RoadRoute route,
        Vector2 attachment)
    {
        RoadBranchKey key = new(feature.id, route.first.id, route.second.id);
        Lazy<Vector2[]> lazy;
        lock (roadPathLock)
        {
            if (!roadBranchPaths.TryGetValue(key, out lazy))
            {
                Vector2 start = feature.center;
                Vector2 end = attachment;
                lazy = new Lazy<Vector2[]>(
                    () => BuildRoadPath(start, end),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                roadBranchPaths.Add(key, lazy);
            }
        }
        return lazy.Value;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon) return start;
        return start + segment * Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
    }

    private Vector2[] BuildRoadPath(Vector2 start, Vector2 end)
    {
        int step = Mathf.Max(1, featureLayer.roadPathStep);
        int waterClearance = Mathf.Max(
            1,
            Mathf.CeilToInt(Mathf.Max(0.5f, featureLayer.roadWidth) * 0.5f));
        var terrainCosts = new Dictionary<Vector2Int, float>();
        float TraversalCost(Vector2 point)
        {
            // Endpoints may sit on authored feature floors. Let A* leave and
            // enter them, then enforce hard terrain restrictions elsewhere.
            if (Vector2.Distance(point, start) <= step ||
                Vector2.Distance(point, end) <= step)
                return 0f;

            int sampleX = Mathf.RoundToInt(point.x);
            int sampleY = Mathf.RoundToInt(point.y);
            Vector2Int sampleCell = new(sampleX, sampleY);
            if (terrainCosts.TryGetValue(sampleCell, out float cachedCost))
                return cachedCost;

            float height = GetRoadPlanningHeight(sampleX, sampleY);
            if (height <= elevationLayer.waterHeight ||
                GetRoadPlanningHeight(sampleX + waterClearance, sampleY) <= elevationLayer.waterHeight ||
                GetRoadPlanningHeight(sampleX - waterClearance, sampleY) <= elevationLayer.waterHeight ||
                GetRoadPlanningHeight(sampleX, sampleY + waterClearance) <= elevationLayer.waterHeight ||
                GetRoadPlanningHeight(sampleX, sampleY - waterClearance) <= elevationLayer.waterHeight ||
                GetRoadPlanningHeight(sampleX + waterClearance, sampleY + waterClearance) <= elevationLayer.waterHeight ||
                GetRoadPlanningHeight(sampleX + waterClearance, sampleY - waterClearance) <= elevationLayer.waterHeight ||
                GetRoadPlanningHeight(sampleX - waterClearance, sampleY + waterClearance) <= elevationLayer.waterHeight ||
                GetRoadPlanningHeight(sampleX - waterClearance, sampleY - waterClearance) <= elevationLayer.waterHeight)
            {
                terrainCosts[sampleCell] = float.PositiveInfinity;
                return float.PositiveInfinity;
            }

            float right = GetRoadPlanningHeight(sampleX + 1, sampleY);
            float left = GetRoadPlanningHeight(sampleX - 1, sampleY);
            float up = GetRoadPlanningHeight(sampleX, sampleY + 1);
            float down = GetRoadPlanningHeight(sampleX, sampleY - 1);
            float highestNeighbor = Mathf.Max(right, left, up, down);
            if (highestNeighbor - height > elevationLayer.cliffHeight &&
                height > elevationLayer.waterHeight + 0.04f)
            {
                terrainCosts[sampleCell] = float.PositiveInfinity;
                return float.PositiveInfinity;
            }
            float slope = Mathf.Max(
                Mathf.Abs(height - right),
                Mathf.Abs(height - left),
                Mathf.Abs(height - up),
                Mathf.Abs(height - down));
            float variation = Util.Hash01(
                Mathf.RoundToInt(point.x / step),
                Mathf.RoundToInt(point.y / step),
                unchecked((int)seed) ^ 0x6a09e667) * 0.35f;
            float cost = slope * featureLayer.roadSlopeCost + variation;
            terrainCosts[sampleCell] = cost;
            return cost;
        }

        Vector2[] path = ProceduralRoadPathfinder.Find(start, end, step, TraversalCost);
        int steeringSeed = unchecked((int)seed) ^
                           Mathf.RoundToInt(start.x * 17f + start.y * 31f) ^
                           Mathf.RoundToInt(end.x * 43f + end.y * 59f);
        bool CanTraverse(Vector2 point)
        {
            float cost = TraversalCost(point);
            return !float.IsInfinity(cost) && !float.IsNaN(cost);
        }
        return ProceduralRoadPathfinder.ApplySteering(
            path,
            featureLayer.roadSteeringWeight,
            featureLayer.roadSteeringPasses,
            featureLayer.roadJitter,
            steeringSeed,
            CanTraverse);
    }

    private float GetRoadPlanningHeight(int x, int y) =>
        GetHeight(
            x,
            y,
            out _,
            out _,
            out _,
            out _,
            out _,
            sampleRadius: 1,
            knownCaveKind: null,
            applyFeatureRecipes: false);

    public void ApplyTownCell(
        int x,
        int y,
        ref TileData floorTile,
        ref TerrainKind terrainKind,
        ref ChunkBuildResult.IsCliff cliff,
        ref bool isRoad)
    {
        if (!TryGetTownCell(x, y, out TownFeatureData town, out TownCellKind kind))
            return;

        switch (kind)
        {
            case TownCellKind.Street:
                floorTile = town.streetTile ?? floorTile;
                terrainKind = TerrainKind.Floor;
                cliff = default;
                isRoad = true;
                break;
            case TownCellKind.TownCorePlaza:
                floorTile = town.townCorePlazaTile ?? town.streetTile ?? floorTile;
                terrainKind = TerrainKind.Floor;
                cliff = default;
                isRoad = true;
                break;
            case TownCellKind.BuildingFloor:
            case TownCellKind.Door:
                floorTile = town.buildingFloorTile ?? floorTile;
                terrainKind = TerrainKind.Floor;
                cliff = default;
                break;
            case TownCellKind.AlternateLot:
                floorTile = town.alternateLotTile ?? town.buildingFloorTile ?? floorTile;
                terrainKind = TerrainKind.Floor;
                cliff = default;
                break;
            case TownCellKind.BuildingWall:
                if (town.buildingWallTile != null)
                {
                    floorTile = town.buildingWallTile;
                    terrainKind = TerrainKind.Wall;
                    cliff = new ChunkBuildResult.IsCliff(true, true);
                }
                break;
        }
    }

    private bool TryGetTownCell(int x, int y, out TownFeatureData town, out TownCellKind kind)
    {
        FeatureInstance requiredTown = GetGuaranteedTownInstance();
        if (TryGetTownCell(requiredTown, x, y, out town, out kind))
            return true;

        int cellSize = Mathf.Max(1, featureLayer.cellSize);
        int cellX = Mathf.FloorToInt((float)x / cellSize);
        int cellY = Mathf.FloorToInt((float)y / cellSize);
        for (int oy = -featureNeighborRange; oy <= featureNeighborRange; oy++)
        for (int ox = -featureNeighborRange; ox <= featureNeighborRange; ox++)
        {
            FeatureInstance instance = GetFeatureInstance(cellX + ox, cellY + oy);
            if (TryGetTownCell(instance, x, y, out town, out kind))
                return true;
        }
        town = null;
        kind = TownCellKind.None;
        return false;
    }

    private bool TryGetTownCell(
        FeatureInstance instance,
        int x,
        int y,
        out TownFeatureData town,
        out TownCellKind kind)
    {
        town = instance.feature as TownFeatureData;
        if (!instance.exists || town == null)
        {
            kind = TownCellKind.None;
            return false;
        }
        Vector2 local = WorldToFeatureLocal(
            new Vector2(x, y),
            instance.center,
            Mathf.Cos(instance.rotation),
            Mathf.Sin(instance.rotation));
        kind = GetTownLayout(instance).GetCell(Vector2Int.RoundToInt(local));
        return kind != TownCellKind.None;
    }

    private FeatureInstance GetGuaranteedTownInstance()
    {
        if (!hasWorldSpawnPosition)
            return default;
        lock (guaranteedTownLock)
        {
            if (guaranteedTownResolved)
                return guaranteedTown;
            guaranteedTownResolved = true;
            TownFeatureData town = (featureLayer.features ?? Array.Empty<FeatureData>())
                .OfType<TownFeatureData>()
                .FirstOrDefault(candidate => candidate != null && candidate.guaranteeNearWorldSpawn);
            if (town == null)
                return default;

            int regionSize = WorldPartition.RegionSizeInChunks * ChunkBuildResult.ChunkSize;
            float minimumDistance = Mathf.Max(1, town.minimumSpawnDistanceRegions) * regionSize;
            float maximumDistance = Mathf.Max(
                town.minimumSpawnDistanceRegions,
                town.maximumSpawnDistanceRegions) * regionSize;
            int salt = unchecked((int)seed) ^ StableHash(town.persistentId) ^ 0x3c6ef372;
            Vector2 spawn = WorldSpawnPosition;
            for (int attempt = 0; attempt < 192; attempt++)
            {
                float angle = Util.Hash01(attempt, salt, 0x2719) * Mathf.PI * 2f;
                float distance = Mathf.Lerp(
                    minimumDistance,
                    maximumDistance,
                    Util.Hash01(attempt, salt, 0x527d));
                Vector2 center = spawn + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                TerrainGenerationState placement = SampleFeaturePlacementTerrain(
                    Mathf.RoundToInt(center.x),
                    Mathf.RoundToInt(center.y));
                if (!town.Allows(placement, elevationLayer.waterHeight))
                    continue;
                float radius = Mathf.Lerp(
                    town.minimumRadius,
                    town.maximumRadius,
                    Util.Hash01(attempt, salt, 0x108d7));
                float rotation = Mathf.Round(
                    Util.Hash01(attempt, salt, 0x713a5) * 4f) * Mathf.PI * 0.5f;
                guaranteedTown = new FeatureInstance
                {
                    exists = true,
                    feature = town,
                    center = center,
                    radius = radius,
                    aspect = 1f,
                    rotation = rotation
                };
                break;
            }
            return guaranteedTown;
        }
    }

    public bool TryGetGuaranteedTownPosition(out Vector2 position)
    {
        FeatureInstance instance = GetGuaranteedTownInstance();
        position = instance.center;
        return instance.exists;
    }

    private TownLayout GetTownLayout(FeatureInstance instance)
    {
        long key = PackCoordinates(
            Mathf.RoundToInt(instance.center.x * 16f),
            Mathf.RoundToInt(instance.center.y * 16f));
        Lazy<TownLayout> lazy;
        lock (townLayoutLock)
        {
            if (!townLayouts.TryGetValue(key, out lazy))
            {
                TownFeatureData town = (TownFeatureData)instance.feature;
                int layoutSeed = unchecked(
                    (int)seed ^ key.GetHashCode() ^ StableHash(town.persistentId));
                int radius = Mathf.Max(8, Mathf.RoundToInt(instance.radius));
                lazy = new Lazy<TownLayout>(
                    () => (town.layoutGenerator ?? new VillageTownLayoutGenerator())
                        .Generate(town, layoutSeed, radius),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                townLayouts.Add(key, lazy);
            }
        }
        return lazy.Value;
    }

    private FeatureInstance CreateFeatureInstance(int cellX, int cellY)
    {
        int seedSalt = unchecked((int)seed);
        if (Util.Hash01(cellX, cellY, seedSalt ^ 0x37a91) >
            featureLayer.chancePerCell)
        {
            return default;
        }

        FeatureData feature = SelectFeature(
            Util.Hash01(cellX, cellY, seedSalt ^ 0x51bc3));
        if (feature == null)
            return default;

        int cellSize = Mathf.Max(1, featureLayer.cellSize);
        float centerX =
            (cellX + Mathf.Lerp(
                0.18f,
                0.82f,
                Util.Hash01(cellX, cellY, seedSalt ^ 0x229f1))) *
            cellSize;
        float centerY =
            (cellY + Mathf.Lerp(
                0.18f,
                0.82f,
                Util.Hash01(cellX, cellY, seedSalt ^ 0x6f18d))) *
            cellSize;

        TerrainGenerationState placement = SampleFeaturePlacementTerrain(
            Mathf.RoundToInt(centerX),
            Mathf.RoundToInt(centerY));
        if (!feature.Allows(placement, elevationLayer.waterHeight))
            return default;

        float rotation = Util.Hash01(
                cellX,
                cellY,
                seedSalt ^ StableHash(feature.persistentId)) *
            Mathf.PI * 2f;
        if (feature is TownFeatureData)
            rotation = Mathf.Round(rotation / (Mathf.PI * 0.5f)) * (Mathf.PI * 0.5f);

        return new FeatureInstance
        {
            exists = true,
            feature = feature,
            center = new Vector2(centerX, centerY),
            radius = Mathf.Lerp(
                feature.minimumRadius,
                feature.maximumRadius,
                Util.Hash01(cellX, cellY, seedSalt ^ 0x108d7)),
            aspect = Mathf.Lerp(
                feature.minimumAspect,
                feature.maximumAspect,
                Util.Hash01(cellX, cellY, seedSalt ^ 0x713a5)),
            rotation = rotation
        };
    }

    public TerrainKind GetTerrainKind(int x, int y)
    {
        if (caveLayer == null)
            return TerrainKind.Floor;

        Vector2 warped = WarpCavePoint(x, y);
        float routeDistance = GetCaveRouteDistance(x, y);
        bool protectedRoute = routeDistance <=
            caveLayer.corridorHalfWidth + caveLayer.protectedRouteMargin;
        if (routeDistance > caveLayer.corridorHalfWidth &&
            IsCellularCaveWall(warped))
            return TerrainKind.Wall;
        if (!protectedRoute && IsCaveCrevasse(warped, x, y))
            return TerrainKind.Crevasse;
        return TerrainKind.Floor;
    }

    private bool IsCellularCaveWall(Vector2 point)
    {
        int cellSize = Mathf.Max(1, caveLayer.cellularCellSize);
        int cellX = Mathf.FloorToInt(point.x / cellSize);
        int cellY = Mathf.FloorToInt(point.y / cellSize);
        int blockX = FloorDiv(cellX, CaveCellBlockSize);
        int blockY = FloorDiv(cellY, CaveCellBlockSize);
        long key = PackCoordinates(blockX, blockY);
        Lazy<byte[]> lazyBlock;

        lock (caveCellBlockLock)
        {
            if (!caveCellBlocks.TryGetValue(key, out lazyBlock))
            {
                int capturedBlockX = blockX;
                int capturedBlockY = blockY;
                lazyBlock = new Lazy<byte[]>(
                    () => BuildCaveCellBlock(
                        capturedBlockX,
                        capturedBlockY),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                caveCellBlocks.Add(key, lazyBlock);
                caveCellBlockOrder.Enqueue(key);
                while (caveCellBlocks.Count > CaveCellCacheCapacity)
                {
                    long oldest = caveCellBlockOrder.Dequeue();
                    caveCellBlocks.Remove(oldest);
                }
            }
        }

        // Generate outside the dictionary lock. Different cave blocks can now
        // use separate chunk workers, while Lazy still guarantees that two
        // neighboring chunks requesting the same block build it only once.
        byte[] block = lazyBlock.Value;
        int localX = cellX - blockX * CaveCellBlockSize;
        int localY = cellY - blockY * CaveCellBlockSize;
        return block[localY * CaveCellBlockSize + localX] != 0;
    }

    private byte[] BuildCaveCellBlock(int blockX, int blockY)
    {
        int iterations = Mathf.Clamp(caveLayer.cellularIterations, 0, 8);
        int padding = iterations;
        int side = CaveCellBlockSize + padding * 2;
        int originX = blockX * CaveCellBlockSize - padding;
        int originY = blockY * CaveCellBlockSize - padding;
        byte[] current = new byte[side * side];
        byte[] next = new byte[side * side];

        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            current[y * side + x] = IsInitialCaveWall(
                originX + x,
                originY + y)
                ? (byte)1
                : (byte)0;
        }

        int birthLimit = Mathf.Clamp(caveLayer.wallBirthLimit, 0, 8);
        int survivalLimit = Mathf.Clamp(caveLayer.wallSurvivalLimit, 0, 8);
        for (int iteration = 1; iteration <= iterations; iteration++)
        {
            int minimum = iteration;
            int maximum = side - iteration;
            for (int y = minimum; y < maximum; y++)
            for (int x = minimum; x < maximum; x++)
            {
                int neighborWalls = 0;
                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0)
                        continue;
                    neighborWalls += current[(y + oy) * side + x + ox];
                }

                bool isWall = current[y * side + x] != 0;
                next[y * side + x] =
                    neighborWalls >= (isWall ? survivalLimit : birthLimit)
                        ? (byte)1
                        : (byte)0;
            }

            (current, next) = (next, current);
        }

        byte[] result = new byte[CaveCellBlockSize * CaveCellBlockSize];
        for (int y = 0; y < CaveCellBlockSize; y++)
        {
            Array.Copy(
                current,
                (y + padding) * side + padding,
                result,
                y * CaveCellBlockSize,
                CaveCellBlockSize);
        }

        return result;
    }

    private bool IsInitialCaveWall(int cellX, int cellY)
    {
        int cellSize = Mathf.Max(1, caveLayer.cellularCellSize);
        float worldX = (cellX + 0.5f) * cellSize;
        float worldY = (cellY + 0.5f) * cellSize;
        GetCaveBiomeShape(
            worldX,
            worldY,
            out float chamberScale,
            out float chamberOpenness);
        float chamber = caveChamberNoise.GetNoise(
            worldX / chamberScale,
            worldY / chamberScale);
        float wallChance = Mathf.Clamp01(
            caveLayer.initialWallChance -
            chamber * caveLayer.chamberInfluence -
            chamberOpenness);
        int seedSalt = unchecked((int)seed) ^ 0x5ca1ab1;
        return Util.Hash01(cellX, cellY, seedSalt) < wallChance;
    }

    private void GetCaveBiomeShape(
        float worldX,
        float worldY,
        out float chamberScale,
        out float chamberOpenness)
    {
        float fallback = Mathf.Max(1f, caveLayer.chamberScale);
        if (biomeLibrary.Length == 0)
        {
            chamberScale = fallback;
            chamberOpenness = 0f;
            return;
        }

        if (biomeLibrary.Length == 1)
        {
            BiomeData b = biomeLibrary[0];
            float scale = b?.hillScale ?? fallback;
            chamberScale = scale > 0f ? scale : fallback;
            chamberOpenness = Mathf.Clamp(
                b?.hillStrength ?? 0f,
                0f,
                0.15f);
            return;
        }

        // Cave biome selection is cellular and does not consume climate. The
        // previous code evaluated blended climate noise here for every source
        // cell in every automaton block, then discarded it.
        BiomeBlend biome = SelectCellularCaveBiome(
            Mathf.FloorToInt(worldX),
            Mathf.FloorToInt(worldY));
        chamberScale = biome.hillScale > 0f
            ? biome.hillScale
            : fallback;
        chamberOpenness = Mathf.Clamp(biome.hillStrength, 0f, 0.15f);
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static long PackCoordinates(int x, int y)
    {
        return (long)(uint)x << 32 | (uint)y;
    }

    private Vector2 WarpCavePoint(float x, float y)
    {
        float scale = Mathf.Max(1f, caveLayer.warpScale);
        float strength = caveLayer.warpStrength;
        return new Vector2(
            x + caveWarpNoise.GetNoise(x / scale, y / scale) * strength,
            y + caveWarpNoise.GetNoise((x + 193.7f) / scale, (y - 71.3f) / scale) * strength);
    }

    private bool IsCaveCrevasse(
        Vector2 point,
        int worldX,
        int worldY)
    {
        float strength = GetBiomeCrevasseStrength(worldX, worldY);
        if (strength <= 0f)
            return false;

        float cellSize = Mathf.Max(4f, caveLayer.crevasseCellSize);
        float px = point.x / cellSize;
        float py = point.y / cellSize;
        int baseX = Mathf.FloorToInt(px);
        int baseY = Mathf.FloorToInt(py);
        float nearest = float.MaxValue;
        float second = float.MaxValue;
        for (int oy = -1; oy <= 1; oy++)
        for (int ox = -1; ox <= 1; ox++)
        {
            int cx = baseX + ox;
            int cy = baseY + oy;
            float fx = cx + Util.Hash01(cx, cy, 741121);
            float fy = cy + Util.Hash01(cx, cy, 741127);
            float distance = new Vector2(px - fx, py - fy).sqrMagnitude;
            if (distance < nearest) { second = nearest; nearest = distance; }
            else if (distance < second) second = distance;
        }
        float edgeDistance = Mathf.Sqrt(second) - Mathf.Sqrt(nearest);
        float mask = Mathf.InverseLerp(-1f, 1f,
            caveCrevasseMaskNoise.GetNoise(point.x / 128f, point.y / 128f));
        return edgeDistance < caveLayer.crevasseWidth * strength &&
               mask < Mathf.Clamp01(caveLayer.crevasseDensity * strength);
    }

    private float GetBiomeCrevasseStrength(int worldX, int worldY)
    {
        if (biomeLibrary.Length == 1)
            return Mathf.Max(0f, biomeLibrary[0].crevasseStrength);

        // Cellular cave biome selection ignores climate, so avoid calculating
        // a blended climate sample for every crevasse test.
        BiomeBlend biome = SelectCellularCaveBiome(worldX, worldY);
        return Mathf.Max(0f, biome.crevasseStrength);
    }

    private float GetCaveRouteDistance(float x, float y)
    {
        int size = Mathf.Max(32, caveLayer.regionSize);
        int rx = Mathf.FloorToInt(x / size);
        int ry = Mathf.FloorToInt(y / size);
        Vector2 point = new(x, y);
        Vector2 center = CaveRegionAnchor(rx, ry, size);
        float distance = float.MaxValue;
        distance = Mathf.Min(distance, DistanceToSegment(point, center, CaveRegionAnchor(rx + 1, ry, size)));
        distance = Mathf.Min(distance, DistanceToSegment(point, center, CaveRegionAnchor(rx - 1, ry, size)));
        distance = Mathf.Min(distance, DistanceToSegment(point, center, CaveRegionAnchor(rx, ry + 1, size)));
        distance = Mathf.Min(distance, DistanceToSegment(point, center, CaveRegionAnchor(rx, ry - 1, size)));
        return distance;
    }

    private Vector2 CaveRegionAnchor(int rx, int ry, int size)
    {
        const float margin = 0.28f;
        return new Vector2(
            (rx + Mathf.Lerp(margin, 1f - margin, Util.Hash01(rx, ry, 741131))) * size,
            (ry + Mathf.Lerp(margin, 1f - margin, Util.Hash01(rx, ry, 741133))) * size);
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(point - a, delta) /
                                Mathf.Max(0.0001f, delta.sqrMagnitude));
        return Vector2.Distance(point, a + delta * t);
    }

    private TerrainGenerationState SampleFeaturePlacementTerrain(int x, int y)
    {
        Vector3 values = SampleBlendedTerrainValues(
            x,
            y,
            climateLayer.blendSampleRadius);
        return new TerrainGenerationState
        {
            height = values.x,
            baseHeight = values.x,
            moisture = values.y,
            temperature = values.z,
            biomeData = SelectBiomeBlend(x, y, values)
        };
    }

    private FeatureData SelectFeature(float roll)
    {
        float totalWeight = 0f;
        foreach (FeatureData feature in featureLayer.features)
        {
            if (feature != null)
                totalWeight += Mathf.Max(0f, feature.selectionWeight);
        }

        if (totalWeight <= 0f)
            return null;

        float target = roll * totalWeight;
        FeatureData lastWeighted = null;
        foreach (FeatureData feature in featureLayer.features)
        {
            if (feature == null)
                continue;

            float weight = Mathf.Max(0f, feature.selectionWeight);
            if (weight <= 0f)
                continue;
            lastWeighted = feature;
            target -= weight;
            if (target <= 0f)
                return feature;
        }

        return lastWeighted;
    }

    public IReadOnlyList<PropSpawnData> GetFeatureBuildingSpawns(
        Vector2Int chunkPosition)
    {
        Vector2Int region = WorldPartition.ChunkToRegion(chunkPosition);
        Dictionary<string, FeatureInstance> winners =
            GetFeatureBuildingWinners(region);
        List<PropSpawnData> spawns = new();

        foreach (KeyValuePair<string, FeatureInstance> pair in winners)
        {
            FeatureInstance instance = pair.Value;
            var building = instance.feature as FeatureBuildingData;
            if (building == null || !building.IsConfigured)
                continue;

            Vector2 position = instance.center + building.placementOffset;
            if (WorldPartition.WorldToChunk(position) != chunkPosition)
                continue;

            Vector2Int cell = Vector2Int.FloorToInt(position);
            ushort generatorType = NodeId.CreateGeneratorType(
                $"FeatureBuilding:{building.persistentId}");
            spawns.Add(new PropSpawnData
            {
                NodeId = NodeId.Create(
                    seed,
                    cell,
                    generatorType,
                    0),
                worldPosition = cell,
                propName = building.persistentId,
                nodeData = building.entityArchetype.NodeData,
                position = position,
                scale = 1f,
                terrainSample = GetTerrainSample(cell.x, cell.y),
                persistenceKind = EntityPersistenceKind.Procedural
            });
        }

        return spawns;
    }

    public void ApplyFeatureEntityGenerators(
        Vector2Int chunkPosition,
        List<PropSpawnData> entities)
    {
        if (entities == null)
            throw new ArgumentNullException(nameof(entities));

        // Resolve the anchor before sampling the fixed feature. Terrain samples
        // used by the spawn search intentionally ignore this feature to avoid a
        // circular WorldSpawnPosition lookup.
        _ = WorldSpawnPosition;

        List<FeatureInstance> affecting =
            GetFeatureInstancesAffectingChunk(chunkPosition).ToList();
        // Towns are applied last so their mandatory clear pass removes props
        // and entities contributed by every overlapping ordinary feature.
        foreach (FeatureInstance instance in affecting)
        {
            if (instance.feature is TownFeatureData)
                continue;
            ApplyFeatureEntityGenerators(
                chunkPosition,
                entities,
                instance);
        }
        foreach (FeatureInstance instance in affecting)
        {
            if (instance.feature is not TownFeatureData)
                continue;
            ApplyFeatureEntityGenerators(chunkPosition, entities, instance);
        }
    }

    private IEnumerable<FeatureInstance> GetFeatureInstancesAffectingChunk(
        Vector2Int chunkPosition)
    {
        int cellSize = Mathf.Max(1, featureLayer.cellSize);
        int minimumWorldX = chunkPosition.x * ChunkBuildResult.ChunkSize;
        int minimumWorldY = chunkPosition.y * ChunkBuildResult.ChunkSize;
        int maximumWorldX = minimumWorldX + ChunkBuildResult.ChunkSize - 1;
        int maximumWorldY = minimumWorldY + ChunkBuildResult.ChunkSize - 1;
        int minimumCellX =
            Mathf.FloorToInt((float)minimumWorldX / cellSize) -
            featureNeighborRange;
        int minimumCellY =
            Mathf.FloorToInt((float)minimumWorldY / cellSize) -
            featureNeighborRange;
        int maximumCellX =
            Mathf.FloorToInt((float)maximumWorldX / cellSize) +
            featureNeighborRange;
        int maximumCellY =
            Mathf.FloorToInt((float)maximumWorldY / cellSize) +
            featureNeighborRange;

        if (featureLayer.chancePerCell > 0f &&
            featureLayer.features != null &&
            featureLayer.features.Length > 0)
        {
            for (int cellY = minimumCellY; cellY <= maximumCellY; cellY++)
            for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
            {
                FeatureInstance instance = GetFeatureInstance(cellX, cellY);
                if (!instance.exists ||
                    !IsSelectedFeatureBuilding(
                        instance,
                        GetFeaturePosition(instance)))
                {
                    continue;
                }

                yield return instance;
            }
        }

        FeatureInstance worldSpawn = GetWorldSpawnFeatureInstance();
        if (worldSpawn.exists)
            yield return worldSpawn;
        FeatureInstance requiredTown = GetGuaranteedTownInstance();
        if (requiredTown.exists)
            yield return requiredTown;
    }

    private void ApplyFeatureEntityGenerators(
        Vector2Int chunkPosition,
        List<PropSpawnData> entities,
        FeatureInstance instance)
    {
        float cosine = Mathf.Cos(instance.rotation);
        float sine = Mathf.Sin(instance.rotation);

        if (instance.feature is TownFeatureData town)
        {
            float clearRadiusSquared = instance.radius * instance.radius;
            entities.RemoveAll(candidate =>
                (candidate.position - instance.center).sqrMagnitude <= clearRadiusSquared);
            TownLayout layout = GetTownLayout(instance);
            IReadOnlyList<TownEntityPlacement> townEntities = layout.Entities;
            for (int entityIndex = 0; entityIndex < townEntities.Count; entityIndex++)
            {
                TownEntityPlacement placement = townEntities[entityIndex];
                if (placement.entity == null)
                    continue;
                Vector2 local = placement.localCell;
                Vector2 position = instance.center + new Vector2(
                    local.x * cosine - local.y * sine,
                    local.x * sine + local.y * cosine);
                Vector2Int worldCell = Vector2Int.RoundToInt(position);
                if (WorldPartition.WorldToChunk(worldCell) != chunkPosition)
                    continue;
                string placementId = $"Town:{instance.feature.persistentId}:{placement.id}";
                entities.RemoveAll(candidate => candidate.worldPosition == worldCell);
                entities.Add(new PropSpawnData
                {
                    NodeId = NodeId.Create(
                        seed,
                        worldCell,
                        NodeId.CreateGeneratorType(placementId),
                        (ushort)Mathf.Clamp(entityIndex, 0, ushort.MaxValue)),
                    worldPosition = worldCell,
                    propName = placementId,
                    nodeData = placement.entity,
                    // Town layout cells are rasterized with RoundToInt. Use
                    // that exact cell as the entity anchor; Node placement
                    // floors its input, so passing the fractional transformed
                    // position could move a door into the neighboring cell.
                    position = worldCell,
                    scale = 1f,
                    terrainSample = GetTerrainSample(worldCell.x, worldCell.y),
                    persistenceKind = EntityPersistenceKind.Procedural,
                    generatedTownName = placement.generatedName
                });
            }
        }

        GeneratorInfo[] recipe =
            instance.feature.generators ?? Array.Empty<GeneratorInfo>();

        for (int generatorIndex = 0;
             generatorIndex < recipe.Length;
             generatorIndex++)
        {
            GeneratorInfo info = recipe[generatorIndex];
            FeatureGenerator generator = info?.generator;
            if (info?.enabled != true ||
                generator == null ||
                info.strength <= 0f)
            {
                continue;
            }

            entities.RemoveAll(candidate => generator.ClearsEntity(
                WorldToFeatureLocal(
                    candidate.position,
                    instance.center,
                    cosine,
                    sine)));

            if (!generator.TryGetEntityPlacement(
                    out FeatureEntityPlacement placement) ||
                placement.entity == null)
            {
                continue;
            }

            Vector2 rotatedOffset = new(
                placement.offset.x * cosine - placement.offset.y * sine,
                placement.offset.x * sine + placement.offset.y * cosine);
            Vector2 position = instance.center + rotatedOffset;
            if (WorldPartition.WorldToChunk(position) != chunkPosition)
                continue;

            Vector2Int worldCell = Vector2Int.FloorToInt(position);
            if (placement.requireWalkableArea &&
                !IsGeneratedAreaWalkable(new RectInt(
                    worldCell + placement.walkableAreaOffset,
                    new Vector2Int(
                        Mathf.Max(1, placement.walkableAreaSize.x),
                        Mathf.Max(1, placement.walkableAreaSize.y)))))
            {
                continue;
            }
            string placementId = string.IsNullOrWhiteSpace(
                placement.persistentId)
                ? $"FeatureEntity:{instance.feature.persistentId}:{generatorIndex}"
                : placement.persistentId.Trim();
            ushort generatorType = NodeId.CreateGeneratorType(placementId);

            entities.RemoveAll(candidate =>
                candidate.worldPosition == worldCell);
            entities.Add(new PropSpawnData
            {
                NodeId = NodeId.Create(
                    seed,
                    worldCell,
                    generatorType,
                    slot: 0),
                worldPosition = worldCell,
                propName = placementId,
                nodeData = placement.entity,
                position = position,
                scale = placement.scale,
                flipX = placement.flipX,
                terrainSample = GetTerrainSample(
                    worldCell.x,
                    worldCell.y),
                persistenceKind = EntityPersistenceKind.Procedural,
                damageImmune = placement.damageImmune,
                clearReservedAreaCoverage =
                    placement.clearReservedAreaCoverage
            });
        }
    }

    public bool IsGeneratedAreaWalkable(RectInt worldArea)
    {
        foreach (Vector2Int cell in worldArea.allPositionsWithin)
        {
            if (!GetTerrainSample(cell.x, cell.y).IsWalkable)
                return false;
        }

        return true;
    }

    private static Vector2 WorldToFeatureLocal(
        Vector2 position,
        Vector2 center,
        float cosine,
        float sine)
    {
        Vector2 delta = position - center;
        return new Vector2(
            delta.x * cosine + delta.y * sine,
            -delta.x * sine + delta.y * cosine);
    }

    private static Vector2 GetFeaturePosition(FeatureInstance instance) =>
        instance.feature is FeatureBuildingData building
            ? instance.center + building.placementOffset
            : instance.center;

    public bool RegionContainsGeneratedFeatureBuilding(
        Vector2Int region,
        string uniqueKey)
    {
        if (string.IsNullOrWhiteSpace(uniqueKey))
            return false;

        return GetFeatureBuildingWinners(region).ContainsKey(uniqueKey.Trim());
    }

    public bool TryLocateFeatureInRegion(
        string featureId,
        Vector2Int region,
        out Vector2 position)
    {
        position = default;
        string normalizedId = featureId?.Trim();
        if (string.IsNullOrEmpty(normalizedId))
            return false;

        FeatureData worldSpawn = WorldSpawnFeature;
        if (worldSpawn != null &&
            string.Equals(
                worldSpawn.persistentId,
                normalizedId,
                StringComparison.OrdinalIgnoreCase))
        {
            position = WorldSpawnPosition;
            return WorldPartition.ChunkToRegion(
                WorldPartition.WorldToChunk(position)) == region;
        }

        if (featureLayer?.features == null)
            return false;

        FeatureBuildingData requestedBuilding =
            featureLayer.features?
                .OfType<FeatureBuildingData>()
                .FirstOrDefault(building => string.Equals(
                    building.persistentId,
                    normalizedId,
                    StringComparison.OrdinalIgnoreCase));
        if (requestedBuilding != null)
        {
            foreach (FeatureInstance winner in
                     GetFeatureBuildingWinners(region).Values)
            {
                if (winner.feature is not FeatureBuildingData building ||
                    !string.Equals(
                        building.persistentId,
                        normalizedId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                position = winner.center + building.placementOffset;
                return true;
            }

            return false;
        }

        int regionWorldSize =
            WorldPartition.RegionSizeInChunks * ChunkBuildResult.ChunkSize;
        int minimumWorldX = region.x * regionWorldSize;
        int minimumWorldY = region.y * regionWorldSize;
        int maximumWorldX = minimumWorldX + regionWorldSize - 1;
        int maximumWorldY = minimumWorldY + regionWorldSize - 1;
        int cellSize = Mathf.Max(1, featureLayer.cellSize);
        int minimumCellX = Mathf.FloorToInt((float)minimumWorldX / cellSize) - 1;
        int minimumCellY = Mathf.FloorToInt((float)minimumWorldY / cellSize) - 1;
        int maximumCellX = Mathf.FloorToInt((float)maximumWorldX / cellSize) + 1;
        int maximumCellY = Mathf.FloorToInt((float)maximumWorldY / cellSize) + 1;

        for (int cellY = minimumCellY; cellY <= maximumCellY; cellY++)
        {
            for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
            {
                FeatureInstance instance = GetFeatureInstance(cellX, cellY);
                if (!instance.exists ||
                    instance.feature is FeatureBuildingData ||
                    !string.Equals(
                        instance.feature.persistentId,
                        normalizedId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Vector2 candidate = instance.feature is FeatureBuildingData building
                    ? instance.center + building.placementOffset
                    : instance.center;
                if (WorldPartition.ChunkToRegion(
                        WorldPartition.WorldToChunk(candidate)) != region)
                {
                    continue;
                }

                position = candidate;
                return true;
            }
        }

        return false;
    }

    public bool TryFindNearestFeature(
        string featureId,
        Vector2Int originRegion,
        int maximumRegionRadius,
        out Vector2Int featureRegion,
        out Vector2 position)
    {
        featureRegion = default;
        position = default;
        if (string.IsNullOrWhiteSpace(featureId) || maximumRegionRadius < 0)
            return false;

        for (int radius = 0; radius <= maximumRegionRadius; radius++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (TryFindFeatureAtRegionOffset(
                        featureId,
                        originRegion,
                        x,
                        radius,
                        out featureRegion,
                        out position))
                    return true;
                if (radius > 0 && TryFindFeatureAtRegionOffset(
                        featureId,
                        originRegion,
                        x,
                        -radius,
                        out featureRegion,
                        out position))
                    return true;
            }

            for (int y = -radius + 1; y < radius; y++)
            {
                if (TryFindFeatureAtRegionOffset(
                        featureId,
                        originRegion,
                        radius,
                        y,
                        out featureRegion,
                        out position))
                    return true;
                if (radius > 0 && TryFindFeatureAtRegionOffset(
                        featureId,
                        originRegion,
                        -radius,
                        y,
                        out featureRegion,
                        out position))
                    return true;
            }
        }

        return false;
    }

    private bool TryFindFeatureAtRegionOffset(
        string featureId,
        Vector2Int originRegion,
        int offsetX,
        int offsetY,
        out Vector2Int featureRegion,
        out Vector2 position)
    {
        featureRegion = new Vector2Int(
            originRegion.x + offsetX,
            originRegion.y + offsetY);
        return TryLocateFeatureInRegion(
            featureId,
            featureRegion,
            out position);
    }

    public bool TryLocateAnyFeatureInRegion(
        Vector2Int region,
        out FeatureData feature,
        out Vector2 position)
    {
        foreach (FeatureData candidate in
                 featureLayer.features ?? Array.Empty<FeatureData>())
        {
            if (candidate != null &&
                TryLocateFeatureInRegion(
                    candidate.persistentId,
                    region,
                    out position))
            {
                feature = candidate;
                return true;
            }
        }

        feature = null;
        position = default;
        return false;
    }

    public IReadOnlyList<IFeatureData> FindFeatures(
        Vector3 center,
        float radius)
    {
        if (radius <= 0f || featureLayer?.features == null)
            return Array.Empty<IFeatureData>();

        float radiusSquared = radius * radius;
        int cellSize = Mathf.Max(1, featureLayer.cellSize);
        int minimumCellX = Mathf.FloorToInt((center.x - radius) / cellSize) - 1;
        int minimumCellY = Mathf.FloorToInt((center.y - radius) / cellSize) - 1;
        int maximumCellX = Mathf.FloorToInt((center.x + radius) / cellSize) + 1;
        int maximumCellY = Mathf.FloorToInt((center.y + radius) / cellSize) + 1;
        List<IFeatureData> result = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        for (int cellY = minimumCellY; cellY <= maximumCellY; cellY++)
        for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
        {
            FeatureInstance instance = GetFeatureInstance(cellX, cellY);
            FeatureData feature = instance.feature;
            if (!instance.exists || feature == null || !feature.canBeSensed)
                continue;

            Vector2 position = feature is FeatureBuildingData building
                ? instance.center + building.placementOffset
                : instance.center;
            if (((Vector2)center - position).sqrMagnitude > radiusSquared ||
                !IsSelectedFeatureBuilding(instance, position))
                continue;

            string key = $"{feature.persistentId}:{position.x:R}:{position.y:R}";
            if (!seen.Add(key))
                continue;
            result.Add(new LocatedFeature(
                position,
                feature.senseIcon,
                string.IsNullOrWhiteSpace(feature.senseName)
                    ? feature.name
                    : feature.senseName.Trim()));
        }

        return result;
    }

    private bool IsSelectedFeatureBuilding(
        FeatureInstance instance,
        Vector2 position)
    {
        if (instance.feature is not FeatureBuildingData building)
            return true;
        Vector2Int region = WorldPartition.ChunkToRegion(
            WorldPartition.WorldToChunk(position));
        string key = string.IsNullOrWhiteSpace(building.regionUniqueKey)
            ? null
            : building.regionUniqueKey.Trim();
        if (key == null)
            return true;
        return GetFeatureBuildingWinners(region).TryGetValue(key, out FeatureInstance winner) &&
               winner.feature == instance.feature &&
               (winner.center - instance.center).sqrMagnitude < 0.0001f;
    }

    private sealed class LocatedFeature : IFeatureData
    {
        public Vector3 Position { get; }
        public Sprite Icon { get; }
        public string Name { get; }

        public LocatedFeature(Vector2 position, Sprite icon, string name)
        {
            Position = position;
            Icon = icon;
            Name = name;
        }
    }

    private Dictionary<string, FeatureInstance> GetFeatureBuildingWinners(
        Vector2Int region)
    {
        Lazy<Dictionary<string, FeatureInstance>> lazy;
        lock (featureBuildingWinnerLock)
        {
            if (!featureBuildingWinners.TryGetValue(region, out lazy))
            {
                Vector2Int capturedRegion = region;
                lazy = new Lazy<Dictionary<string, FeatureInstance>>(
                    () => BuildFeatureBuildingWinners(capturedRegion),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                featureBuildingWinners.Add(region, lazy);
            }
        }
        return lazy.Value;
    }

    private Dictionary<string, FeatureInstance> BuildFeatureBuildingWinners(
        Vector2Int region)
    {
        Dictionary<string, FeatureInstance> winners =
            new(StringComparer.Ordinal);
        int regionWorldSize =
            WorldPartition.RegionSizeInChunks * ChunkBuildResult.ChunkSize;
        int minimumWorldX = region.x * regionWorldSize;
        int minimumWorldY = region.y * regionWorldSize;
        int maximumWorldX = minimumWorldX + regionWorldSize - 1;
        int maximumWorldY = minimumWorldY + regionWorldSize - 1;
        int cellSize = Mathf.Max(1, featureLayer.cellSize);
        int minimumCellX = Mathf.FloorToInt((float)minimumWorldX / cellSize) - 1;
        int minimumCellY = Mathf.FloorToInt((float)minimumWorldY / cellSize) - 1;
        int maximumCellX = Mathf.FloorToInt((float)maximumWorldX / cellSize) + 1;
        int maximumCellY = Mathf.FloorToInt((float)maximumWorldY / cellSize) + 1;

        for (int cellY = minimumCellY; cellY <= maximumCellY; cellY++)
        {
            for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
            {
                FeatureInstance instance = GetFeatureInstance(cellX, cellY);
                if (!instance.exists ||
                    instance.feature is not FeatureBuildingData building ||
                    !building.IsConfigured)
                {
                    continue;
                }

                Vector2 position = instance.center + building.placementOffset;
                if (WorldPartition.ChunkToRegion(
                        WorldPartition.WorldToChunk(position)) != region)
                {
                    continue;
                }

                string key = string.IsNullOrWhiteSpace(building.regionUniqueKey)
                    ? $"{building.persistentId}:{cellX}:{cellY}"
                    : building.regionUniqueKey.Trim();

                // Iteration is deterministic. Keeping the first candidate gives
                // every chunk worker the same one-per-region decision.
                winners.TryAdd(key, instance);
            }
        }

        return winners;
    }

    private void ApplyFeatureInstance(
        int x,
        int y,
        FeatureInstance instance,
        ref TerrainGenerationState terrain)
    {
        float cosine = Mathf.Cos(instance.rotation);
        float sine = Mathf.Sin(instance.rotation);
        float deltaX = x - instance.center.x;
        float deltaY = y - instance.center.y;
        float localX = deltaX * cosine + deltaY * sine;
        float localY = -deltaX * sine + deltaY * cosine;
        float radius = Mathf.Max(1f, instance.radius);
        float aspect = Mathf.Max(0.1f, instance.aspect);
        float normalizedDistance = Mathf.Sqrt(
            localX * localX / (radius * radius) +
            localY * localY / (radius * radius * aspect * aspect));

        if (normalizedDistance > 1.45f)
            return;

        float warpScale = Mathf.Max(8f, radius * 0.65f);
        float edgeNoise = volcanoNoise.GetNoise(
            x / warpScale,
            y / warpScale);
        float distortedDistance =
            normalizedDistance +
            edgeNoise *
            instance.feature.edgeWarp *
            SmoothStep(0.25f, 1f, normalizedDistance);
        float mask = 1f - SmoothStep(0.72f, 1f, distortedDistance);
        if (mask <= 0f)
            return;

        FeatureGenerationContext context = new(
            x,
            y,
            instance.center,
            localX,
            localY,
            distortedDistance,
            mask,
            volcanoRidgeNoise.GetNoise(x * 0.075f, y * 0.075f));

        foreach (GeneratorInfo info in
                 instance.feature.generators ?? Array.Empty<GeneratorInfo>())
        {
            if (info?.enabled == true &&
                info.generator != null &&
                info.strength > 0f)
            {
                info.generator.Generate(
                    ref terrain,
                    in context,
                    info.strength);
            }
        }
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char character in value ?? string.Empty)
                hash = (hash ^ character) * 16777619u;
            return (int)hash;
        }
    }

    private static float GetMaximumEntityReach(FeatureData feature)
    {
        float maximum = 0f;
        foreach (GeneratorInfo info in
                 feature?.generators ?? Array.Empty<GeneratorInfo>())
        {
            if (info?.enabled == true && info.generator != null)
            {
                maximum = Mathf.Max(
                    maximum,
                    info.generator.EntityReach);
            }
        }

        return maximum;
    }

    private struct FeatureInstance
    {
        public bool exists;
        public FeatureData feature;
        public Vector2 center;
        public float radius;
        public float aspect;
        public float rotation;
    }

    private readonly struct RoadFeature
    {
        public readonly long id;
        public readonly FeatureData feature;
        public readonly Vector2 center;
        public RoadFeature(long id, FeatureInstance instance, Vector2 center)
        {
            this.id = id;
            feature = instance.feature;
            this.center = center;
        }
    }

    private readonly struct RoadRoute
    {
        public readonly RoadFeature first;
        public readonly RoadFeature second;
        public readonly Vector2[] path;
        public RoadRoute(RoadFeature first, RoadFeature second, Vector2[] path)
        {
            this.first = first;
            this.second = second;
            this.path = path;
        }
    }

    private readonly struct RoadBranchKey : IEquatable<RoadBranchKey>
    {
        private readonly long feature;
        private readonly RoadKey road;
        public RoadBranchKey(long feature, long first, long second)
        {
            this.feature = feature;
            road = new RoadKey(first, second);
        }
        public bool Equals(RoadBranchKey other) => feature == other.feature && road.Equals(other.road);
        public override bool Equals(object obj) => obj is RoadBranchKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (feature.GetHashCode() * 397) ^ road.GetHashCode(); }
        }
    }

    private readonly struct RoadKey : IEquatable<RoadKey>
    {
        private readonly long first;
        private readonly long second;
        public RoadKey(long a, long b)
        {
            first = Math.Min(a, b);
            second = Math.Max(a, b);
        }
        public bool Equals(RoadKey other) => first == other.first && second == other.second;
        public override bool Equals(object obj) => obj is RoadKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (first.GetHashCode() * 397) ^ second.GetHashCode(); }
        }
    }
    
    #endregion

    #region Utility

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return Smooth01(t);
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }



    #endregion
}

public static class SeasonalBiomeTint
{
    public const int HoursInDay = 24;

    public static void Apply(
        ref BiomeBlend biome,
        WorldData worldData,
        ITimeController timeController,
        int worldX,
        int worldY)
    {
        biome.untintedGroundColor = biome.groundColor;
        biome.untintedDirtColor = biome.dirtColor;
        biome.untintedPathColor = biome.pathColor;
        biome.untintedWaterColor = biome.waterColor;
        biome.untintedCliffColor = biome.cliffColor;
        biome.untintedBeachColor = biome.beachColor;
        ApplyCurrentTint(
            ref biome,
            worldData,
            timeController,
            worldX,
            worldY);
    }

    public static void Reapply(
        ref BiomeBlend biome,
        WorldData worldData,
        ITimeController timeController,
        int worldX,
        int worldY)
    {
        biome.groundColor = biome.untintedGroundColor;
        biome.dirtColor = biome.untintedDirtColor;
        biome.pathColor = biome.untintedPathColor;
        biome.waterColor = biome.untintedWaterColor;
        biome.cliffColor = biome.untintedCliffColor;
        biome.beachColor = biome.untintedBeachColor;
        ApplyCurrentTint(
            ref biome,
            worldData,
            timeController,
            worldX,
            worldY);
    }

    private static void ApplyCurrentTint(
        ref BiomeBlend biome,
        WorldData worldData,
        ITimeController timeController,
        int worldX,
        int worldY)
    {
        int dayInMonth = timeController?.DayInMonth ?? 1;
        Season season = timeController?.Season ?? worldData.startSeason;
        int year = timeController?.Year ?? 0;
        int hour = timeController?.Hour ?? HoursInDay;

        if (hour < GetScheduledHour(
                worldX,
                worldY,
                dayInMonth,
                season,
                year))
        {
            MoveToPreviousDay(
                ref dayInMonth,
                ref season,
                ref year,
                worldData.daysInMonth);
        }

        float monthProgress = Mathf.Clamp01(
            (float)dayInMonth /
            Mathf.Max(1, worldData.daysInMonth));
        float blend = worldData.seasonalTintCurve == null
            ? monthProgress
            : Mathf.Clamp01(worldData.seasonalTintCurve.Evaluate(monthProgress));

        Color worldTint = Color.Lerp(
            GetWorldTint(worldData, Previous(season)),
            GetWorldTint(worldData, season),
            blend);
        Color biomeTint = Color.Lerp(
            GetBiomeTint(biome, Previous(season)),
            GetBiomeTint(biome, season),
            blend);
        Color tint = Color.LerpUnclamped(
            Color.white,
            worldTint * biomeTint,
            biome.SeasonColorEffectMod);

        biome.groundColor *= tint;
        biome.dirtColor *= tint;
        biome.pathColor *= tint;
        biome.waterColor *= tint;
        biome.cliffColor *= tint;
        biome.beachColor *= tint;
    }

    public static int GetScheduledHour(
        int worldX,
        int worldY,
        int dayInMonth,
        Season season,
        int year)
    {
        uint hash = unchecked((uint)worldX * 0x8da6b343u);
        hash ^= unchecked((uint)worldY * 0xd8163841u);
        hash ^= unchecked((uint)dayInMonth * 0xcb1ab31fu);
        hash ^= unchecked((uint)season * 0x165667b1u);
        hash ^= unchecked((uint)year * 0xa24baed5u);
        hash ^= hash >> 16;
        hash *= 0x7feb352du;
        hash ^= hash >> 15;
        hash *= 0x846ca68bu;
        hash ^= hash >> 16;
        return (int)(hash % HoursInDay);
    }

    public static int GetDayKey(
        int dayInMonth,
        Season season,
        int year)
    {
        return unchecked(year * 1000 + (int)season * 100 + dayInMonth);
    }

    private static void MoveToPreviousDay(
        ref int dayInMonth,
        ref Season season,
        ref int year,
        int daysInMonth)
    {
        if (dayInMonth > 1)
        {
            dayInMonth--;
            return;
        }

        season = Previous(season);
        dayInMonth = Mathf.Max(1, daysInMonth);
        if (season == Season.Winter)
            year = Mathf.Max(0, year - 1);
    }

    private static Season Previous(Season season)
    {
        return (Season)(((int)season + 3) % 4);
    }

    private static Color GetWorldTint(WorldData worldData, Season season)
    {
        return season switch
        {
            Season.Spring => worldData.springTint,
            Season.Summer => worldData.summerTint,
            Season.Autumn => worldData.fallTint,
            Season.Winter => worldData.winterTint,
            _ => Color.white
        };
    }

    private static Color GetBiomeTint(BiomeBlend biome, Season season)
    {
        return season switch
        {
            Season.Spring => biome.springTint,
            Season.Summer => biome.summerTint,
            Season.Autumn => biome.fallTint,
            Season.Winter => biome.winterTint,
            _ => Color.white
        };
    }
}
