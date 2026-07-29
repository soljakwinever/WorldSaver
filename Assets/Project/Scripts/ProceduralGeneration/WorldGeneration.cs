using System.Linq;
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
    
    private WorldData worldData;
    private readonly ITimeController timeController;
    [Inject] private BiomeData[] biomeLibrary;

    private uint seed;

    public uint Seed => seed;
    
    public float continentalNoiseScale = 46.5f;
    public float moistureNoiseScale = 32f;
    public float temperatureNoiseScale = 48.2f;
    public float erosionNoiseScale = 0.5f;
    public float valleyNoiseScale = 32;
    public float peakValleyNoiseScale = 8.5f;

    public float valleyDepth = 0.22f;
    public float valleyWidth = 0.12f;
    
    public int featureCellSize = 256;
    public float featureChancePerCell = 0.85f;

    public float minFeatureRadius = 64f;
    public float maxFeatureRadius = 256f;
    
    public WorldGeneration(WorldData worldData)
        : this(worldData, null)
    {
    }

    [Inject]
    public WorldGeneration(WorldData worldData, ITimeController timeController)
    {
        this.worldData = worldData;
        this.timeController = timeController;
        this.seed = (uint)worldData.seed;
        
        continentalNoise = new FastNoiseLite();
        continentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        continentalNoise.SetSeed(145679 + worldData.seed);
        continentalNoise.SetFractalType(FastNoiseLite.FractalType.None);
        
        
        moistureNoise = new FastNoiseLite();
        moistureNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        moistureNoise.SetSeed(645745 + worldData.seed);
        moistureNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        moistureNoise.SetFractalOctaves(3);
        moistureNoise.SetFractalLacunarity(2.720f);
        moistureNoise.SetFractalGain(0.45f);
        
        temperatureNoise = new FastNoiseLite();
        temperatureNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        temperatureNoise.SetSeed(324234 + worldData.seed);
        temperatureNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        temperatureNoise.SetFractalOctaves(3);
        temperatureNoise.SetFractalLacunarity(2.720f);
        temperatureNoise.SetFractalGain(0.45f);
        
        erosionNoise = new FastNoiseLite();
        erosionNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        erosionNoise.SetSeed(877555 + worldData.seed);
        erosionNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        erosionNoise.SetFractalOctaves(3);
        erosionNoise.SetFractalLacunarity(2.720f);
        erosionNoise.SetFractalGain(0.45f);
        
        roughnessNoise = new FastNoiseLite();
        roughnessNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        roughnessNoise.SetSeed(843221 + worldData.seed);
        roughnessNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        roughnessNoise.SetFractalOctaves(2);
        roughnessNoise.SetFractalGain(0.45f);

        // High-frequency FBm produces irregular, rough-edged patches.
        grassHeightNoise = new FastNoiseLite();
        grassHeightNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        grassHeightNoise.SetSeed(314159 + worldData.seed);
        grassHeightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        grassHeightNoise.SetFractalOctaves(5);
        grassHeightNoise.SetFractalLacunarity(2.65f);
        grassHeightNoise.SetFractalGain(0.58f);

        smallPoolsNoise = new FastNoiseLite();
        smallPoolsNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        smallPoolsNoise.SetSeed(271828 + worldData.seed);
        smallPoolsNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        smallPoolsNoise.SetFractalOctaves(3);
        smallPoolsNoise.SetFractalLacunarity(2.2f);
        smallPoolsNoise.SetFractalGain(0.5f);
        
        peakValleyNoise = new FastNoiseLite();
        peakValleyNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        peakValleyNoise.SetSeed(455534 + worldData.seed);
        
        volcanoNoise = new FastNoiseLite();
        volcanoNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        volcanoNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        volcanoNoise.SetFractalOctaves(3);
        volcanoNoise.SetFractalLacunarity(2.1f);
        volcanoNoise.SetFractalGain(0.45f);
        volcanoNoise.SetSeed(676767 + worldData.seed);
        
        volcanoRidgeNoise = new FastNoiseLite();
        volcanoRidgeNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        volcanoRidgeNoise.SetSeed(696969 + worldData.seed);
        volcanoRidgeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        volcanoRidgeNoise.SetFractalOctaves(2);
        
        valleyNoise = new FastNoiseLite();
        valleyNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        valleyNoise.SetFractalType(FastNoiseLite.FractalType.PingPong);
        valleyNoise.SetFractalOctaves(2);
        valleyNoise.SetSeed(969696 + worldData.seed);
        
        hillNoise = new FastNoiseLite();
        hillNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        hillNoise.SetSeed(81231 + worldData.seed);
        hillNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        hillNoise.SetFractalOctaves(3);
        hillNoise.SetFractalLacunarity(2.0f);
        hillNoise.SetFractalGain(0.45f);

        bumpNoise = new FastNoiseLite();
        bumpNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        bumpNoise.SetSeed(55991 + worldData.seed);
        bumpNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        bumpNoise.SetFractalOctaves(2);

        cliffNoise = new FastNoiseLite();
        cliffNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        cliffNoise.SetSeed(77128 + worldData.seed);
        cliffNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        cliffNoise.SetFractalOctaves(2);

        cliffMaskNoise = new FastNoiseLite();
        cliffMaskNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        cliffMaskNoise.SetSeed(91277 + worldData.seed);
        cliffMaskNoise.SetFractalType(FastNoiseLite.FractalType.None);
        
        outcropNoise = new FastNoiseLite();
        outcropNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        outcropNoise.SetSeed(882311 + worldData.seed);
        outcropNoise.SetFractalType(FastNoiseLite.FractalType.PingPong);
        outcropNoise.SetFractalOctaves(3);
        outcropNoise.SetFractalLacunarity(2.0f);
        outcropNoise.SetFractalGain(0.45f);

        outcropEdgeNoise = new FastNoiseLite();
        outcropEdgeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        outcropEdgeNoise.SetSeed(449812 + worldData.seed);
        outcropEdgeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        outcropEdgeNoise.SetFractalOctaves(2);
        outcropEdgeNoise.SetFractalLacunarity(2.0f);
        outcropEdgeNoise.SetFractalGain(0.45f);

        propNoise = new FastNoiseLite(667766 + worldData.seed);
    }

    private float ContinentalNoise(float x, float y) => Mathf.InverseLerp(-0.5f, 0.5f,continentalNoise.GetNoise(x / worldData.continentalNoiseScale, y / worldData.continentalNoiseScale));
    private float MoistureNoise(float x, float y) => Mathf.InverseLerp(-.5f,.5f,moistureNoise.GetNoise(x / worldData.moistureNoiseScale, y / worldData.moistureNoiseScale));
    private float TemperatureNoise(float x, float y) => Mathf.InverseLerp(-.5f, .5f,temperatureNoise.GetNoise(x / worldData.temperatureNoiseScale, y / worldData.temperatureNoiseScale));
    
    private float ErosionNoise(float x, float y) => Mathf.InverseLerp(-1f,1f,erosionNoise.GetNoise(x / worldData.erosionNoiseScale, y / worldData.erosionNoiseScale));

    private float PeakValleyNoise(float x, float y) => Mathf.InverseLerp(-0.5f, .5f,peakValleyNoise.GetNoise(x / worldData.peakValleyNoiseScale, y / worldData.peakValleyNoiseScale));
    private float LakeNoise(float x, float y) => Mathf.InverseLerp(-0.5f, .5f,peakValleyNoise.GetNoise(x / worldData.peakValleyNoiseScale*2, y / worldData.peakValleyNoiseScale*2));
    
        
    private float ValleyRaw(float x, float y)
    {
        return valleyNoise.GetNoise(x / valleyNoiseScale, y / valleyNoiseScale);
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
        float scale = Mathf.Max(0.01f, worldData.grassHeightNoiseScale);
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

        if (worldData.heightMapDebug)
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
            isWater = height <= worldData.waterHeight,
            isRoad = false,
            isTrail = false
        };
    }

    private float SelectNoiseLayer(int x, int y, float moisture, float temperature, BiomeBlend biomeData = default)
    {
        float height = Mathf.Lerp(worldData.waterHeight, worldData.mountainHeight, 0.5f);
        switch (worldData.previewNoiseLayer)
        {
            case WorldData.NoiseLayer.Hill:
                height = ApplyBiomeMicroTerrain(x, y, height, biomeData);
                break;
            case WorldData.NoiseLayer.PeakValley:
                height = PeakValleyNoise(x, y);
                break;
            case WorldData.NoiseLayer.Height:
                return GetHeight(x, y, out BiomeBlend _, out float _, out float _, out float _);
            case WorldData.NoiseLayer.MountainIsland:
                height = ApplyMountainIslands(x, y, height, biomeData.dominantBiome);
                break;
            case WorldData.NoiseLayer.LocalLandforms:
                ApplyLocalLandforms(x,y, ref height, moisture, temperature, ref biomeData);
                break;
            case WorldData.NoiseLayer.Lakes:
                height=ApplyLakes(x, y, height, biomeData.lakeStrength);
                break;
            case WorldData.NoiseLayer.GrassHeight:
                return GrassHeightNoise(x, y);
            case WorldData.NoiseLayer.SmallPools:
                return SmallPoolsNoise(x, y);
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
    private float GetHeight(int x, int y, out BiomeBlend biomeData, out float baseHeight, out float moisture, out float temperature, int sampleRadius = 2)
    {
        float continentalNoise = ContinentalNoise(x, y);
        float peakValleyNoise = PeakValleyNoise(x, y);
        float erosionNoise = ErosionNoise(x, y);
        float lakeNoise = Mathf.Lerp(worldData.lakeDepth, 0,LakeNoise(x, y));
        
        //float height = baseHeight = continentalNoise;
        
        var blendedValues = SampleBlendedTerrainValues(x, y, sampleRadius);

        float height = baseHeight = blendedValues.x;
        moisture = blendedValues.y;
        temperature = blendedValues.z;
        
        biomeData = BiomeSelector.GetBiomeBlend(biomeLibrary, blendedValues.x, blendedValues.y, blendedValues.z);
        SeasonalBiomeTint.Apply(
            ref biomeData,
            worldData,
            timeController,
            x,
            y);
        
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
        
        //ApplyLocalLandforms(x,y, ref height, moisture, temperature, ref biomeData);
        
        height = Mathf.InverseLerp(-.1f,1.25f,height);

        //height = PeakValleyNoise(x, y);
        
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

        return new ChunkBuildResult.IsCliff(upwardJump > worldData.cliffHeight &&
                                            h > worldData.waterHeight + 0.04f, Mathf.Approximately(highestNeighbor, hD) || Mathf.Approximately(highestNeighbor, hU));
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
            int x = Random.Range(-searchRadius, searchRadius);
            int y = Random.Range(-searchRadius, searchRadius);
            
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
        float landMask = SmoothStep(worldData.beachHeight+0.1f, worldData.mountainHeight-0.1f, height);

        var lakeNoise = Mathf.InverseLerp(-1f,1f,peakValleyNoise.GetNoise(x / worldData.lakeNoiseScale, y / worldData.lakeNoiseScale));
        
        float lake = Mathf.Lerp(-(worldData.lakeDepth * lakeStrength), 0, lakeNoise);
        
        height += lake * landMask;
        
        return height;
    }

    private float SmallPoolsNoise(int x, int y)
    {
        float scale = Mathf.Max(0.01f, worldData.SmallPoolsScale);
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
                      Mathf.Max(0f, worldData.SmallPoolsStrength) *
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
        float landMask = SmoothStep(worldData.waterHeight, worldData.mountainHeight-0.1f, height);
        
        var noise = outcropNoise.GetNoise(x/ worldData.outCropNoiseScale, y/ worldData.outCropNoiseScale);
        var inter = Mathf.InverseLerp(-1f, 1f, noise);

        if (inter > worldData.outCropThreshold)
        {
            height += worldData.outCropStrength * biome.cliffStrength * landMask;
        }
        
        return height;
    }
    
    private float ApplyBiomeMicroTerrain(int x, int y, float height, BiomeBlend biomeData)
    {
        var offset = Mathf.Lerp(worldData.waterHeight, worldData.mountainHeight, 0.65f);
        float landMask = SmoothStep(worldData.waterHeight, offset, height);
        
        float mountainFade = 1f- SmoothStep(worldData.mountainHeight, 1, height);
        
        float localTerrainMask = landMask * mountainFade;
        
        if(localTerrainMask <= 0f)
            return height;
        
        float hills = hillNoise.GetNoise(x/biomeData.hillScale, y/biomeData.hillScale);
        
        hills = Mathf.InverseLerp(-1f, 1f, hills);
        hills = hills * 2f - 1f;

        height += hills * worldData.hillStrength * biomeData.hillStrength *localTerrainMask;

        float bumps = bumpNoise.GetNoise(
            x / biomeData.bumpScale,
            y / biomeData.bumpScale
        );

        bumps = Mathf.InverseLerp(-1f, 1f, bumps);
        bumps = bumps * 2f - 1f;
        
        height += bumps * worldData.bumpStrength * biomeData.bumpStrength * localTerrainMask;
        
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

        float valleyMask = 1f - SmoothStep(0.02f, valleyWidth, n);

        float highlandMask = SmoothStep(worldData.beachHeight, worldData.mountainHeight, height);

        height -= valleyMask * highlandMask * valleyDepth * biomeValleyStrength;

        return height;
    }
    
    private void ApplyLocalLandforms(int x, int y, ref float height, float moisture, float temperature, ref BiomeBlend biomeData)
    {
        int cellX = Mathf.FloorToInt((float)x / featureCellSize);
        int cellY = Mathf.FloorToInt((float)y / featureCellSize);


        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                int cX = cellX + ox;
                int cY = cellY + oy;

                float existsRoll = Util.Hash01(cX, cY, 1);

                if (existsRoll > featureChancePerCell)
                {
                    continue;
                }

                float centerOffsetX = Mathf.Lerp(0.2f, 0.8f, Util.Hash01(cX, cY, 2));
                float centerOffsetY = Mathf.Lerp(0.2f, 0.8f, Util.Hash01(cX, cY, 3));

                float centerX = (cX +centerOffsetX) * featureCellSize;
                float centerY = (cY + centerOffsetY) * featureCellSize;
                
                float radius = Mathf.Lerp(
                    minFeatureRadius,
                    maxFeatureRadius,
                    Util.Hash01(cX, cY, 4));

                float dX = x - centerX;
                float dY = y - centerY;
                float distance = Mathf.Sqrt(dX * dX + dY * dY);
                
                if(distance > radius)
                    continue;
                
                float  d = distance / radius;
                float typeRoll = Util.Hash01(cX, cY, 5);
                
                float landMask = SmoothStep(0.28f, 0.55f, height);

                if (typeRoll < 0.33f)
                {
                    //Volcanos can appear anywhere but hotter areas make them stronger
                    float hotMask = Mathf.Lerp(0.65f,1f, SmoothStep(0.45f, 0.85f, temperature));
                    //
                    // var lava = biomeLibrary.FirstOrDefault(t => t.biomeName.StartsWith("Volcano"));
                    //
                    // if (lava != null)
                    // {
                    //     biomeData = BiomeSelector.BlendBiomes(biomeData.dominantBiome, lava, landMask * hotMask);
                    // }
                    
                    ApplyVolcano(
                        ref height,
                        x,
                        y,
                        centerX,
                        centerY,
                        radius,
                        landMask * hotMask
                    );
                }
            }
        }
    }

    private void ApplyVolcano(
            ref float height,
            float x,
            float y,
            float centerX,
            float centerY,
            float radius,
            float weight
        )
    {
        if (weight <= 0f)
            return;

        float dx = x - centerX;
        float dy = y - centerY;

        float distance = Mathf.Sqrt(dx * dx + dy * dy);
        float baseD = distance / radius;

        if (baseD > 1.25f)
            return;

        float angle = Mathf.Atan2(dy, dx);

        // ------------------------------------------------------------
        // 1. Break the circular outline
        // ------------------------------------------------------------

        float edgeNoise = volcanoNoise.GetNoise(
            x * 0.035f,
            y * 0.035f
        );

        // Stronger near the edge, weaker near the crater.
        float edgeMask = SmoothStep(0.35f, 1f, baseD);

        // Distort the volcano radius.
        float distortedD = baseD + edgeNoise * 0.18f * edgeMask;

        // ------------------------------------------------------------
        // 2. Add volcanic ridges running down the slope
        // ------------------------------------------------------------

        int ridgeCount = 13;

        // Angular ridges.
        float angularRidges = Mathf.Sin(angle * ridgeCount);

        // Convert sine wave into sharp raised ridges.
        angularRidges = Mathf.Pow(Mathf.Abs(angularRidges), 5f);

        // Invert so ridges become narrow crests instead of broad bands.
        angularRidges = 1f - angularRidges;

        // Add unevenness so ridges are not perfect spokes.
        float ridgeNoise = volcanoRidgeNoise.GetNoise(
            x * 0.075f,
            y * 0.075f
        );

        angularRidges += ridgeNoise * 0.45f;
        angularRidges = Mathf.Clamp01(angularRidges);

        // Ridges mostly appear on the cone slope, not inside the crater.
        float ridgeSlopeMask =
            SmoothStep(0.18f, 0.45f, distortedD) *
            (1f - SmoothStep(0.88f, 1.05f, distortedD));

        float ridges = angularRidges * ridgeSlopeMask * 0.12f;

        // ------------------------------------------------------------
        // 3. Main volcano shape
        // ------------------------------------------------------------

        float cone = Mathf.Pow(1f - Mathf.Clamp01(distortedD), 1.35f) * 0.55f;

        float rim = Ring(distortedD, 0.23f, 0.075f) * 0.27f;

        float craterBowl =
            (1f - SmoothStep(0f, 0.43f, distortedD)) * 0.50f;

        // ------------------------------------------------------------
        // 4. Eroded gullies between ridges
        // ------------------------------------------------------------

        float gullyMask = 1f - angularRidges;

        float gullies =
            gullyMask *
            ridgeSlopeMask *
            SmoothStep(0.28f, 0.95f, distortedD) *
            0.10f;

        height += ((cone + rim + ridges - craterBowl - gullies)*8f) * weight;
    }
    
    private void ApplyImpactCrater(ref float height, float d, float weight)
    {
        if (weight <= 0f)
            return;

        // Depression in the middle.
        float bowl = (1f - SmoothStep(0f, 0.58f, d)) * 0.38f;

        // Raised outer rim.
        float rim = Ring(d, 0.62f, 0.12f) * 0.25f;

        // Light outer ejecta mound.
        float ejecta = (1f - SmoothStep(0.62f, 1f, d)) * 0.05f;

        height += (rim + ejecta - bowl) * weight;
    }
    
    private void ApplyMesa(ref float height, float d, float weight, float mesaLevel)
    {
        if (weight <= 0f)
            return;

        // Flat top, sharp-ish sides.
        float plateau = Plateau(d, 0.42f, 0.82f);

        // Blend the inner area toward a fixed height level.
        // This creates the tabletop instead of just adding more noisy terrain.
        height = Mathf.Lerp(height, mesaLevel, plateau * weight * 0.95f);

        // Small raised lip near the mesa edge.
        float edgeLip = Ring(d, 0.45f, 0.08f) * 0.06f;
        height += edgeLip * weight;
    }
    
    #endregion

    #region Shaping

    private static float Ring(float d, float center, float width)
    {
        float t = Mathf.Abs(d - center) / width;
        return 1f - Smooth01(t);
    }
    
    private static float Plateau(float d, float innerRadius, float outerRadius)
    {
        if (d <= innerRadius)
            return 1f;

        if (d >= outerRadius)
            return 0f;

        float t = Mathf.InverseLerp(innerRadius, outerRadius, d);
        return 1f - Smooth01(t);
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
