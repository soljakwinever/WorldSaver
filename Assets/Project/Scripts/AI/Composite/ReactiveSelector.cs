using System;
using Project.Scripts.AI.GraphEditor;

namespace Project.Scripts.AI.Composite
{
    [Serializable]
    [AiNode(Name = "Reactive Selector", Group = "Composites")]
    public sealed class ReactiveSelector : Composite
    {
        [NonSerialized]
        private int _runningChild = -1;

        protected override NodeState OnTick()
        {
            for (int i = 0; i < children.Count; i++)
            {
                NodeState result = children[i].Evaluate();
                if (result == NodeState.Failure)
                    continue;

                // A selector is priority ordered. Once a child succeeds or is
                // still running, no lower-priority child may remain active.
                AbortLaterChildren(i);
                SetRunningChild(result == NodeState.Running ? i : -1);
                return result;
            }

            SetRunningChild(-1);
            return NodeState.Failure;
        }

        protected override void OnAbort()
        {
            _runningChild = -1;
        }

        private void SetRunningChild(int index)
        {
            if (_runningChild >= 0 && _runningChild != index)
                children[_runningChild].Abort();

            _runningChild = index;
        }

        private void AbortLaterChildren(int selectedIndex)
        {
            for (int i = selectedIndex + 1; i < children.Count; i++)
                children[i]?.Abort();
        }
    }
}
