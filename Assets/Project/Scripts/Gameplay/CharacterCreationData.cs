using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public enum CharacterGender { Male, Female, Other }
    public enum StatGrowthRank { E, D, C, B, A, S }

    [Serializable]
    public struct CharacterStatValues
    {
        public int strength;
        public int constitution;
        public int dexterity;
        public int wisdom;
        public int intelligence;
        public int luck;

        public int Get(PlayerStat stat) => stat switch
        {
            PlayerStat.Strength => strength,
            PlayerStat.Constitution => constitution,
            PlayerStat.Dexterity => dexterity,
            PlayerStat.Wisdom => wisdom,
            PlayerStat.Intelligence => intelligence,
            PlayerStat.Luck => luck,
            _ => 0
        };
    }

    [Serializable]
    public struct CharacterGrowthRanks
    {
        public StatGrowthRank strength;
        public StatGrowthRank constitution;
        public StatGrowthRank dexterity;
        public StatGrowthRank wisdom;
        public StatGrowthRank intelligence;
        public StatGrowthRank luck;

        public StatGrowthRank Get(PlayerStat stat) => stat switch
        {
            PlayerStat.Strength => strength,
            PlayerStat.Constitution => constitution,
            PlayerStat.Dexterity => dexterity,
            PlayerStat.Wisdom => wisdom,
            PlayerStat.Intelligence => intelligence,
            PlayerStat.Luck => luck,
            _ => StatGrowthRank.E
        };

        public static CharacterGrowthRanks All(StatGrowthRank rank) => new()
        {
            strength = rank, constitution = rank, dexterity = rank,
            wisdom = rank, intelligence = rank, luck = rank
        };
    }

    [Serializable]
    public sealed class CharacterAppearance
    {
        public string body = string.Empty;
        public string hair = string.Empty;
        public string horns = string.Empty;
        public string clothes = string.Empty;
        public string wings = string.Empty;
        public Color skinColorA = new(0.82f, 0.65f, 0.5f);
        public Color skinColorB = new(0.62f, 0.42f, 0.3f);
        public Color eyeColor = Color.blue;
        public Color clothingColorA = Color.gray;
        public Color clothingColorB = Color.black;
    }

    [Serializable]
    public sealed class CharacterProfile
    {
        public string id;
        public string name;
        public CharacterGender gender;
        public string speciesId;
        public string classId;
        public CharacterGrowthRanks growthRanks;
        public CharacterAppearance appearance = new();
        public string createdUtc;
    }

    [Serializable]
    public sealed class CharacterAppearanceOption
    {
        public string id;
        public string displayName;
        public Sprite sprite;
    }

    [Serializable]
    public sealed class CharacterSpeciesDefinition
    {
        public string id;
        public string displayName;
        public CharacterStatValues startingStats;
        public bool supportsSecondarySkinColor;
        public bool supportsHorns;
        public bool supportsWings;
    }

    [Serializable]
    public sealed class CharacterStarterItem
    {
        public ItemData item;
        [Min(1)] public int count = 1;
        public bool equip;
    }

    [Serializable]
    public sealed class CharacterClassDefinition
    {
        public string id;
        public string displayName;
        public bool available = true;
        public CharacterStatValues startingStats;
        public CharacterGrowthRanks growthRanks;
        public SkillData[] starterSkills = Array.Empty<SkillData>();
        public CharacterStarterItem[] starterItems = Array.Empty<CharacterStarterItem>();
    }

    public static class CharacterProfileStore
    {
        public const string ActiveCharacterKey = "WorldSaver.ActiveCharacter";

        public static IReadOnlyList<CharacterProfile> ReadAll()
        {
            string root = RootDirectory;
            if (!Directory.Exists(root)) return Array.Empty<CharacterProfile>();
            var profiles = new List<CharacterProfile>();
            foreach (string path in Directory.GetFiles(root, "profile.json", SearchOption.AllDirectories))
            {
                try
                {
                    CharacterProfile profile = JsonUtility.FromJson<CharacterProfile>(File.ReadAllText(path));
                    if (profile != null && !string.IsNullOrWhiteSpace(profile.id)) profiles.Add(profile);
                }
                catch (Exception exception) { Debug.LogWarning($"Could not read character profile '{path}': {exception.Message}"); }
            }
            return profiles.OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static bool TryRead(string id, out CharacterProfile profile)
        {
            profile = null;
            string path = ProfilePath(id);
            if (!File.Exists(path)) return false;
            try { profile = JsonUtility.FromJson<CharacterProfile>(File.ReadAllText(path)); }
            catch (Exception exception) { Debug.LogWarning($"Could not read character profile '{id}': {exception.Message}"); }
            return profile != null && !string.IsNullOrWhiteSpace(profile.id);
        }

        public static CharacterProfile Create(CharacterProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.name = (profile.name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(profile.name)) throw new ArgumentException("Character name is required.");
            string baseId = Sanitize(profile.name, "character").ToLowerInvariant();
            profile.id = string.IsNullOrWhiteSpace(profile.id) ? baseId : Sanitize(profile.id, baseId);
            if (ReadAll().Any(x => string.Equals(x.name, profile.name, StringComparison.OrdinalIgnoreCase) || string.Equals(x.id, profile.id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A character with that name already exists.");
            profile.createdUtc = string.IsNullOrWhiteSpace(profile.createdUtc) ? DateTime.UtcNow.ToString("O") : profile.createdUtc;
            profile.appearance ??= new CharacterAppearance();
            Save(profile);
            return profile;
        }

        public static void Save(CharacterProfile profile)
        {
            string path = ProfilePath(profile.id);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(profile, true));
        }

        public static string Sanitize(string value, string fallback)
        {
            string result = (value ?? string.Empty).Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
            result = result.Trim('.', ' ');
            return string.IsNullOrWhiteSpace(result) ? fallback : result;
        }

        private static string RootDirectory => Path.Combine(Application.persistentDataPath, "Characters");
        private static string ProfilePath(string id) => Path.Combine(RootDirectory, Sanitize(id, "character"), "profile.json");
    }
}
