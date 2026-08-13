using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class CraftingBenchComponent :
        MonoBehaviour,
        IInteractable,
        IEntityComponent
    {
        [SerializeField] private string windowTitle = "Crafting Bench";
        [SerializeField] private string interactionPrompt = "Use crafting bench";
        [SerializeField] private CraftingBenchLayout layout;
        [SerializeField] private RecipeList recipeList;

        private IComponentWindowService _windowService;
        private ICraftingService _craftingService;
        private DefaultCraftingBenchLayout _defaultLayout;
        public IPersistentEntity PersistentEntity { get; set; }

        [Inject]
        public void Construct(
            IComponentWindowService windowService,
            ICraftingService craftingService)
        {
            _windowService = windowService ??
                throw new ArgumentNullException(nameof(windowService));
            _craftingService = craftingService ??
                throw new ArgumentNullException(nameof(craftingService));
        }

        public void Initialize(
            string title,
            string prompt,
            RecipeList availableRecipes,
            CraftingBenchLayout benchLayout = null)
        {
            windowTitle = string.IsNullOrWhiteSpace(title)
                ? "Crafting Bench"
                : title;
            interactionPrompt = string.IsNullOrWhiteSpace(prompt)
                ? "Use crafting bench"
                : prompt;
            recipeList = availableRecipes;
            layout = benchLayout;
        }

        public Vector3 GetPosition()
        {
            return transform.position;
        }

        public IReadOnlyList<CraftingRecipeData> Recipes =>
            recipeList?.Recipes ?? Array.Empty<CraftingRecipeData>();

        public bool TryCraft(
            CraftingRecipeData recipe,
            IInventory source,
            IInventory destination,
            out CraftResult result)
        {
            if (_craftingService == null || recipe == null ||
                !Recipes.Contains(recipe))
            {
                result = new CraftResult(
                    false,
                    CraftFailureReason.InvalidRecipe,
                    Array.Empty<IItemStack>());
                return false;
            }
            return _craftingService.TryCraft(recipe, source, destination, out result);
        }

        public bool CanInteract(InteractionContext context)
        {
            return context.interactionType == InteractionType.Direct &&
                   _windowService != null &&
                   TryGetInventory(context.user, out _);
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context) ||
                !TryGetInventory(context.user, out IInventory inventory))
            {
                return;
            }

            CraftingBenchLayout activeLayout = layout ?? ResolveDefaultLayout();
            CraftingBenchWindowContext benchContext = null;
            benchContext = new CraftingBenchWindowContext(
                recipeList?.Recipes ?? Array.Empty<CraftingRecipeData>(),
                inventory,
                inventory,
                recipe => _craftingService.CanCraft(
                    recipe, inventory, inventory),
                recipe =>
                {
                    _craftingService.TryCraft(
                        recipe, inventory, inventory,
                        out CraftResult result);
                    benchContext.StatusMessage = result.Succeeded
                        ? "Crafted successfully."
                        : DescribeFailure(result.FailureReason);
                    return result;
                },
                _windowService.Close);

            _windowService.Open(new ComponentWindowRequest(
                windowTitle,
                activeLayout.DefaultWindowSize,
                new DelegateComponentWindowSection(context =>
                {
                    activeLayout.Draw(benchContext);
                    context.StatusMessage = benchContext.StatusMessage;
                })));
        }

        public string GetInteractionPrompt(InteractionContext context)
        {
            return context.interactionType == InteractionType.Direct
                ? interactionPrompt
                : string.Empty;
        }

        private static bool TryGetInventory(
            GameObject user,
            out IInventory inventory)
        {
            inventory = null;
            if (user == null)
                return false;

            MonoBehaviour[] behaviours =
                user.GetComponentsInParent<MonoBehaviour>(includeInactive: true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IInventory found)
                {
                    inventory = found;
                    return true;
                }
            }
            return false;
        }

        private DefaultCraftingBenchLayout ResolveDefaultLayout()
        {
            if (_defaultLayout == null)
            {
                _defaultLayout =
                    ScriptableObject.CreateInstance<DefaultCraftingBenchLayout>();
                _defaultLayout.hideFlags = HideFlags.HideAndDontSave;
            }
            return _defaultLayout;
        }

        private static string DescribeFailure(
            CraftFailureReason failureReason)
        {
            return failureReason switch
            {
                CraftFailureReason.InvalidRecipe => "This recipe is invalid.",
                CraftFailureReason.MissingIngredients => "Missing ingredients.",
                CraftFailureReason.InsufficientOutputSpace =>
                    "Not enough inventory space.",
                _ => "Crafting failed."
            };
        }

        private void OnDestroy()
        {
            if (_defaultLayout != null)
                Destroy(_defaultLayout);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(windowTitle))
                windowTitle = "Crafting Bench";
            if (string.IsNullOrWhiteSpace(interactionPrompt))
                interactionPrompt = "Use crafting bench";
        }
#endif
    }
}
