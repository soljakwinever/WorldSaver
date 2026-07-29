using System;
using Project.Scripts.AI.GraphEditor;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;

namespace Project.Scripts.AI.Leaves.Sensors
{
    /// <summary>
    /// Succeeds when the current hour is outside the transient spawn rule's
    /// configured window. Fails safely when the AI was not created by a rule.
    /// </summary>
    [Serializable, AiNode(Name = "Is Outside Spawn Hours", Group = "Sensors")]
    public sealed class IsOutsideSpawnHours : AiNode
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

            return rule.AllowsHour(time.Hour)
                ? NodeState.Failure
                : NodeState.Success;
        }
    }
}
