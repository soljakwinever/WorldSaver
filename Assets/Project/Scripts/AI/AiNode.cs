using System;
using System.Collections.Generic;
using System.Reflection;

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

        [NonSerialized]
        private List<(FieldInfo field, AiFloatExpression expression)>
            _runtimeFloatBindings;

        [UnityEngine.SerializeField, UnityEngine.HideInInspector]
        private List<AiFloatFieldBinding> _floatBindings = new();

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
            BindFloatExpressions();

            foreach (AiNode child in Children)
            {
                if (child == null)
                    throw new InvalidOperationException($"{GetType().Name} contains a null child.");

                child.Bind(blackboard, requestEvaluation);
            }
        }
        
        public NodeState Evaluate()
        {
            ApplyFloatExpressions();

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

        public void SetFloatBinding(
            string fieldName,
            AiFloatExpression expression)
        {
            if (string.IsNullOrEmpty(fieldName) || expression == null)
                return;

            _floatBindings ??= new List<AiFloatFieldBinding>();
            _floatBindings.RemoveAll(binding => binding.fieldName == fieldName);
            _floatBindings.Add(new AiFloatFieldBinding(fieldName, expression));
        }

        private void BindFloatExpressions()
        {
            _runtimeFloatBindings = null;
            if (_floatBindings == null || _floatBindings.Count == 0)
                return;

            foreach (AiFloatFieldBinding binding in _floatBindings)
            {
                if (binding?.expression == null)
                    continue;

                FieldInfo matchedField = null;
            for (Type type = GetType();
                 type != null && type != typeof(object);
                 type = type.BaseType)
            {
                    matchedField = type.GetField(
                        binding.fieldName,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                    if (matchedField != null)
                        break;
            }

                if (matchedField?.FieldType != typeof(float))
                    continue;

                _runtimeFloatBindings ??=
                    new List<(FieldInfo, AiFloatExpression)>();
                _runtimeFloatBindings.Add(
                    (matchedField, binding.expression));
            }

            ApplyFloatExpressions();
        }

        private void ApplyFloatExpressions()
        {
            if (_runtimeFloatBindings == null || Blackboard == null)
                return;

            foreach (var binding in _runtimeFloatBindings)
            {
                binding.field.SetValue(
                    this,
                    binding.expression.Evaluate(Blackboard));
            }
        }

        private void AbortActiveChildren()
        {
            foreach (AiNode child in Children)
                child?.Abort();
        }
        
        public enum NodeState{Running, Success, Failure }
    }
}
