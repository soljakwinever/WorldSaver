using System;
using Project.Scripts.AI.GraphEditor;

namespace Project.Scripts.AI.Decorators
{
    /// <summary>
    /// Marks the owner as idle for as long as the decorated branch is running.
    /// The marker is cleared on completion and when a higher-priority branch
    /// aborts this branch.
    /// </summary>
    [Serializable, AiNode(Name = "Tick Idle", Group = "Decorators")]
    public sealed class TickIdle : DecoratorNode
    {
        public TickIdle()
        {
        }

        public TickIdle(AiNode child) : base(child)
        {
        }

        protected override void OnEnter()
        {
            Blackboard.Set(AiKeys.IsIdle, true);
        }

        protected override void OnExit()
        {
            ResetIdle();
        }

        protected override void OnAbort()
        {
            ResetIdle();
        }

        private void ResetIdle()
        {
            Blackboard?.Set(AiKeys.IsIdle, false);
        }
    }
}
