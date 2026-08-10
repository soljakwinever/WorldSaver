using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public static class WorldGenerationPresetDefaults
    {
        public static WorldGenerationPresetData CreateFromLegacy(
            Project.Scripts.WorldData world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            ClimateLayerData climate = Create<ClimateLayerData>();
            climate.useLegacyResourceBiomes = true;
            climate.continentalNoiseScale = world.continentalNoiseScale;
            climate.moistureNoiseScale = world.moistureNoiseScale;
            climate.temperatureNoiseScale = world.temperatureNoiseScale;

            ElevationLayerData elevation = Create<ElevationLayerData>();
            elevation.waterHeight = world.waterHeight;
            elevation.beachHeight = world.beachHeight;
            elevation.mountainHeight = world.mountainHeight;
            elevation.cliffHeight = world.cliffHeight;
            elevation.erosionNoiseScale = world.erosionNoiseScale;
            elevation.peakValleyNoiseScale = world.peakValleyNoiseScale;

            LakeLayerData lakes = Create<LakeLayerData>();
            lakes.depth = world.lakeDepth;
            lakes.noiseScale = world.lakeNoiseScale;

            SmallPoolLayerData pools = Create<SmallPoolLayerData>();
            pools.noiseScale = world.SmallPoolsScale;
            pools.strength = world.SmallPoolsStrength;

            ValleyLayerData valleys = Create<ValleyLayerData>();
            valleys.noiseScale = world.valleyNoiseScale;
            valleys.depth = world.valleyDepth;
            valleys.width = world.valleyWidth;

            BiomeMicroTerrainLayerData micro =
                Create<BiomeMicroTerrainLayerData>();
            micro.hillStrength = world.hillStrength;
            micro.bumpStrength = world.bumpStrength;

            OutcropLayerData outcrops = Create<OutcropLayerData>();
            outcrops.strength = world.outCropStrength;
            outcrops.threshold = world.outCropThreshold;
            outcrops.noiseScale = world.outCropNoiseScale;

            FeatureCellLayerData features = Create<FeatureCellLayerData>();
            features.cellSize = world.featureCellSize;
            features.chancePerCell = world.featureChancePerCell;
            features.features =
                world.legacyFeatures ?? Array.Empty<FeatureData>();

            SurfaceDetailLayerData surface = Create<SurfaceDetailLayerData>();
            surface.useLegacyWorldPropRules = false;
            surface.grassHeightNoiseScale = world.grassHeightNoiseScale;
            surface.propSpawnRules =
                world.propSpawnRules ?? Array.Empty<PropSpawnRule>();

            WorldGenerationPresetData preset =
                Create<WorldGenerationPresetData>();
            preset.name = "Legacy Runtime Preset";
            preset.heightMapDebug = world.heightMapDebug;
            preset.previewLayer = ConvertPreview(world.previewNoiseLayer);
            preset.climate = climate;
            preset.elevation = elevation;
            preset.lakes = lakes;
            preset.smallPools = pools;
            preset.valleys = valleys;
            preset.microTerrain = micro;
            preset.outcrops = outcrops;
            preset.features = features;
            preset.surfaceDetails = surface;
            preset.useLegacyWorldNPCSpawnRules = false;
            preset.enemySpawnRules =
                world.enemySpawnRules ?? Array.Empty<EnemySpawnRule>();
            preset.allowEventNPCSpawnRules = true;
            return preset;
        }

        private static T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            value.hideFlags = HideFlags.DontSave;
            return value;
        }

        private static WorldGenerationPreviewLayer ConvertPreview(
            Project.Scripts.WorldData.NoiseLayer layer) =>
            layer switch
            {
                Project.Scripts.WorldData.NoiseLayer.Moisture =>
                    WorldGenerationPreviewLayer.Moisture,
                Project.Scripts.WorldData.NoiseLayer.Temperature =>
                    WorldGenerationPreviewLayer.Temperature,
                Project.Scripts.WorldData.NoiseLayer.PeakValley =>
                    WorldGenerationPreviewLayer.PeakValley,
                Project.Scripts.WorldData.NoiseLayer.Lakes =>
                    WorldGenerationPreviewLayer.Lakes,
                Project.Scripts.WorldData.NoiseLayer.SmallPools =>
                    WorldGenerationPreviewLayer.SmallPools,
                Project.Scripts.WorldData.NoiseLayer.GrassHeight =>
                    WorldGenerationPreviewLayer.GrassHeight,
                _ => WorldGenerationPreviewLayer.Height
            };
    }
}
