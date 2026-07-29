using System;
using System.Collections.Generic;

namespace Project.Scripts.AI
{
    [Serializable]
    public abstract class AiNode
    {
        [ThreadStatic]
        private static int _continuationDepth;

        [NonSerialized]
        protected NodeState state;

        [NonSerialized]
        private bool _isActive;

        [NonSerialized]
        private bool _hasEvaluated;

        [NonSerialized]
        private Action<float> _requestEvaluation;

        public NodeState State => state;
        public bool IsActive => _isActive;

        /// <summary>
        /// True while the runner is advancing the previously selected branch
        /// rather than performing a full decision pass.
        /// </summary>
        protected static bool IsContinuationPass => _continuationDepth > 0;
        
        protected Blackboard Blackboard { get; private set; }

        /// <summary>
        /// Child nodes that should share this node's blackboard.
        /// Composite nodes override this property.
        /// </summary>
        protected virtual IEnumerable<AiNode> Children
        {
            get { yield break; }
        }

        public void Bind(
            Blackboard blackboard,
            Action<float> requestEvaluation = null)
        {
            Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            _requestEvaluation = requestEvaluation;

            foreach (AiNode child in Children)
            {
                if (child == null)
                    throw new InvalidOperationException($"{GetType().Name} contains a null child.");

                child.Bind(blackboard, requestEvaluation);
            }
        }
        
        public NodeState Evaluate()
        {
            // A continuation pass advances only the branch selected by the most
            // recent full evaluation. Terminal decision/sensor nodes retain
            // their cached result until the next full pass.
            if (_continuationDepth > 0 && !_isActive)
                return _hasEvaluated ? state : NodeState.Failure;

            if (!_isActive)
            {
                _isActive = true;
                OnEnter();
            }

            state = OnTick();
            _hasEvaluated = true;
            if (state == NodeState.Running)
                return state;

            AbortActiveChildren();
            OnExit();
            _isActive = false;
            return state;
        }

        /// <summary>
        /// Advances the active branch without reevaluating inactive decisions.
        /// </summary>
        public NodeState Continue()
        {
            if (!_isActive)
                return _hasEvaluated ? state : NodeState.Failure;

            _continuationDepth++;
            try
            {
                return Evaluate();
            }
            finally
            {
                _continuationDepth--;
            }
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

        /// <summary>
        /// Requests a full tree evaluation after the supplied delay.
        /// Running actions normally request an immediate evaluation by simply
        /// completing during a continuation pass.
        /// </summary>
        protected void RequestEvaluation(float delaySeconds = 0f)
        {
            _requestEvaluation?.Invoke(Math.Max(0f, delaySeconds));
        }

        private void AbortActiveChildren()
        {
            foreach (AiNode child in Children)
                child?.Abort();
        }
        
        public enum NodeState{Running, Success, Failure }
    }
}
