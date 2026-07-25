using System.Collections.Generic;
using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public enum CraftFailureReason
    {
        None,
        InvalidRecipe,
        MissingIngredients,
        InsufficientOutputSpace
    }

    public sealed class CraftResult
    {
        public bool Succeeded { get; }
        public CraftFailureReason FailureReason { get; }
        public IReadOnlyList<IItemStack> Outputs { get; }

        public CraftResult(
            bool succeeded,
            CraftFailureReason failureReason,
            IReadOnlyList<IItemStack> outputs)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            Outputs = outputs;
        }
    }

    public interface ICraftingRandom
    {
        float Value();
    }

    public interface ICraftingService
    {
        bool CanCraft(CraftingRecipeData recipe, IInventory source, IInventory destination);
        bool TryCraft(
            CraftingRecipeData recipe,
            IInventory source,
            IInventory destination,
            out CraftResult result);
    }
}
