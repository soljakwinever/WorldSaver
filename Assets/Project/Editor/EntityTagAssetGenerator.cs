using System.Collections.Generic;
using System.IO;
using Project.Scripts.DataTypes;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>
    /// Maintains the project's shared EntityTag vocabulary. Existing assets are
    /// deliberately left untouched so references and GUIDs remain stable.
    /// </summary>
    public static class EntityTagAssetGenerator
    {
        private const string OutputFolder = "Assets/Project/Data/Tags";

        private static readonly string[] TagNames =
        {
            // Behaviours and interactions
            "Buildable", "Burnable", "Consumable", "Craftable", "Destructible",
            "Equippable", "Farmable", "Flammable", "Fuel", "Harvestable",
            "Interactable", "Mineable", "Placeable", "Plantable", "Repairable",
            "Sellable", "Smeltable", "Stackable", "Tradable", "Usable",

            // Tool and progression requirements
            "MiningLevel1", "MiningLevel2", "MiningLevel3", "MiningLevel4",
            "MiningLevel5", "ChoppingLevel1", "ChoppingLevel2", "ChoppingLevel3",
            "DiggingLevel1", "DiggingLevel2", "DiggingLevel3", "MagicLevel1",
            "MagicLevel2", "MagicLevel3",

            // Materials and resources
            "Wood", "Stone", "Rock", "Iron", "Copper", "Tin", "Bronze",
            "Silver", "Gold", "Steel", "Mythril", "Obsidian", "Crystal",
            "Glass", "Sand", "Clay", "Brick", "Ash", "Coal", "Sulfur",
            "Bone", "Leather", "Fiber", "Cloth", "Plant", "Flower", "Herb",
            "Mushroom", "Cactus", "Coral", "Shell", "Water", "Ice", "Snow",

            // World objects and item families
            "Tree", "Log", "Stick", "Kindling", "Ore", "Gem", "Metal",
            "Mineral", "Seed", "Crop", "Food", "Potion", "Tool", "Weapon",
            "Armor", "Pickaxe", "Axe", "Shovel", "Hoe", "FishingRod",
            "Campfire", "Container", "Machine", "Building", "Decoration",

            // Tile and environment properties
            "Ground", "Path", "Wall", "WaterTile", "Lava", "Cliff",
            "Beach", "Grass", "Dirt", "Mud", "Swamp", "Ocean", "Underwater",
            "Mountain", "Cave", "Forest", "Desert", "Tundra", "Volcanic",
            "Walkable", "Blocked", "Solid", "Liquid", "Hazardous", "Fertile",

            // Damage, elements, and fantasy properties
            "Magic", "Fire", "WaterElement", "Earth", "Air", "IceElement",
            "Lightning", "Poison", "Shadow", "Light", "Arcane", "Fey",
            "Eldritch", "Runic", "Dragon", "Undead", "Luminous", "Cursed",

            // Quality and provenance
            "Natural", "Processed", "Refined", "Raw", "Organic", "Common",
            "Uncommon", "Rare", "Epic", "Legendary", "Quest", "Debug"
        };

        [MenuItem("Tools/World Saver/Generate Entity Tags")]
        public static void Generate()
        {
            Directory.CreateDirectory(OutputFolder);

            HashSet<string> existingNames = new();
            foreach (string guid in AssetDatabase.FindAssets("t:EntityTag", new[] { OutputFolder }))
            {
                EntityTag existing = AssetDatabase.LoadAssetAtPath<EntityTag>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (existing != null)
                    existingNames.Add(existing.name);
            }

            int created = 0;
            foreach (string tagName in TagNames)
            {
                if (!existingNames.Add(tagName))
                    continue;

                EntityTag tag = ScriptableObject.CreateInstance<EntityTag>();
                tag.name = tagName;
                AssetDatabase.CreateAsset(tag, $"{OutputFolder}/{tagName}.asset");
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Entity tag generation complete: created {created}, total {existingNames.Count}.");
        }
    }
}
