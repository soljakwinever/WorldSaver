using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "New Crafting Recipe", menuName = "Data/Crafting Recipe Data", order = 0)]
    public class CraftingRecipeData : ScriptableObject
    {
        public RecipeIngredient[] ingredients = Array.Empty<RecipeIngredient>();
        public RecipeOutput[] output = Array.Empty<RecipeOutput>();
        [Min(0.01f)]
        public float workRequired = 1.0f;

        public enum IngredientMatchType
        {
            ExactItem,
            Tag
        }

        [Serializable]
        public sealed class RecipeIngredient
        {
            public IngredientMatchType matchType;
            public ItemData itemData;
            public EntityTag tag;

            [Min(1)] public int count = 1;
        }

        [Serializable]
        public sealed class RecipeOutput
        {
            public ItemData itemData;
            [Min(1)]
            public int amount = 1;
            [Range(0, 1)]
            public float chance = 1;
            [Min(1)]
            public int rolls = 1;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ingredients ??= Array.Empty<RecipeIngredient>();
            output ??= Array.Empty<RecipeOutput>();
            workRequired = Mathf.Max(0.01f, workRequired);

            foreach (RecipeIngredient ingredient in ingredients)
            {
                if (ingredient != null)
                    ingredient.count = Mathf.Max(1, ingredient.count);
            }

            foreach (RecipeOutput recipeOutput in output)
            {
                if (recipeOutput == null)
                    continue;

                recipeOutput.amount = Mathf.Max(1, recipeOutput.amount);
                recipeOutput.chance = Mathf.Clamp01(recipeOutput.chance);
                recipeOutput.rolls = Mathf.Max(1, recipeOutput.rolls);
            }
        }
#endif
    }
}
