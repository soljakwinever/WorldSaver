using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "New Crafting Recipe", menuName = "Data/Crafting Recipe Data", order = 0)]
    public class CraftingRecipeData : ScriptableObject
    {
        public RecipeComponent[] output;
        public RecipeComponent[] ingredients;
        public float workRequired = 1.0f;
        
        [Serializable]
        public struct RecipeComponent
        {
            public ItemData itemData;
            public int amount;
        }
    }
}