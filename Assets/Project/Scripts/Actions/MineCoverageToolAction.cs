using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Actions
{
    /// <summary>
    /// Removes an item-configured amount from the visible coverage on a tile.
    /// </summary>
    [CreateAssetMenu(
        fileName = "New Mine Coverage Tool Action",
        menuName = "Data/Tool Actions/Mine Coverage")]
    public sealed class MineCoverageToolAction :
        ToolAction,
        IRequiresToolProximity
    {
        [SerializeField, Min(0f)] private float interactionRange = 1.75f;

        public bool IsWithinToolRange(ToolActionContext context)
        {
            if (context.User == null)
                return false;

            Vector3Int cell = Vector3Int.FloorToInt(context.TargetPosition);
            Vector2 target = new Vector2(cell.x, cell.y) + new Vector2(0.5f, 0.5f);
            return ((Vector2)context.User.transform.position - target)
                   .sqrMagnitude <= interactionRange * interactionRange;
        }

        public override bool CanPerform(ToolActionContext context)
        {
            return TryGetTarget(context, out _, out _, out _);
        }

        public override bool Perform(ToolActionContext context)
        {
            return TryGetTarget(
                       context,
                       out Chunk chunk,
                       out Vector3Int cell,
                       out MineCoverageToolActionData data) &&
                   chunk.TryReduceCoverage(cell, data.amount);
        }

        private static bool TryGetTarget(
            ToolActionContext context,
            out Chunk chunk,
            out Vector3Int cell,
            out MineCoverageToolActionData data)
        {
            cell = Vector3Int.FloorToInt(context.TargetPosition);
            chunk = null;
            data = null;

            Chunkloader chunkloader = FindAnyObjectByType<Chunkloader>();
            return context.User != null &&
                   context.Item != null &&
                   context.Item.TryGetActionData(out data) &&
                   data.amount > 0f &&
                   chunkloader != null &&
                   chunkloader.TryGetLoadedChunk(cell, out chunk) &&
                   chunk.HasCoverage(cell);
        }
    }
}
