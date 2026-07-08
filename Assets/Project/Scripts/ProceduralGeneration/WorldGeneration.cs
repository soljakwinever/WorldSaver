using Project.Scripts;
using UnityEngine;
using Zenject;


public class WorldGeneration
{
    FastNoiseLite continentalNoise;
    FastNoiseLite moistureNoise;
    FastNoiseLite temperatureNoise;
    FastNoiseLite erosionNoise;
    FastNoiseLite peakValleyNoise;
    FastNoiseLite valleyNoise;
    FastNoiseLite roughnessNoise;
    
    private FastNoiseLite outcropNoise;
    private FastNoiseLite outcropEdgeNoise;

    private FastNoiseLite propNoise;
    
    FastNoiseLite hillNoise;
    FastNoiseLite bumpNoise;
    FastNoiseLite cliffNoise;
    FastNoiseLite cliffMaskNoise;
    
    FastNoiseLite volcanoNoise;
    FastNoiseLite volcanoRidgeNoise;
    
    [Inject] private WorldData worldData;
    [Inject] private BiomeData[] biomeLibrary;
    
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
    
    public WorldGeneration()
    {
        continentalNoise = new FastNoiseLite();
        continentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        continentalNoise.SetSeed(145679);
        continentalNoise.SetFractalType(FastNoiseLite.FractalType.None);
        
        
        moistureNoise = new FastNoiseLite();
        moistureNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        moistureNoise.SetSeed(645745);
        
        temperatureNoise = new FastNoiseLite();
        temperatureNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        temperatureNoise.SetSeed(324234);
        
        erosionNoise = new FastNoiseLite();
        erosionNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        erosionNoise.SetSeed(877555);
        erosionNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        erosionNoise.SetFractalOctaves(3);
        erosionNoise.SetFractalLacunarity(2.720f);
        erosionNoise.SetFractalGain(0.45f);
        
        roughnessNoise = new FastNoiseLite();
        roughnessNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        roughnessNoise.SetSeed(843221);
        roughnessNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        roughnessNoise.SetFractalOctaves(2);
        roughnessNoise.SetFractalGain(0.45f);
        
        peakValleyNoise = new FastNoiseLite();
        peakValleyNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        peakValleyNoise.SetSeed(455534);
        
        volcanoNoise = new FastNoiseLite();
        volcanoNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        volcanoNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        volcanoNoise.SetFractalOctaves(3);
        volcanoNoise.SetFractalLacunarity(2.1f);
        volcanoNoise.SetFractalGain(0.45f);
        volcanoNoise.SetSeed(676767);
        
        volcanoRidgeNoise = new FastNoiseLite();
        volcanoRidgeNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        volcanoRidgeNoise.SetSeed(696969);
        volcanoRidgeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        volcanoRidgeNoise.SetFractalOctaves(2);
        
        valleyNoise = new FastNoiseLite();
        valleyNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        valleyNoise.SetFractalType(FastNoiseLite.FractalType.PingPong);
        valleyNoise.SetFractalOctaves(2);
        valleyNoise.SetSeed(969696);
        
        hillNoise = new FastNoiseLite();
        hillNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        hillNoise.SetSeed(81231);
        hillNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        hillNoise.SetFractalOctaves(3);
        hillNoise.SetFractalLacunarity(2.0f);
        hillNoise.SetFractalGain(0.45f);

        bumpNoise = new FastNoiseLite();
        bumpNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        bumpNoise.SetSeed(55991);
        bumpNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        bumpNoise.SetFractalOctaves(2);

        cliffNoise = new FastNoiseLite();
        cliffNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        cliffNoise.SetSeed(77128);
        cliffNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        cliffNoise.SetFractalOctaves(2);

        cliffMaskNoise = new FastNoiseLite();
        cliffMaskNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        cliffMaskNoise.SetSeed(91277);
        cliffMaskNoise.SetFractalType(FastNoiseLite.FractalType.None);
        
        outcropNoise = new FastNoiseLite();
        outcropNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        outcropNoise.SetSeed(882311);
        outcropNoise.SetFractalType(FastNoiseLite.FractalType.PingPong);
        outcropNoise.SetFractalOctaves(3);
        outcropNoise.SetFractalLacunarity(2.0f);
        outcropNoise.SetFractalGain(0.45f);

        outcropEdgeNoise = new FastNoiseLite();
        outcropEdgeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        outcropEdgeNoise.SetSeed(449812);
        outcropEdgeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        outcropEdgeNoise.SetFractalOctaves(2);

        propNoise = new FastNoiseLite(667766);
    }

    private float ContinentalNoise(float x, float y) => continentalNoise.GetNoise(x / worldData.continentalNoiseScale, y / worldData.continentalNoiseScale);
    private float MoistureNoise(float x, float y) => Mathf.InverseLerp(-1f,1f,moistureNoise.GetNoise(x / worldData.moistureNoiseScale, y / worldData.moistureNoiseScale));
    private float TemperatureNoise(float x, float y) => Mathf.InverseLerp(-1f, 1f,temperatureNoise.GetNoise(x / worldData.temperatureNoiseScale, y / worldData.temperatureNoiseScale));
    
    private float ErosionNoise(float x, float y) => Mathf.InverseLerp(-1f,1f,erosionNoise.GetNoise(x / worldData.erosionNoiseScale, y / worldData.erosionNoiseScale));

    private float PeakValleyNoise(float x, float y) => Mathf.InverseLerp(-0.5f, .5f,peakValleyNoise.GetNoise(x / worldData.peakValleyNoiseScale, y / worldData.peakValleyNoiseScale));
    
    
    public float PropNoise(int worldX, int worldY, float ruleNoiseScale)
    {
        return propNoise.GetNoise(worldX / ruleNoiseScale, worldY / ruleNoiseScale);
    }
    
    private int GetTileIndex(float continentalNoise)
    {
        return 0;
    }
    
    public int GetTile(int x, int y, out BiomeSelector.BiomeBlend biomeData, out float height, out float moisture, out float temperature)
    {
        height = GetHeight(x, y, out biomeData, out float baseHeight, out moisture, out temperature);
        
        return GetTileIndex(height);
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
    private float GetHeight(int x, int y, out BiomeSelector.BiomeBlend biomeData, out float baseHeight, out float moisture, out float temperature, int sampleRadius = 2)
    {
        float continentalNoise = ContinentalNoise(x, y);
        float peakValleyNoise = PeakValleyNoise(x, y);
        float erosionNoise = ErosionNoise(x, y);
        
        moisture = MoistureNoise(x, y);
        temperature = TemperatureNoise(x, y);
        
        float height = baseHeight = continentalNoise;
        
        var blendedValues = SampleBlendedTerrainValues(x, y, sampleRadius);
        
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
        
        height = ApplyValleys(x, y, height, biomeData.valleyStrength);
        
        height = ApplyBiomeMicroTerrain(x, y, height, biomeData);
        height = ApplyMountainIslands(x, y, height, biomeData.dominantBiome);
        
        ApplyLocalLandforms(x,y, ref height, moisture, temperature);
        
        height = Mathf.InverseLerp(-.1f,1f,height);

        //height = PeakValleyNoise(x, y);
        
        return height;
    }
    
    public ChunkGenerator.TerrainSample GetTerrainSample(int x, int y)
    {
        var height = GetHeight(x, y, out BiomeSelector.BiomeBlend biomeData, out float baseHeight, out float moisture, out float temperature);
        return new ChunkGenerator.TerrainSample()
        {
            biome = biomeData.dominantBiome,
            height = height,
            moisture = moisture,
            temperature = temperature,
                    
            isCliff = IsSmallCliff(x,y),
            isWater = height <= worldData.waterHeight,
            isRoad = false,
            isTrail = false
        };
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
    
    private float ApplyBiomeMicroTerrain(int x, int y, float height, BiomeSelector.BiomeBlend biomeData)
    {
        float landMask = SmoothStep(worldData.waterHeight, worldData.mountainHeight-0.2f, height);
        
        float mountainFade = 1f- SmoothStep(worldData.mountainHeight, 0.88f, height);
        
        float localTerrainMask = landMask * mountainFade;
        
        if(localTerrainMask <= 0f)
            return height;
        
        float hills = hillNoise.GetNoise(x/biomeData.hillScale, y/biomeData.hillScale);
        
        hills = Mathf.InverseLerp(-1f, 1f, hills);
        hills = hills * 2f - 1f;

        height += hills * biomeData.hillStrength *localTerrainMask;

        float bumps = bumpNoise.GetNoise(
            x / biomeData.bumpScale,
            y / biomeData.bumpScale
        );

        bumps = Mathf.InverseLerp(-1f, 1f, bumps);
        bumps = bumps * 2f - 1f;
        
        height += bumps * biomeData.bumpStrength * localTerrainMask;
        
        height = ApplySmallCliffs(x,y,height,biomeData, localTerrainMask);
        
        return height;
    }

    private float ApplySmallCliffs(int x, int y, float height, BiomeSelector.BiomeBlend biomeData, float localTerrainMask)
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

    public bool IsSmallCliff(int x, int y)
    {
        GetTile(x, y, out BiomeSelector.BiomeBlend _, out float h, out float _, out float _);
        
        GetTile(x+1, y, out BiomeSelector.BiomeBlend _, out float hR, out float _, out float _);
        GetTile(x-1, y, out BiomeSelector.BiomeBlend _, out float hL, out float _, out float _);
        
        GetTile(x, y+1, out BiomeSelector.BiomeBlend _, out float hU, out float _, out float _);
        GetTile(x, y-1, out BiomeSelector.BiomeBlend _, out float hD, out float _, out float _);

        float maxSlope = 0f;
        maxSlope = Mathf.Max(maxSlope, Mathf.Abs( h - hR));
        maxSlope = Mathf.Max(maxSlope, Mathf.Abs( h - hL));
        maxSlope = Mathf.Max(maxSlope, Mathf.Abs( h - hU));
        maxSlope = Mathf.Max(maxSlope, Mathf.Abs( h - hD));

        return maxSlope > worldData.cliffHeight && h > worldData.waterHeight && h < worldData.mountainHeight;

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
                
                float height = GetHeight(sampleX, sampleY, out BiomeSelector.BiomeBlend biomeData, out float _, out float _, out float _, 1);
                
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
                float height = GetHeight(centerX + x, centerY + y, out BiomeSelector.BiomeBlend biomeData, out float _, out float _, out float _, 1);

                if (height < min)
                    min = height;

                if (height > max)
                    max = height;
            }
        }

        return max - min;
    }
    
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

    private float ApplyValleys(int x, int y, float height, float biomeValleyStrength)
    {
        float n = Mathf.Abs(ValleyRaw(x, y));

        float valleyMask = 1f - SmoothStep(0.02f, valleyWidth, n);

        float highlandMask = SmoothStep(0.35f, 0.85f, height);

        height -= valleyMask * highlandMask * valleyDepth * biomeValleyStrength;

        return height;
    }
    
    private void ApplyLocalLandforms(int x, int y, ref float height, float moisture, float temperature)
    {
        int cellX = Mathf.FloorToInt((float)x / featureCellSize);
        int cellY = Mathf.FloorToInt((float)y / featureCellSize);


        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                int cX = cellX + ox;
                int cY = cellY + oy;

                float existsRoll = Hash01(cX, cY, 1);

                if (existsRoll > featureChancePerCell)
                {
                    continue;
                }

                float centerOffsetX = Mathf.Lerp(0.2f, 0.8f, Hash01(cX, cY, 2));
                float centerOffsetY = Mathf.Lerp(0.2f, 0.8f, Hash01(cX, cY, 3));

                float centerX = (cX +centerOffsetX) * featureCellSize;
                float centerY = (cY + centerOffsetY) * featureCellSize;
                
                float radius = Mathf.Lerp(
                    minFeatureRadius,
                    maxFeatureRadius,
                    Hash01(cX, cY, 4));

                float dX = x - centerX;
                float dY = y - centerY;
                float distance = Mathf.Sqrt(dX * dX + dY * dY);
                
                if(distance > radius)
                    continue;
                
                float  d = distance / radius;
                float typeRoll = Hash01(cX, cY, 5);
                
                float landMask = SmoothStep(0.28f, 0.55f, height);

                if (typeRoll < 0.33f)
                {
                    //Volcanos can appear anywhere but hotter areas make them stronger
                    float hotMask = Mathf.Lerp(0.65f,1f, SmoothStep(0.45f, 0.85f, temperature));
                    
                    ApplyVolcano(ref height, d, landMask * hotMask);
                }
            }
        }
    }

    private void ApplyVolcano(ref float height, float d, float weight)
    {
        if(weight <= 0f)
            return;
        
        //Cone rises from the center
        float coneHeight = Mathf.Pow(1f - d, 1.35f) * 0.55f;

        float rim = Ring(d, 0.22f, 0.08f) * 0.24f;

        float craterBowl = (1f - SmoothStep(0f, 0.22f, d)) * 0.48f;
        
        height += (coneHeight + rim - craterBowl) * weight;
    }
    
    
    private float ValleyRaw(float x, float y)
    {
        return valleyNoise.GetNoise(x / valleyNoiseScale, y / valleyNoiseScale);
    }
    
    private float GetRoughness(float x, float y)
    {
        return roughnessNoise.GetNoise(x / 12f, y / 12f);
    }

    private static float Ring(float d, float center, float width)
    {
        float t = Mathf.Abs(d - center) / width;
        return 1f - Smooth01(t);
    }

    
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

    public static float Hash01(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393);
            h ^= (uint)(y * 668265263);
            h ^= (uint)(salt * 1442695041);

            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;

            return h / 4294967295f;
        }
    }
}
