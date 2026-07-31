using System;
using System.Collections.Generic;
using Project.Scripts.Enums;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Flags]
    public enum SeasonMask
    {
        None = 0,
        Spring = 1 << 0,
        Summer = 1 << 1,
        Autumn = 1 << 2,
        Winter = 1 << 3,
        All = Spring | Summer | Autumn | Winter
    }

    public enum NPCPersistence
    {
        Transient,
        Persistent
    }

    [Serializable]
    public sealed class EnemySpawnVariation
    {
        public EnemyData enemyData;

        [Range(0f, 1f)]
        [Tooltip("Absolute chance that this variation replaces the default enemy. Variations are checked in list order; unused probability falls back to the default.")]
        public float chance;
    }

    /// <summary>
    /// Describes one NPC population and the world conditions under which it may spawn.
    /// Assets can also act as rule sets by referencing additional rules.
    /// </summary>
    [CreateAssetMenu(fileName = "Enemy Spawn Rule", menuName = "World/NPC Spawn Rule")]
    public sealed class EnemySpawnRule : ScriptableObject
    {
        [Header("Rule Set")]
        [Tooltip("Additional rules evaluated alongside this rule. Cycles are ignored.")]
        public EnemySpawnRule[] rules = Array.Empty<EnemySpawnRule>();

        [Header("NPC")]
        [Tooltip("Enemy definition used for transient spawns. Its Visual prefab is always spawned.")]
        public EnemyData enemyData;
        [Tooltip("Required for persistent NPCs. The NodeData must have a registered runtime EntityArchetype.")]
        public NodeData persistentNodeData;
        public NPCPersistence persistence = NPCPersistence.Transient;

        [Header("Population")]
        [Min(0)] public int minimumPerChunk;
        [Min(0)] public int maximumPerChunk = 2;
        [Range(0f, 1f)] public float spawnChance = 0.5f;
        [Min(0f)] public float minimumSpacing = 2f;
        [Min(0), Tooltip("Maximum living transient enemies from this rule across all loaded chunks.")]
        public int maxAllowed = 20;
        [Min(1), Tooltip("World ticks between attempts to add one offscreen enemy while below Max Allowed.")]
        public int spawnIntervalTicks = 30;

        [Header("Variations")]
        [Tooltip("Optional enemies that can replace the default Enemy Data when an individual transient enemy is spawned.")]
        public List<EnemySpawnVariation> variations = new();

        [Header("Transient Lifecycle")]
        [Tooltip("Recycle transient NPCs after they have remained idle and outside the camera view for the configured durations.")]
        public bool recycleOffscreenIdle = true;
        [Min(0), Tooltip("Minimum age in world ticks before this NPC can be recycled.")]
        public int minimumLifetimeTicks = 120;
        [Min(1), Tooltip("Continuous idle world ticks required before this NPC can be recycled.")]
        public int idleTicksBeforeRecycle = 30;

        [Header("Time")]
        public SeasonMask seasons = SeasonMask.All;
        [Tooltip("First eligible hour. For late-night spawning, use a value such as 22 and set Last Hour to an earlier morning hour.")]
        [Range(0, 23)] public int firstHour;
        [Tooltip("Exclusive final eligible hour. A value earlier than First Hour creates an overnight window that crosses midnight.")]
        [Range(0, 24)] public int lastHour = 24;

        [Header("Biomes")]
        [Tooltip("Empty means every biome is allowed.")]
        public BiomeData[] allowedBiomes = Array.Empty<BiomeData>();
        public BiomeData[] restrictedBiomes = Array.Empty<BiomeData>();

        [Header("Event (stub)")]
        [Tooltip("Leave empty to ignore events. Requires an INPCSpawnEnvironmentProvider implementation.")]
        public string requiredEvent;

        [Header("Weather")]
        [Tooltip("Any listed active weather allows this rule. Empty allows every weather condition. Requires an INPCSpawnEnvironmentProvider implementation.")]
        public List<WeatherData> requiredWeather = new();

        [Header("Temperature")]
        [Range(-1f, 1f)]
        [Tooltip("Minimum normalized ambient temperature at the player.")]
        public float minimumTemperature = -1f;
        [Range(-1f, 1f)]
        [Tooltip("Maximum normalized ambient temperature at the player.")]
        public float maximumTemperature = 1f;

        public bool AllowsSeason(Season season)
        {
            return (seasons & (SeasonMask)(1 << (int)season)) != 0;
        }

        public bool AllowsHour(int hour)
        {
            if (firstHour == lastHour)
                return true;
            return firstHour < lastHour
                ? hour >= firstHour && hour < lastHour
                : hour >= firstHour || hour < lastHour;
        }

        public EnemyData SelectEnemyData(float roll)
        {
            roll = Mathf.Clamp01(roll);
            float threshold = 0f;
            if (variations != null)
            {
                foreach (EnemySpawnVariation variation in variations)
                {
                    if (variation?.enemyData == null)
                        continue;

                    threshold += Mathf.Clamp01(variation.chance);
                    if (roll < threshold)
                        return variation.enemyData;
                }
            }

            return enemyData;
        }

        public bool AllowsTemperature(float temperature)
        {
            float minimum = Mathf.Min(
                minimumTemperature,
                maximumTemperature);
            float maximum = Mathf.Max(
                minimumTemperature,
                maximumTemperature);
            return temperature >= minimum && temperature <= maximum;
        }

        private void OnValidate()
        {
            maximumPerChunk = Mathf.Max(minimumPerChunk, maximumPerChunk);
            maxAllowed = Mathf.Max(0, maxAllowed);
            spawnIntervalTicks = Mathf.Max(1, spawnIntervalTicks);
            minimumLifetimeTicks = Mathf.Max(0, minimumLifetimeTicks);
            idleTicksBeforeRecycle = Mathf.Max(1, idleTicksBeforeRecycle);
            minimumTemperature = Mathf.Clamp(minimumTemperature, -1f, 1f);
            maximumTemperature = Mathf.Clamp(maximumTemperature, -1f, 1f);
        }
    }
}
