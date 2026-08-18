using System;
using System.Linq;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [CreateAssetMenu(fileName = "Character Creation Catalog", menuName = "Data/Character Creation Catalog")]
    public sealed class CharacterCreationCatalogData : ScriptableObject
    {
        public const string ResourcePath = "CharacterCreation/Catalog";

        [Header("Defaults")]
        public string defaultSpeciesId = "human";
        public string defaultClassId = "warrior";
        public CharacterGender defaultGender = CharacterGender.Other;
        public string editorDefaultCharacterName = "Default Character";

        [Header("Definitions")]
        public CharacterSpeciesDefinition[] species = Array.Empty<CharacterSpeciesDefinition>();
        public CharacterClassDefinition[] classes = Array.Empty<CharacterClassDefinition>();

        [Header("Appearance Options")]
        public CharacterAppearanceOption[] bodyTypes = Array.Empty<CharacterAppearanceOption>();
        public CharacterAppearanceOption[] hair = Array.Empty<CharacterAppearanceOption>();
        public CharacterAppearanceOption[] horns = Array.Empty<CharacterAppearanceOption>();
        public CharacterAppearanceOption[] clothes = Array.Empty<CharacterAppearanceOption>();
        public CharacterAppearanceOption[] wings = Array.Empty<CharacterAppearanceOption>();

        [Header("Growth Per Level")]
        [Tooltip("Fractional automatic stat growth for ranks E through S.")]
        public float[] growthPerLevel = { 0.1f, 0.2f, 0.35f, 0.5f, 0.75f, 1f };

        public float GetGrowth(StatGrowthRank rank)
        {
            int index = (int)rank;
            return growthPerLevel != null && index < growthPerLevel.Length
                ? Mathf.Max(0f, growthPerLevel[index])
                : index == (int)StatGrowthRank.S ? 1f : 0f;
        }

        public bool TryGetSpecies(string id, out CharacterSpeciesDefinition value)
        {
            value = species?.FirstOrDefault(x => x != null && string.Equals(x.id, id, StringComparison.Ordinal));
            return value != null;
        }

        public bool TryGetClass(string id, out CharacterClassDefinition value)
        {
            value = classes?.FirstOrDefault(x => x != null && string.Equals(x.id, id, StringComparison.Ordinal));
            return value != null;
        }

        public static CharacterCreationCatalogData LoadOrFallback()
        {
            CharacterCreationCatalogData catalog = Resources.Load<CharacterCreationCatalogData>(ResourcePath);
            if (catalog != null) return catalog;
            catalog = CreateInstance<CharacterCreationCatalogData>();
            catalog.species = new[]
            {
                new CharacterSpeciesDefinition { id = "human", displayName = "Human" },
                new CharacterSpeciesDefinition { id = "dragon", displayName = "Dragon", startingStats = new CharacterStatValues { strength = 2, constitution = 1, dexterity = -1 }, supportsHorns = true, supportsWings = true, supportsSecondarySkinColor = true },
                new CharacterSpeciesDefinition { id = "faerie", displayName = "Faerie", startingStats = new CharacterStatValues { strength = -1, wisdom = 2, intelligence = 1 }, supportsWings = true }
            };
            ItemData woodenSword = Resources.Load<ItemData>("Items/WoodenSword");
            ItemData magicCrystal = Resources.Load<ItemData>("Items/Magic Crystal");
            SkillData charge = Resources.Load<SkillData>("Skills/Charge");
            SkillData fireball = Resources.Load<SkillData>("Skills/Fireball");
            catalog.classes = new[]
            {
                new CharacterClassDefinition { id = "warrior", displayName = "Warrior", available = true, startingStats = new CharacterStatValues { strength = 3, constitution = 2 }, growthRanks = new CharacterGrowthRanks { strength = StatGrowthRank.S, constitution = StatGrowthRank.B, dexterity = StatGrowthRank.B, wisdom = StatGrowthRank.D, intelligence = StatGrowthRank.E, luck = StatGrowthRank.C }, starterSkills = charge == null ? Array.Empty<SkillData>() : new[] { charge }, starterItems = woodenSword == null ? Array.Empty<CharacterStarterItem>() : new[] { new CharacterStarterItem { item = woodenSword } } },
                new CharacterClassDefinition { id = "mage", displayName = "Mage", available = true, startingStats = new CharacterStatValues { wisdom = 2, intelligence = 3 }, growthRanks = new CharacterGrowthRanks { strength = StatGrowthRank.E, constitution = StatGrowthRank.D, dexterity = StatGrowthRank.C, wisdom = StatGrowthRank.S, intelligence = StatGrowthRank.S, luck = StatGrowthRank.C }, starterSkills = fireball == null ? Array.Empty<SkillData>() : new[] { fireball }, starterItems = magicCrystal == null ? Array.Empty<CharacterStarterItem>() : new[] { new CharacterStarterItem { item = magicCrystal } } },
                new CharacterClassDefinition { id = "ranger", displayName = "Ranger", available = false },
                new CharacterClassDefinition { id = "tank", displayName = "Tank", available = false },
                new CharacterClassDefinition { id = "custom", displayName = "Custom", available = false }
            };
            catalog.bodyTypes = new[] { new CharacterAppearanceOption { id = "default", displayName = "Default" } };
            catalog.hair = new[] { new CharacterAppearanceOption { id = "default", displayName = "Default" } };
            catalog.clothes = new[] { new CharacterAppearanceOption { id = "default", displayName = "Default" } };
            return catalog;
        }
    }
}
