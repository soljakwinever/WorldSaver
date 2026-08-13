using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.Entities;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    /// <summary>Publishes recurring world work from a Town Core's policies.</summary>
    [DisallowMultipleComponent]
    public sealed class TownWorkDiscovery : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float refreshSeconds = 1f;
        private TownCore _town;
        private TownJobBoard _board;
        private VillagerResourceDiscovery _resources;
        private float _nextRefresh;

        private void Awake()
        {
            ResolveComponents();
        }

        private void Update()
        {
            ResolveComponents();
            if (Time.time < _nextRefresh || _town == null || _board == null) return;
            _nextRefresh = Time.time + Mathf.Max(0.1f, refreshSeconds);
            int food = CountFood(_town.StockpileInventory) +
                       CountIncomingFood();
            DiscoverThreatsAndPrey(food);
            DiscoverPlants();
            DiscoverFurnaceWork(food);
        }

        private void ResolveComponents()
        {
            _town ??= GetComponent<TownCore>();
            _board ??= GetComponent<TownJobBoard>();
            _resources ??= GetComponent<VillagerResourceDiscovery>();
        }

        private void DiscoverThreatsAndPrey(int food)
        {
            TownJobPriority configured =
                _board.GetPriority(VillagerJobType.Hunt);
            TownJobPriority huntingPriority =
                food < Mathf.Max(1, _town.Population)
                    ? TownJobPriority.Emergency
                    : configured;
            EnemyRuntime[] enemies = FindObjectsByType<EnemyRuntime>(FindObjectsSortMode.None);
            foreach (EnemyRuntime enemy in enemies)
            {
                if (enemy == null || enemy.Data == null) continue;
                Vector3 position = enemy.transform.position;
                if (_town.ContainsTownPosition(position))
                {
                    Issue(VillagerJobType.Defend, enemy, position, 0.5f,
                        TownJobPriority.Emergency);
                    continue;
                }
                if (_town.ContainsResourcePosition(position) && DropsFood(enemy.Data))
                    Issue(VillagerJobType.Hunt, enemy, position, 1f,
                        huntingPriority,
                        FindFoodDrop(enemy.Data));
            }
        }

        private void DiscoverPlants()
        {
            if (_board.GetPriority(VillagerJobType.Farm) == TownJobPriority.Off) return;
            PersistentPlant[] plants = FindObjectsByType<PersistentPlant>(FindObjectsSortMode.None);
            foreach (PersistentPlant plant in plants)
            {
                if (plant != null && _town.ContainsResourcePosition(plant.transform.position))
                    Issue(VillagerJobType.Farm, plant, plant.transform.position, 0.5f,
                        _board.GetPriority(VillagerJobType.Farm));
            }
        }

        private void DiscoverFurnaceWork(int food)
        {
            TownJobPriority configured =
                _board.GetPriority(VillagerJobType.Craft);
            if (configured == TownJobPriority.Off)
                return;

            int emergencyThreshold = Mathf.Max(1, _town.Population);
            int reserveTarget = Mathf.Max(4, _town.Population * 2);
            TownJobPriority priority = food < emergencyThreshold
                ? TownJobPriority.Emergency
                : configured;

            _town.RefreshBuildings();
            foreach (GameObject building in _town.Buildings)
            {
                if (building == null)
                    continue;
                FurnaceComponent furnace =
                    building.GetComponentInChildren<FurnaceComponent>(true);
                if (furnace == null ||
                    _board.IsTargetInvalid(
                        VillagerJobType.Craft,
                        furnace,
                        furnace.transform.position) ||
                    _board.HasActiveTarget(furnace, VillagerJobType.Craft))
                    continue;

                if (furnace.OutputInventory.OccupiedSlots > 0)
                {
                    IssueFurnace(furnace, null, priority);
                    continue;
                }

                if (food >= reserveTarget ||
                    furnace.IngredientInventory.OccupiedSlots > 0)
                    continue;

                CraftingRecipeData recipe = FindFoodRecipe(furnace);
                if (recipe == null)
                    continue;
                if (!furnace.CanQueueRecipe(recipe, _town.StockpileInventory))
                {
                    RequestFurnaceSupplies(furnace, recipe, priority);
                    continue;
                }
                IssueFurnace(furnace, recipe, priority);
            }
        }

        private void RequestFurnaceSupplies(
            FurnaceComponent furnace,
            CraftingRecipeData recipe,
            TownJobPriority priority)
        {
            if (_resources == null || recipe?.ingredients == null)
                return;
            foreach (CraftingRecipeData.RecipeIngredient ingredient in
                     recipe.ingredients)
            {
                if (ingredient == null) continue;
                if (ingredient.matchType ==
                    CraftingRecipeData.IngredientMatchType.ExactItem)
                {
                    if (_town.StockpileInventory.Contains(
                            ingredient.itemData,
                            Mathf.Max(1, ingredient.count)))
                        continue;
                    _resources.TryIssueDemand(
                        ingredient.itemData,
                        Mathf.Max(1, ingredient.count),
                        priority,
                        $"food:i:{ingredient.itemData?.persistentId}",
                        out _);
                    return;
                }

                if (_town.StockpileInventory.Contains(
                        ingredient.tag,
                        Mathf.Max(1, ingredient.count)))
                    continue;
                ItemData item = _resources.FindGatherableItem(ingredient.tag);
                if (item == null) return;
                _resources.TryIssueDemand(
                    item,
                    Mathf.Max(1, ingredient.count),
                    priority,
                    $"food:t:{item.persistentId}",
                    out _);
                return;
            }

            if (furnace.HasFuelForRecipe(recipe, _town.StockpileInventory))
                return;
            ItemData fuel =
                _resources.FindGatherableItem(furnace.AcceptedFuelTag);
            if (fuel != null)
                _resources.TryIssueDemand(
                    fuel,
                    1,
                    priority,
                    $"food:f:{fuel.persistentId}",
                    out _);
        }

        private void IssueFurnace(
            FurnaceComponent furnace,
            CraftingRecipeData recipe,
            TownJobPriority priority)
        {
            ItemData output = null;
            if (recipe?.output != null)
            {
                foreach (CraftingRecipeData.RecipeOutput candidate in
                         recipe.output)
                {
                    if (candidate?.itemData == null ||
                        !IsFood(candidate.itemData))
                        continue;
                    output = candidate.itemData;
                    break;
                }
            }

            _board.TryIssue(new TownJobRequest(
                VillagerJobType.Craft,
                furnace.transform.position,
                recipe != null ? Mathf.Max(0f, recipe.workRequired) : 0.25f,
                priority,
                furnace,
                output,
                recipe), out _);
        }

        private static CraftingRecipeData FindFoodRecipe(
            FurnaceComponent furnace)
        {
            foreach (CraftingRecipeData recipe in furnace.Recipes)
            {
                if (recipe?.output == null)
                    continue;
                foreach (CraftingRecipeData.RecipeOutput output in recipe.output)
                    if (output?.itemData != null && IsFood(output.itemData))
                        return recipe;
            }
            return null;
        }

        private static int CountFood(IInventory inventory)
        {
            if (inventory == null)
                return 0;
            int count = 0;
            foreach (IItemStack stack in inventory.Stacks)
                if (stack?.Item != null && IsFood(stack.Item))
                    count += stack.Count;
            return count;
        }

        private static bool IsFood(ItemData item) =>
            item != null &&
            item.TryGetActionData<IncreaseNeedsActionData>(out var needs) &&
            needs.hunger > 0f;

        private int CountIncomingFood()
        {
            int count = 0;
            string townId = _town.PersistentEntity?.Id.ToString() ?? string.Empty;
            foreach (VillagerEntityBridge villager in VillagerEntityBridge.All)
            {
                if (villager == null || villager.TownId != townId) continue;
                foreach (IItemStack stack in villager.Stacks)
                    if (stack?.Item != null && IsFood(stack.Item))
                        count += stack.Count;
            }
            return count;
        }

        private void Issue(VillagerJobType type, Object target, Vector3 position,
            float work, TownJobPriority priority, ItemData item = null)
        {
            if (priority == TownJobPriority.Off ||
                _board.IsTargetInvalid(type, target, position) ||
                _board.HasActiveTarget(target, type)) return;
            _board.TryIssue(new TownJobRequest(
                type, position, work, priority, target, item), out _);
        }

        private static bool DropsFood(EnemyData data)
        {
            foreach (DropData drop in data.drops ?? System.Array.Empty<DropData>())
                if (drop?.item != null &&
                    drop.item.TryGetActionData<IncreaseNeedsActionData>(out _)) return true;
            return false;
        }

        private static ItemData FindFoodDrop(EnemyData data)
        {
            foreach (DropData drop in data?.drops ?? System.Array.Empty<DropData>())
                if (drop?.item != null && IsFood(drop.item))
                    return drop.item;
            return null;
        }
    }
}
