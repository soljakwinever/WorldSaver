using System;
using Project.Scripts.AI.GraphEditor;

namespace Project.Scripts.AI.Composite
{
    [Serializable]
    [AiNode(Name = "Reactive Sequence", Group = "Composites")]
    public sealed class ReactiveSequence : Composite
    {
        [NonSerialized]
        private int _runningChild = -1;

        protected override NodeState OnTick()
        {
            for (int i = 0; i < children.Count; i++)
            {
                NodeState result = children[i].Evaluate();
                if (result == NodeState.Success)
                    continue;

                SetRunningChild(result == NodeState.Running ? i : -1);
                return result;
            }

            SetRunningChild(-1);
            return NodeState.Success;
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
    }
}
