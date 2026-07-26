using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>
    /// Routes item use to the tool stored in
    /// <see cref="ToolHotbarActionData"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "New Tool Item Action", menuName = "Data/Item Actions/Tool")]
    public sealed class ToolHotbarAction : ItemAction
    {
        public override string GetPersistentId(ItemData item) =>
            TryGetTool(item, out ToolData tool)
                ? tool.ToolName
                : base.GetPersistentId(item);
        public override string GetDisplayName(ItemData item) =>
            TryGetTool(item, out ToolData tool)
                ? tool.ToolName
                : base.GetDisplayName(item);
        public override string GetTooltip(ItemData item) => string.Empty;

        public override bool CanPerform(ActionContext context)
        {
            if (!TryGetTool(context.Item, out ToolData tool) ||
                context.User == null)
                return false;

            // Tools with action assets use target-based dispatch.
            if (tool.HasActions)
            {
                return tool.CanPerform(
                    new ToolActionContext(
                        context.User,
                        context.TargetPosition,
                        tool,
                        context.Item,
                        context.SpawnItemDrop));
            }

            // Legacy tools interact with the currently focused object.
            return context.User.TryGetComponent(
                       out PlayerInteractionController controller) &&
                   controller.CanUseTool(tool);
        }

        public override bool Perform(ActionContext context)
        {
            if (!TryGetTool(context.Item, out ToolData tool) ||
                context.User == null)
                return false;

            if (tool.HasActions)
            {
                return tool.Perform(
                    new ToolActionContext(
                        context.User,
                        context.TargetPosition,
                        tool,
                        context.Item,
                        context.SpawnItemDrop));
            }

            return context.User.TryGetComponent(
                       out PlayerInteractionController controller) &&
                   controller.TryUseTool(tool);
        }

        private static bool TryGetTool(ItemData item, out ToolData tool)
        {
            if (item != null &&
                item.TryGetActionData(out ToolHotbarActionData data) &&
                data.tool != null)
            {
                tool = data.tool;
                return true;
            }

            tool = null;
            return false;
        }
    }
}
