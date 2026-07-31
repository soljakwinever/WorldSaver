#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Project.Scripts.AI;
using Project.Scripts.AI.Decorators;

namespace Project.Tests.EditMode
{
    public sealed class TickIdleTests
    {
        [Test]
        public void MarksIdleUntilRunningBranchIsAborted()
        {
            Blackboard blackboard = new();
            blackboard.Set(AiKeys.IsIdle, false);
            TickIdle decorator = new(new RunningNode());
            decorator.Bind(blackboard);

            Assert.That(
                decorator.Evaluate(),
                Is.EqualTo(AiNode.NodeState.Running));
            Assert.That(blackboard.Get(AiKeys.IsIdle), Is.True);

            decorator.Abort();

            Assert.That(blackboard.Get(AiKeys.IsIdle), Is.False);
        }

        [Test]
        public void ResetsIdleWhenDecoratedBranchCompletes()
        {
            Blackboard blackboard = new();
            blackboard.Set(AiKeys.IsIdle, false);
            TickIdle decorator = new(new SuccessNode());
            decorator.Bind(blackboard);

            Assert.That(
                decorator.Evaluate(),
                Is.EqualTo(AiNode.NodeState.Success));
            Assert.That(blackboard.Get(AiKeys.IsIdle), Is.False);
        }

        private sealed class RunningNode : AiNode
        {
            protected override NodeState OnTick() => NodeState.Running;
        }

        private sealed class SuccessNode : AiNode
        {
            protected override NodeState OnTick() => NodeState.Success;
        }
    }
}
#endif
