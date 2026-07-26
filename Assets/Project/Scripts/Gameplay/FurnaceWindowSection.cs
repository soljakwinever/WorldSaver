using System;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class FurnaceWindowSection : IComponentWindowSection
    {
        private readonly IFurnaceStation _furnace;
        private readonly IInventory _player;
        private Vector2 _fuelScroll;

        public FurnaceWindowSection(
            IFurnaceStation furnace,
            IInventory playerInventory)
        {
            _furnace = furnace ??
                throw new ArgumentNullException(nameof(furnace));
            _player = playerInventory ??
                throw new ArgumentNullException(nameof(playerInventory));
        }

        public void Draw(ComponentWindowContext context)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(_furnace.IsBurning ? "Burning" : "Idle",
                GUI.skin.box);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            Rect progressRect = GUILayoutUtility.GetRect(
                1f, 20f, GUILayout.ExpandWidth(true));
            GUI.Box(progressRect, string.Empty);
            Rect fill = progressRect;
            fill.width *= _furnace.Progress01;
            GUI.Box(fill, $"{_furnace.Progress01:P0}");

            GUILayout.BeginHorizontal();
            DrawFuel(context);
            DrawOutput(context);
            GUILayout.EndHorizontal();
        }

        private void DrawFuel(ComponentWindowContext context)
        {
            GUILayout.BeginVertical(GUILayout.Width(220f));
            GUILayout.Label("Fuel (1 stack)", GUI.skin.box);
            if (_furnace.FuelInventory.Stacks.Count == 0)
                GUILayout.Label("[ Empty ]", GUI.skin.box,
                    GUILayout.Height(44f));
            else
            {
                IItemStack fuel = _furnace.FuelInventory.Stacks[0];
                GUILayout.Box($"{fuel.Item.name}\nx{fuel.Count}",
                    GUILayout.Height(44f));
            }

            GUILayout.Label("Add fuel from inventory");
            _fuelScroll = GUILayout.BeginScrollView(
                _fuelScroll, GUILayout.Height(210f));
            foreach (IItemStack stack in _player.Stacks)
            {
                if (!GUILayout.Button($"{stack.Item.name} x{stack.Count}"))
                    continue;

                context.StatusMessage = _furnace.TryInsertFuel(
                    _player,
                    new ItemStack(stack.Item, stack.Count, stack.Rarity))
                    ? "Fuel added."
                    : "That stack cannot be used as fuel here.";
                break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawOutput(ComponentWindowContext context)
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Output (9 slots)", GUI.skin.box);
            if (GUILayout.Button("Collect All", GUILayout.Width(90f)))
            {
                int count = _furnace.CollectAll(_player);
                context.StatusMessage = count > 0
                    ? $"Collected {count} item{(count == 1 ? "" : "s")}."
                    : "No output could be collected.";
            }
            GUILayout.EndHorizontal();

            int stackIndex = 0;
            for (int row = 0; row < 3; row++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < 3; column++)
                {
                    if (stackIndex < _furnace.OutputInventory.Stacks.Count)
                    {
                        IItemStack stack =
                            _furnace.OutputInventory.Stacks[stackIndex++];
                        if (GUILayout.Button(
                                $"{stack.Item.name}\nx{stack.Count}",
                                GUILayout.Width(92f),
                                GUILayout.Height(70f)))
                        {
                            context.StatusMessage =
                                _furnace.TryCollectOutput(
                                    _player,
                                    new ItemStack(
                                        stack.Item,
                                        stack.Count,
                                        stack.Rarity))
                                    ? "Output collected."
                                    : "Player inventory is full.";
                        }
                    }
                    else
                    {
                        GUILayout.Box("Empty", GUILayout.Width(92f),
                            GUILayout.Height(70f));
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }
    }
}
