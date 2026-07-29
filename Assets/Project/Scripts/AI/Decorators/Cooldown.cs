using System;
using UnityEngine;

namespace Project.Scripts.AI.Decorators
{
    [Serializable]
    [Project.Scripts.AI.GraphEditor.AiNode(
        Name = nameof(Cooldown),
        Group = nameof(Decorators))]
    public class Cooldown : DecoratorNode
    {
        [SerializeField, Min(0)]
        [Project.Scripts.AI.GraphEditor.InputPort("Cooldown")]
        private float cooldown;
        
        [NonSerialized]
        private float _readyTime;
        
        public Cooldown() : base()
        {
        }
        
        public Cooldown(AiNode child) : base(child)
        {
        }

        public Cooldown(AiNode child, float cooldown) : base(child)
        {
            this.cooldown = Mathf.Max(0f, cooldown);
        }

        protected override NodeState OnTick()
        {
            float remaining = _readyTime - Time.time;
            if (remaining > 0f)
            {
                // Ensure the runner wakes on the first frame this action can
                // execute again, even when the normal decision tick is later.
                RequestEvaluation(remaining);
                return state = NodeState.Failure;
            }

            if (Child == null)
                return state = NodeState.Failure;

            state = Decorate(Child.Evaluate());
            return state;
        }

        protected override NodeState Decorate(NodeState currentState)
        {
            if (currentState == NodeState.Success)
            {
                float duration = Mathf.Max(0f, cooldown);
                _readyTime = Time.time + duration;
                if (duration > 0f)
                    RequestEvaluation(duration);
            }

            return currentState;
        }
    }
}
