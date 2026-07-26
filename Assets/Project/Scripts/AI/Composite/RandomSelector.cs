using System;
using System.Collections.Generic;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Composite
{
    [Serializable]
    [AiNode(Name = "Random Selector", Group = "Composites")]
    public sealed class RandomSelector : Composite
    {
        [NonSerialized]
        private List<int> _order = new List<int>();

        [NonSerialized]
        private int _current;

        protected override void OnEnter()
        {
            _order ??= new List<int>();
            _order.Clear();
            for (int i = 0; i < children.Count; i++)
                _order.Add(i);

            for (int i = _order.Count - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                (_order[i], _order[swapIndex]) =
                    (_order[swapIndex], _order[i]);
            }

            _current = 0;
        }

        protected override NodeState OnTick()
        {
            while (_current < _order.Count)
            {
                NodeState result = children[_order[_current]].Evaluate();
                if (result != NodeState.Failure)
                    return result;

                _current++;
            }

            return NodeState.Failure;
        }

        protected override void OnAbort()
        {
            _current = 0;
        }
    }
}
