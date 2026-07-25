using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(
        fileName = "New Recipe List",
        menuName = "Data/Crafting/Recipe List")]
    public sealed class RecipeList : ScriptableObject
    {
        [SerializeField] private CraftingRecipeData[] recipes =
            Array.Empty<CraftingRecipeData>();

        public IReadOnlyList<CraftingRecipeData> Recipes =>
            recipes ?? Array.Empty<CraftingRecipeData>();

        public int Count => recipes?.Length ?? 0;

        public CraftingRecipeData this[int index] => Recipes[index];

#if UNITY_EDITOR
        private void OnValidate()
        {
            recipes ??= Array.Empty<CraftingRecipeData>();
        }
#endif
    }
}
