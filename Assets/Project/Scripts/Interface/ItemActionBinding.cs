using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    /// <summary>
    /// Couples a reusable action asset to one item while it occupies a hotbar
    /// slot. This avoids mutable item state on shared action assets.
    /// </summary>
    public sealed class ItemActionBinding :
        IHotbarAction,
        IDisplayable,
        IUsesCursor,
        IRepeatsWhileHeld
    {
        private readonly ItemData _item;
        private readonly ItemAction _action;

        public string PersistentId => _action.GetPersistentId(_item);
        public Sprite Icon => _item != null ? _item.sprite : null;
        public string DisplayName => _action.GetDisplayName(_item);
        public string Tooltip => _action.GetTooltip(_item);
        public Sprite Sprite => Icon;
        public string Label => DisplayName;
        public int Count => _action.GetCount(_item);
        public float Refresh => _action.Refresh;
        public bool DisplayCount => _action.DisplayCount;
        public ItemData ItemData => _item;
        public bool ConsumesItem => _action.ConsumesItem;
        public float RepeatInterval =>
            (_action as IRepeatsWhileHeld)?.RepeatInterval ?? 0f;

        /// <summary>Creates the runtime hotbar view of an item and its action.</summary>
        public ItemActionBinding(ItemData item)
        {
            _item = item;
            _action = item != null ? item.action : null;
        }

        public bool CanPerform(ActionContext context) =>
            _action != null && _action.CanPerform(WithItem(context));

        public bool Perform(ActionContext context) =>
            _action != null && _action.Perform(WithItem(context));

        public bool TryGetCursor(
            ActionContext context,
            out PlacementCursorData cursor)
        {
            if (_action is IUsesCursor cursorAction)
                return cursorAction.TryGetCursor(
                    WithItem(context),
                    out cursor);

            cursor = default;
            return false;
        }

        // Replace any caller item with the item owned by this binding.
        private ActionContext WithItem(ActionContext context) =>
            new(
                context.User,
                context.TargetPosition,
                _item,
                context.SpawnItemDrop);
    }
}
