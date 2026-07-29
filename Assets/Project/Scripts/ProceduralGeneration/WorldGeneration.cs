using System;
using System.Collections.Generic;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.Enums;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;


public class WorldGeneration : IWorldGenerator
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
    private readonly ITimeController timeController;
    private readonly BiomeData[] biomeLibrary;
    private readonly Dictionary<long, FeatureInstance> featureInstances = new();
    private readonly object featureInstanceLock = new();
    private readonly int featureNeighborRange;

    private uint seed;

    public uint Seed => seed;
    public WorldGenerationPresetData Preset => preset;
    public ElevationLayerData Elevation => elevationLayer;
    public IReadOnlyList<PropSpawnRule> PropSpawnRules =>
        surfaceLayer.propSpawnRules != null &&
        surfaceLayer.propSpawnRules.Length > 0
            ? surfaceLayer.propSpawnRules
            : worldData.propSpawnRules ?? Array.Empty<PropSpawnRule>();
    
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
        this.timeController = timeController;
        seed = unchecked((uint)selection.Seed);
        biomeLibrary =
            climateLayer.biomes != null && climateLayer.biomes.Length > 0
                ? climateLayer.biomes
                : Resources.LoadAll<BiomeData>("Biomes");
        if (biomeLibrary.Length == 0)
            throw new InvalidOperationException($"Preset '{preset.name}' has no biomes.");
        float largestFeatureRadius = 0f;
        foreach (FeatureData feature in featureLayer.features ?? Array.Empty<FeatureData>())
        {
            if (feature != null)
            {
                largestFeatureRadius = Mathf.Max(
                    largestFeatureRadius,
                    feature.maximumRadius);
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
        
        height = GetHeight(x, y, out biomeData, out float baseHeight, out moisture, out temperature);

        if (preset.heightMapDebug)
        {
            height = SelectNoiseLayer(x,y,moisture,temperature, biomeData);
            return 0;
        }

        return GetTileIndex(height);
    }
    
    public TerrainSample GetTerrainSample(int x, int y)
    {
        var height = GetHeight(x, y, out BiomeBlend biomeData, out float baseHeight, out float moisture, out float temperature, 8);
        return new TerrainSample()
        {
            biome = biomeData.dominantBiome,
            biomeBlend = biomeData,
            height = height,
            moisture = moisture,
            temperature = temperature,
                    
            isCliff = IsSmallCliff(x,y),
            isWater = height <= elevationLayer.waterHeight,
            isRoad = false,
            isTrail = false
        };
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
        if (sampleRadius < 0)
            sampleRadius = climateLayer.blendSampleRadius;

        float peakValleyNoise = PeakValleyNoise(x, y);
        float erosionNoise = ErosionNoise(x, y);

        var blendedValues = SampleBlendedTerrainValues(x, y, sampleRadius);

        float height = baseHeight = blendedValues.x;
        moisture = blendedValues.y;
        temperature = blendedValues.z;
        
        biomeData = BiomeSelector.GetBiomeBlend(biomeLibrary, blendedValues.x, blendedValues.y, blendedValues.z);
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
        ApplyFeatures(x, y, ref terrain);
        height = Mathf.InverseLerp(
            elevationLayer.normalizationMinimum,
            elevationLayer.normalizationMaximum,
            terrain.height);
        moisture = Mathf.Clamp01(terrain.moisture);
        temperature = Mathf.Clamp01(terrain.temperature);
        biomeData = terrain.biomeData;
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
        Vector2Int bestPosition = Vector2Int.zero;
        float bestScore = float.MinValue;
        bool found = false;
        
        for (int i = 0; i < maxAttempts; i++)
        {
            int x = UnityEngine.Random.Range(-searchRadius, searchRadius);
            int y = UnityEngine.Random.Range(-searchRadius, searchRadius);
            
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


    #region Spawn Point Discovery

    private Vector2Int FindSpawnPointSpiral(float minHeight, float maxHeight, int maxRadius = 1024)
    {
        for (int radius = 0; radius < maxRadius; radius++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if(IsValidSpawnHeight(x, 0, minHeight, maxHeight))
                    return new Vector2Int(x, radius);
                if(IsValidSpawnHeight(x, -radius, minHeight, maxHeight))
                    return new Vector2Int(-radius, x);
            }

            for (int y = -radius; y <= radius; y++)
            {
                if(IsValidSpawnHeight(radius, y, minHeight, maxHeight))
                    return new Vector2Int(radius, y);
                if(IsValidSpawnHeight(-radius, y, minHeight, maxHeight))
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
        if (featureLayer.chancePerCell <= 0f ||
            featureLayer.features == null ||
            featureLayer.features.Length == 0)
        {
            return;
        }

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
            rotation = Util.Hash01(
                    cellX,
                    cellY,
                    seedSalt ^ StableHash(feature.persistentId)) *
                Mathf.PI * 2f
        };
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
            biomeData = BiomeSelector.GetBiomeBlend(
                biomeLibrary,
                values.x,
                values.y,
                values.z)
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

    private struct FeatureInstance
    {
        public bool exists;
        public FeatureData feature;
        public Vector2 center;
        public float radius;
        public float aspect;
        public float rotation;
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
