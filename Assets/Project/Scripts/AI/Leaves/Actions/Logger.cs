using System;
using Project.Scripts.AI.GraphEditor;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Actions
{
    [Serializable, AiNode("Logger", "Actions")]
    public class Logger : AiNode
    {
        [SerializeField, InputPort("Text")]
        private string text;

        protected override NodeState OnTick()
        {
            Debug.Log(text);
            return NodeState.Success;
        }
    }
}
