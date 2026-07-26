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
        
        private float currentCooldown;
        
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
            currentCooldown = Mathf.Max(
                0f,
                currentCooldown - Time.deltaTime);

            if (currentCooldown > 0f)
                return state = NodeState.Failure;

            if (Child == null)
                return state = NodeState.Failure;

            state = Decorate(Child.Evaluate());
            return state;
        }

        protected override NodeState Decorate(NodeState currentState)
        {
            if (currentState == NodeState.Success)
                currentCooldown = cooldown;

            return currentState;
        }
    }
}
