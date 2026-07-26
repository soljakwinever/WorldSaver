using System;
using Project.Scripts.Interface.Decorator;
using UnityEngine;

namespace Project.Scripts.AI.Leaves.Sensors
{
    [Serializable]
    public class CheckHealth : AiNode
    {
        [SerializeField, Tooltip("A component implementing IHasHealth.")]
        private MonoBehaviour _healthSource;

        [SerializeField, Range(0f, 1f)]
        private float _threshold = 0.5f;

        [NonSerialized]
        private IHasHealth _hasHealth;

        [SerializeField]
        private bool _useBlackboardSource;

        [SerializeField]
        private AiKeys.Key _blackboardSourceKey;

        public CheckHealth()
        {
        }

        public CheckHealth(IHasHealth hasHealth, float threshold = 0.5f)
        {
            _hasHealth = hasHealth;
            _healthSource = hasHealth as MonoBehaviour;
            _threshold = threshold;
        }

        public CheckHealth(
            AiKeys.Key blackboardSourceKey,
            float threshold = 0.5f)
        {
            _useBlackboardSource = true;
            _blackboardSourceKey = blackboardSourceKey;
            _threshold = threshold;
        }
        
        protected override NodeState OnTick()
        {
            if (_useBlackboardSource)
            {
                _hasHealth = ResolveBlackboardHealth();
            }
            else
            {
                _hasHealth ??= _healthSource as IHasHealth;
            }

            if (_hasHealth == null)
                return state = NodeState.Failure;

            if (_hasHealth.MaxHealth <= 0)
            {
                state = NodeState.Failure;
                return state;
            }
            
            float percent = (float)_hasHealth.Health / _hasHealth.MaxHealth;

            state = percent < _threshold ? NodeState.Success : NodeState.Failure;
            
            return state;
        }

        private IHasHealth ResolveBlackboardHealth()
        {
            switch (_blackboardSourceKey)
            {
                case AiKeys.Key.Self:
                    return Blackboard
                        .GetOrDefault(AiKeys.Self)
                        ?.GetComponent<IHasHealth>();
                case AiKeys.Key.Target:
                    return Blackboard
                        .GetOrDefault(AiKeys.Target)
                        ?.GetComponent<IHasHealth>();
                default:
                    return null;
            }
        }
    }
}
