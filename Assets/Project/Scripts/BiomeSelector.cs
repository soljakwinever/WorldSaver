using System.Collections.Generic;
using Project.Scripts;
using UnityEngine;

public static class BiomeSelector
{
    public struct BiomeBlend
    {
        public BiomeData dominantBiome;

        public float heightOffset;
        public float heightMultiplier;
        public float erosionStrength;
        public float mountainStrength;
        public float valleyStrength;
        public float roughnessStrength;
        
        public float volcanoChance;
        public float craterChance;
        public float mesaChance;
        public float townChance;
        
        public float hillStrength;
        public float hillScale;

        public float bumpStrength;
        public float bumpScale;

        public float cliffChance;
        public float cliffStrength;
        public float cliffScale;
        
        public Color groundColor;
        public Color pathColor;
        public Color waterColor;
        public Color cliffColor;
        public Color beachColor;
    }

    public const int biomeBlendCount = 4;

    public const float biomeBlendPower = 1.35f;
    
    public const float biomeHeightImportance = 0.65f;

    public static BiomeBlend GetBiomeBlend(BiomeData[] biomeData, float baseHeight, float moisture, float temperature)
    {
        int count = Mathf.Max(1, biomeBlendCount);
        
        WeightedBiome[] closestBiomes = new WeightedBiome[count];

        for (int i = 0; i < closestBiomes.Length; i++)
        {
            closestBiomes[i].distance = float.MaxValue;
            closestBiomes[i].weight = 0f;
            closestBiomes[i].biome = null;
        }

        foreach (var biome in biomeData)
        {
            float distance = GetBiomeDistance(biome, baseHeight, moisture, temperature);

            for (int i = 0; i < closestBiomes.Length; i++)
            {
                if (distance < closestBiomes[i].distance)
                {
                    for (int j = closestBiomes.Length - 1; j > i; j--)
                    {
                        closestBiomes[j] = closestBiomes[j - 1];
                    }

                    closestBiomes[i] = new WeightedBiome
                    {
                        biome = biome,
                        distance = distance,
                        weight = 0f
                    };

                    break;
                }
            }
        }

        float totalWeight = 0f;

        for (int i = 0; i < closestBiomes.Length; i++)
        {
            if(closestBiomes[i].biome == null)
                continue;
            
            float weight = 1f / Mathf.Pow(closestBiomes[i].distance + 0.001f, biomeBlendPower);
            
            closestBiomes[i].weight = weight;
            totalWeight += weight;
        }
        
        if(totalWeight <= 0f)
            return CreateSingleBiomeBlend(biomeData[0]);
        
        BiomeBlend blend = new BiomeBlend();
        
        blend.dominantBiome = closestBiomes[0].biome;

        blend.groundColor = Color.black;
        blend.pathColor = Color.black;
        blend.waterColor = Color.black;
        blend.cliffColor = Color.black;
        blend.beachColor = Color.black;

        for (int i = 0; i < closestBiomes.Length; i++)
        {
            var biome = closestBiomes[i].biome;
            
            if(biome == null)
                continue;
            
            float weight = closestBiomes[i].weight / totalWeight;
            
            blend.heightOffset += biome.heightOffset * weight;
            blend.heightMultiplier += biome.heightMultiplier * weight;
            blend.erosionStrength += biome.erosionStrength * weight;
            blend.mountainStrength += biome.mountainStrength * weight;
            blend.valleyStrength += biome.valleyStrength * weight;
            blend.roughnessStrength += biome.roughnessStrength * weight;
            
            blend.volcanoChance += biome.volcanoChance * weight;
            blend.craterChance += biome.craterChance * weight;
            blend.mesaChance += biome.mesaChance * weight;
            blend.townChance += biome.townChance * weight;
            
            blend.cliffChance += biome.cliffChance * weight;
            blend.cliffStrength += biome.cliffStrength * weight;
            blend.cliffScale += biome.cliffScale * weight;
            
            blend.hillStrength += biome.hillStrength * weight;
            blend.hillScale += biome.hillScale * weight;
            
            blend.bumpStrength += biome.bumpStrength * weight;
            blend.bumpScale += biome.bumpScale * weight;
            
            blend.groundColor += biome.groundColor * weight;
            blend.pathColor += biome.pathColor * weight;
            blend.waterColor += biome.waterColor * weight;
            blend.cliffColor += biome.cliffColor * weight;
            blend.beachColor += biome.beachColor * weight;
        }
        
        return blend;
    }
    
    

    private static BiomeBlend CreateSingleBiomeBlend(BiomeData biome)
    {
        return new BiomeBlend
        {
            dominantBiome = biome,

            heightOffset = biome.heightOffset,
            heightMultiplier = biome.heightMultiplier,
            erosionStrength = biome.erosionStrength,
            mountainStrength = biome.mountainStrength,
            valleyStrength = biome.valleyStrength,
            roughnessStrength = biome.roughnessStrength,

            volcanoChance = biome.volcanoChance,
            craterChance = biome.craterChance,
            mesaChance = biome.mesaChance,
            townChance = biome.townChance,

            groundColor = biome.groundColor,
            pathColor = biome.pathColor,
            waterColor = biome.waterColor,
            cliffColor = biome.cliffColor,
            beachColor = biome.beachColor
        };
    }

    private struct WeightedBiome
    {
        public BiomeData biome;
        public float distance;
        public float weight;
    }
    
    public static BiomeData GetClosestBiome(
        IReadOnlyList<BiomeData> biomes,
        float height,
        float moisture,
        float temperature
    )
    {
        var closestBiome = biomes[0];
        float closestDistance = float.MaxValue;

        foreach (var biome in biomes)
        {
            if (!IsValidBiome(biome, height, moisture, temperature))
                continue;

            float distance = GetBiomeDistance(
                biome,
                height,
                moisture,
                temperature
            );

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestBiome = biome;
            }
        }

        if (closestBiome == null)
        {
            closestBiome = biomes[1];
        }
        return closestBiome;
    }

    private static bool IsValidBiome(
        BiomeData biome,
        float height,
        float moisture,
        float temperature
    )
    {
        bool validHeight =
            Mathf.Abs(height - biome.height) <= biome.heightVariance;

        bool validMoisture =
            Mathf.Abs(moisture - biome.moisture) <= biome.moistureVariance;

        bool validTemperature =
            Mathf.Abs(temperature - biome.temperature) <= biome.temperatureVariance;

        return validHeight && validMoisture && validTemperature;
    }

    private static float GetBiomeDistance(
        BiomeData biome,
        float height,
        float moisture,
        float temperature
    )
    {
        float temperatureVariance = Mathf.Max(0.001f, biome.temperatureVariance);
        float moistureVariance = Mathf.Max(0.001f, biome.moistureVariance);
        float heightVariance = Mathf.Max(0.001f, biome.heightVariance);

        float temperatureDifference = (temperature - biome.temperature) / temperatureVariance;
        float moistureDifference = (moisture - biome.moisture) / moistureVariance;
        float heightDifference = (height - biome.height) / heightVariance;

        return
            temperatureDifference * temperatureDifference +
            moistureDifference * moistureDifference +
            heightDifference * heightDifference * biomeHeightImportance;
    }
}