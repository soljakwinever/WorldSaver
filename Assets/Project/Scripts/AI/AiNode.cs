using System;
using System.Collections.Generic;

namespace Project.Scripts.AI
{
    [Serializable]
    public abstract class AiNode
    {
        [NonSerialized]
        protected NodeState state;

        [NonSerialized]
        private bool _isActive;

        public NodeState State => state;
        public bool IsActive => _isActive;
        
        protected Blackboard Blackboard { get; private set; }

        /// <summary>
        /// Child nodes that should share this node's blackboard.
        /// Composite nodes override this property.
        /// </summary>
        protected virtual IEnumerable<AiNode> Children
        {
            get { yield break; }
        }

        public void Bind(Blackboard blackboard)
        {
            Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));

            foreach (AiNode child in Children)
            {
                if (child == null)
                    throw new InvalidOperationException($"{GetType().Name} contains a null child.");

                child.Bind(blackboard);
            }
        }
        
        public NodeState Evaluate()
        {
            if (!_isActive)
            {
                _isActive = true;
                OnEnter();
            }

            state = OnTick();
            if (state == NodeState.Running)
                return state;

            AbortActiveChildren();
            OnExit();
            _isActive = false;
            return state;
        }

        public void Abort()
        {
            if (!_isActive)
                return;

            AbortActiveChildren();
            OnAbort();
            _isActive = false;
        }

        protected virtual void OnEnter()
        {
        }

        protected abstract NodeState OnTick();

        protected virtual void OnExit()
        {
        }

        protected virtual void OnAbort()
        {
        }

        private void AbortActiveChildren()
        {
            foreach (AiNode child in Children)
                child?.Abort();
        }
        
        public enum NodeState{Running, Success, Failure }
    }
}
