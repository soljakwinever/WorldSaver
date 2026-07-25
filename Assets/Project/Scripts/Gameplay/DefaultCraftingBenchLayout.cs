using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [CreateAssetMenu(
        fileName = "Default Crafting Bench Layout",
        menuName = "Data/Crafting/Default Bench Layout")]
    public sealed class DefaultCraftingBenchLayout : CraftingBenchLayout
    {
        [SerializeField, Min(120f)] private float recipeListWidth = 220f;

        private Vector2 _recipeScroll;
        private Vector2 _detailScroll;

        public override void Draw(CraftingBenchWindowContext context)
        {
            GUILayout.BeginHorizontal();
            DrawRecipeList(context);
            DrawRecipeDetails(context);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(context.StatusMessage))
                GUILayout.Label(context.StatusMessage, GUI.skin.box);
        }

        private void DrawRecipeList(CraftingBenchWindowContext context)
        {
            GUILayout.BeginVertical(GUILayout.Width(recipeListWidth));
            GUILayout.Label("Recipes", GUI.skin.box);
            _recipeScroll = GUILayout.BeginScrollView(_recipeScroll);

            if (context.Recipes.Count == 0)
                GUILayout.Label("No recipes available.");

            for (int i = 0; i < context.Recipes.Count; i++)
            {
                CraftingRecipeData recipe = context.Recipes[i];
                string name = GetRecipeName(recipe);
                bool selected = context.SelectedRecipeIndex == i;
                GUIStyle style = selected ? GUI.skin.box : GUI.skin.button;
                if (GUILayout.Button(name, style))
                {
                    context.SelectedRecipeIndex = i;
                    context.StatusMessage = null;
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawRecipeDetails(CraftingBenchWindowContext context)
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _detailScroll = GUILayout.BeginScrollView(_detailScroll);

            CraftingRecipeData recipe = context.SelectedRecipe;
            if (recipe == null)
            {
                GUILayout.Label("Select a recipe.");
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label(GetRecipeName(recipe), GUI.skin.box);
            GUILayout.Label($"Work required: {recipe.workRequired:0.##}");
            GUILayout.Space(6f);
            GUILayout.Label("Ingredients");
            if (recipe.ingredients == null)
            {
                GUILayout.Label("• Invalid ingredient list");
            }
            else
            {
                for (int i = 0; i < recipe.ingredients.Length; i++)
                    DrawIngredient(recipe.ingredients[i], context);
            }

            GUILayout.Space(6f);
            GUILayout.Label("Outputs");
            if (recipe.output == null)
            {
                GUILayout.Label("• Invalid output list");
            }
            else
            {
                for (int i = 0; i < recipe.output.Length; i++)
                    DrawOutput(recipe.output[i]);
            }

            GUILayout.FlexibleSpace();
            bool canCraft = context.CanCraft(recipe);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = canCraft;
            if (GUILayout.Button("Craft", GUILayout.Height(34f)))
                context.TryCraft(recipe);
            GUI.enabled = previousEnabled;

            if (!canCraft)
                GUILayout.Label("Missing ingredients or output space.");

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private static void DrawIngredient(
            CraftingRecipeData.RecipeIngredient ingredient,
            CraftingBenchWindowContext context)
        {
            if (ingredient == null)
            {
                GUILayout.Label("• Invalid ingredient");
                return;
            }

            string label;
            bool available = false;
            switch (ingredient.matchType)
            {
                case CraftingRecipeData.IngredientMatchType.ExactItem:
                    label = ingredient.itemData != null
                        ? ingredient.itemData.name
                        : "Missing item";
                    if (ingredient.itemData != null)
                    {
                        int total = GetItemCount(
                            context.Source, ingredient.itemData);
                        available = total >= ingredient.count;
                        label += $"  {total}/{ingredient.count}";
                    }
                    break;

                case CraftingRecipeData.IngredientMatchType.Tag:
                    label = ingredient.tag != null
                        ? $"Any {ingredient.tag.name}"
                        : "Missing tag";
                    if (ingredient.tag != null)
                    {
                        int total = context.Source.GetCount(ingredient.tag);
                        available = total >= ingredient.count;
                        label += $"  {total}/{ingredient.count}";
                    }
                    break;

                default:
                    label = "Invalid ingredient";
                    break;
            }

            GUILayout.Label($"{(available ? "✓" : "•")} {label}");
        }

        private static void DrawOutput(
            CraftingRecipeData.RecipeOutput output)
        {
            if (output?.itemData == null)
            {
                GUILayout.Label("• Invalid output");
                return;
            }

            string chance = output.chance >= 1f
                ? ""
                : $" at {output.chance:P0}";
            string rolls = output.rolls > 1
                ? $", {output.rolls} rolls"
                : "";
            GUILayout.Label(
                $"• {output.itemData.name} ×{output.amount}{chance}{rolls}");
        }

        private static int GetItemCount(IInventory inventory, ItemData item)
        {
            int total = 0;
            for (int rarity = (int)ItemData.Rarity.Common;
                 rarity <= (int)ItemData.Rarity.Legendary;
                 rarity++)
            {
                total = checked(total + inventory.GetCount(
                    item, (ItemData.Rarity)rarity));
            }

            return total;
        }

        private static string GetRecipeName(CraftingRecipeData recipe)
        {
            if (recipe == null)
                return "Invalid Recipe";
            if (!string.IsNullOrWhiteSpace(recipe.name))
                return recipe.name;
            if (recipe.output != null &&
                recipe.output.Length > 0 &&
                recipe.output[0]?.itemData != null)
            {
                return recipe.output[0].itemData.name;
            }

            return "Unnamed Recipe";
        }
    }
}
