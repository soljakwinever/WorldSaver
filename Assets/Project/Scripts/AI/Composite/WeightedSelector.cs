using System;
using System.Collections.Generic;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Composite
{
    [Serializable]
    [AiNode(Name = "Weighted Selector", Group = "Composites")]
    public sealed class WeightedSelector : Composite
    {
        [SerializeField, InputPort("Weights")]
        private List<float> weights = new List<float>();

        [NonSerialized]
        private List<int> _order = new List<int>();

        [NonSerialized]
        private int _current;

        protected override void OnEnter()
        {
            BuildWeightedOrder();
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

        private void BuildWeightedOrder()
        {
            _order ??= new List<int>();
            _order.Clear();
            var remaining = new List<int>();
            for (int i = 0; i < children.Count; i++)
                remaining.Add(i);

            while (remaining.Count > 0)
            {
                float total = 0f;
                foreach (int index in remaining)
                    total += GetWeight(index);

                int selectedPosition;
                if (total <= 0f)
                {
                    selectedPosition = UnityEngine.Random.Range(
                        0,
                        remaining.Count);
                }
                else
                {
                    float roll = UnityEngine.Random.value * total;
                    selectedPosition = remaining.Count - 1;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        roll -= GetWeight(remaining[i]);
                        if (roll <= 0f)
                        {
                            selectedPosition = i;
                            break;
                        }
                    }
                }

                _order.Add(remaining[selectedPosition]);
                remaining.RemoveAt(selectedPosition);
            }
        }

        private float GetWeight(int childIndex)
        {
            return childIndex < weights.Count
                ? Mathf.Max(0f, weights[childIndex])
                : 1f;
        }
    }
}
