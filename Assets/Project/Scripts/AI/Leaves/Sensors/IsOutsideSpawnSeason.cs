using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;

namespace Project.Scripts.AI.Leaves.Sensors
{
    /// <summary>
    /// Succeeds when the current season is excluded by the transient spawn
    /// rule. Fails safely when the AI was not created by a spawn rule.
    /// </summary>
    [Serializable, AiNode(Name = "Is Outside Spawn Season", Group = "Sensors")]
    public sealed class IsOutsideSpawnSeason : AiNode
    {
        protected override NodeState OnTick()
        {
            if (!Blackboard.TryGet(
                    AiKeys.SpawnRule,
                    out EnemySpawnRule rule) ||
                rule == null ||
                !Blackboard.TryGet(
                    AiKeys.TimeController,
                    out ITimeController time) ||
                time == null)
            {
                return NodeState.Failure;
            }

            return rule.AllowsSeason(time.Season)
                ? NodeState.Failure
                : NodeState.Success;
        }
    }
}
