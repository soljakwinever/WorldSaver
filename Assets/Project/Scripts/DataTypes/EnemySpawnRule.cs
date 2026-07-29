using System;
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
        [Tooltip("Enemy definition used for transient spawns. Its visual replaces npcPrefab when assigned.")]
        public EnemyData enemyData;
        public GameObject npcPrefab;
        [Tooltip("Required for persistent NPCs. The NodeData must have a registered runtime EntityArchetype.")]
        public NodeData persistentNodeData;
        public NPCPersistence persistence = NPCPersistence.Transient;

        [Header("Population")]
        [Min(0)] public int minimumPerChunk;
        [Min(0)] public int maximumPerChunk = 2;
        [Range(0f, 1f)] public float spawnChance = 0.5f;
        [Min(0f)] public float minimumSpacing = 2f;

        [Header("Time")]
        public SeasonMask seasons = SeasonMask.All;
        [Range(0, 23)] public int firstHour;
        [Range(0, 24)] public int lastHour = 24;

        [Header("Biomes")]
        [Tooltip("Empty means every biome is allowed.")]
        public BiomeData[] allowedBiomes = Array.Empty<BiomeData>();
        public BiomeData[] restrictedBiomes = Array.Empty<BiomeData>();

        [Header("Event (stub)")]
        [Tooltip("Leave empty to ignore events. Requires an INPCSpawnEnvironmentProvider implementation.")]
        public string requiredEvent;

        [Header("Weather (stub)")]
        [Tooltip("Leave empty to ignore weather. Requires an INPCSpawnEnvironmentProvider implementation.")]
        public string requiredWeather;

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

        private void OnValidate()
        {
            maximumPerChunk = Mathf.Max(minimumPerChunk, maximumPerChunk);
        }
    }
}
