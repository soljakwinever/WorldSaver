using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [CreateAssetMenu(fileName = "New Tool Item Action", menuName = "Data/Item Actions/Tool")]
    public sealed class ToolItemAction : ItemAction
    {
        [SerializeField] private ToolData tool;

        public override bool CanPerform(ItemActionContext context)
        {
            return tool != null &&
                   context.User != null &&
                   context.User.TryGetComponent(out PlayerInteractionController controller) &&
                   controller.CanUseTool(tool);
        }

        public override bool Perform(ItemActionContext context)
        {
            return tool != null &&
                   context.User != null &&
                   context.User.TryGetComponent(out PlayerInteractionController controller) &&
                   controller.TryUseTool(tool);
        }
    }
}
