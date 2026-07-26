using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public sealed class CraftingBenchWindowContext
    {
        private readonly Func<CraftingRecipeData, bool> _canCraft;
        private readonly Func<CraftingRecipeData, CraftResult> _tryCraft;
        private readonly Action _close;

        public IReadOnlyList<CraftingRecipeData> Recipes { get; }
        public IInventory Source { get; }
        public IInventory Destination { get; }
        public int SelectedRecipeIndex { get; set; }
        public string StatusMessage { get; set; }

        public CraftingRecipeData SelectedRecipe =>
            SelectedRecipeIndex >= 0 && SelectedRecipeIndex < Recipes.Count
                ? Recipes[SelectedRecipeIndex]
                : null;

        public CraftingBenchWindowContext(
            IReadOnlyList<CraftingRecipeData> recipes,
            IInventory source,
            IInventory destination,
            Func<CraftingRecipeData, bool> canCraft,
            Func<CraftingRecipeData, CraftResult> tryCraft,
            Action close)
        {
            Recipes = recipes ??
                throw new ArgumentNullException(nameof(recipes));
            Source = source ??
                throw new ArgumentNullException(nameof(source));
            Destination = destination ??
                throw new ArgumentNullException(nameof(destination));
            _canCraft = canCraft ??
                throw new ArgumentNullException(nameof(canCraft));
            _tryCraft = tryCraft ??
                throw new ArgumentNullException(nameof(tryCraft));
            _close = close ??
                throw new ArgumentNullException(nameof(close));
            SelectedRecipeIndex = Recipes.Count > 0 ? 0 : -1;
        }

        public bool CanCraft(CraftingRecipeData recipe)
        {
            return recipe != null && _canCraft(recipe);
        }

        public CraftResult TryCraft(CraftingRecipeData recipe)
        {
            return _tryCraft(recipe);
        }

        public void Close()
        {
            _close();
        }
    }

    public abstract class CraftingBenchLayout : ScriptableObject
    {
        public virtual Vector2 DefaultWindowSize => new(640f, 440f);
        public abstract void Draw(CraftingBenchWindowContext context);
    }
}
