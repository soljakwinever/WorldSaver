using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>
    /// Routes item use to a <see cref="MineCoverageToolAction"/>.
    /// </summary>
    [CreateAssetMenu(
        fileName = "New Mine Coverage Item Action",
        menuName = "Data/Item Actions/Mine Coverage")]
    public sealed class MineCoverageItemAction :
        ItemAction,
        IRepeatsWhileHeld,
        IUsesCursor
    {
        private const float CursorOpacity = 0.65f;
        private const float RepeatDelay = 0.1f;

        [SerializeField]
        private MineCoverageToolAction _toolAction;
        
        [SerializeField]
        [Tooltip("World-grid cursor shown while this action is selected.")]
        private Sprite placementCursorSprite;

        public override bool CanPerform(ActionContext context)
        {
            return _toolAction != null &&
                   _toolAction.CanPerform(ToToolActionContext(context));
        }

        public override bool Perform(ActionContext context)
        {
            return _toolAction != null &&
                   _toolAction.Perform(ToToolActionContext(context));
        }

        public override float Refresh => RepeatDelay;
        public float RepeatInterval => RepeatDelay;

        public bool TryGetCursor(
            ActionContext context,
            out PlacementCursorData cursor)
        {
            if (placementCursorSprite == null)
            {
                cursor = default;
                return false;
            }

            Vector3Int cell = Vector3Int.FloorToInt(context.TargetPosition);
            cursor = new PlacementCursorData(
                placementCursorSprite,
                new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f),
                CanPerform(context),
                CursorOpacity);
            return true;
        }

        private static ToolActionContext ToToolActionContext(
            ActionContext context)
        {
            return new ToolActionContext(
                context.User,
                context.TargetPosition,
                null,
                context.Item,
                context.SpawnItemDrop);
        }
    }
}
