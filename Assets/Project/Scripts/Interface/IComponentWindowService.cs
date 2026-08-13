using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    public interface IFurnaceStation
    {
        string WindowTitle { get; }
        IInventory FuelInventory { get; }
        IInventory IngredientInventory { get; }
        IInventory OutputInventory { get; }
        IReadOnlyList<CraftingRecipeData> Recipes { get; }
        EntityTag AcceptedFuelTag { get; }
        float Progress01 { get; }
        bool IsBurning { get; }
        bool CanQueueRecipe(CraftingRecipeData recipe, IInventory source);
        bool HasFuelForRecipe(CraftingRecipeData recipe, IInventory source);
        bool TryQueueRecipe(
            CraftingRecipeData recipe,
            IInventory source,
            out string reason);
        bool TryInsertFuel(IInventory source, IItemStack stack);
        bool TryInsertIngredient(IInventory source, IItemStack stack);
        bool TryCollectIngredient(IInventory destination, IItemStack stack);
        bool TryCollectOutput(IInventory destination, IItemStack stack);
        int CollectAll(IInventory destination);
        bool TryCollectAll(IInventory destination, out int collected);
    }

    public interface IComponentWindowSection
    {
        void Draw(ComponentWindowContext context);
    }

    public interface IComponentWindowService
    {
        bool IsOpen { get; }
        bool IsPointerOverWindow(Vector2 screenPosition);
        void Open(ComponentWindowRequest request);
        void Close();
    }

    public sealed class ComponentWindowRequest
    {
        public string Title { get; }
        public Vector2 Size { get; }
        public IReadOnlyList<IComponentWindowSection> Sections { get; }
        public Action Closed { get; }

        public ComponentWindowRequest(
            string title,
            Vector2 size,
            IReadOnlyList<IComponentWindowSection> sections,
            Action closed = null)
        {
            Title = string.IsNullOrWhiteSpace(title)
                ? "Component"
                : title;
            Size = size;
            Sections = sections ??
                throw new ArgumentNullException(nameof(sections));
            Closed = closed;
        }

        public ComponentWindowRequest(
            string title,
            Vector2 size,
            IComponentWindowSection section,
            Action closed = null)
            : this(title, size, new[]
            {
                section ?? throw new ArgumentNullException(nameof(section))
            }, closed)
        {
        }
    }

    public sealed class ComponentWindowContext
    {
        private readonly Action _close;

        public string StatusMessage { get; set; }

        public ComponentWindowContext(Action close)
        {
            _close = close ?? throw new ArgumentNullException(nameof(close));
        }

        public void Close()
        {
            _close();
        }
    }

    public sealed class DelegateComponentWindowSection :
        IComponentWindowSection
    {
        private readonly Action<ComponentWindowContext> _draw;

        public DelegateComponentWindowSection(
            Action<ComponentWindowContext> draw)
        {
            _draw = draw ?? throw new ArgumentNullException(nameof(draw));
        }

        public void Draw(ComponentWindowContext context)
        {
            _draw(context);
        }
    }
}
