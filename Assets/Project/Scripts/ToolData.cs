using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts
{
    [CreateAssetMenu(fileName = "New Tool", menuName = "Tool Data", order = 0)]
    public class ToolData : ScriptableObject, IToolData
    {
        [SerializeField]
        private string _toolName;
        [SerializeField]
        private ToolType _toolType;
        [SerializeField]
        private int _power;
        [SerializeField]
        private float _staminaCost;

        public string ToolName => _toolName;

        public ToolType ToolType => _toolType;

        public int Power => _power;

        public float StaminaCost => _staminaCost;
    }
}