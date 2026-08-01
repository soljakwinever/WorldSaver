using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Scripts.DataTypes;

namespace Project.Scripts
{
    /// <summary>Runtime inputs passed from an item action to a tool action.</summary>
    public readonly struct ToolActionContext
    {
        public GameObject User { get; }
        public Vector3 TargetPosition { get; }
        public ToolData Tool { get; }
        public ItemData Item { get; }
        public Action<ItemData, Vector3> SpawnItemDrop { get; }

        public ToolActionContext(
            GameObject user,
            Vector3 targetPosition,
            ToolData tool,
            ItemData item = null,
            Action<ItemData, Vector3> spawnItemDrop = null)
        {
            User = user;
            TargetPosition = targetPosition;
            Tool = tool;
            Item = item;
            SpawnItemDrop = spawnItemDrop;
        }
    }

    /// <summary>Target-based behavior that can be shared by tool definitions.</summary>
    public abstract class ToolAction : ScriptableObject
    {
        public abstract bool CanPerform(ToolActionContext context);
        public abstract bool Perform(ToolActionContext context);
    }

    /// <summary>
    /// Tool stats and ordered behaviors. Per-item behavior settings remain on
    /// <see cref="ItemData"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "New Tool", menuName = "Tool Data", order = 0)]
    public class ToolData : ScriptableObject
    {
        [SerializeField]
        private string _toolName;
        [SerializeField]
        private ToolType _toolType;
        [SerializeField]
        private int _power;
        [SerializeField]
        [Tooltip("Determines which player stat increases this tool's attack damage.")]
        private PlayerAttackType _attackType = PlayerAttackType.Melee;
        [SerializeField]
        private float _staminaCost;
        [SerializeField]
        [Tooltip("Ordered actions this tool can perform. The first applicable action is used.")]
        private ToolAction[] _actions = Array.Empty<ToolAction>();
        [SerializeField]
        [Tooltip("Tags supplied by this tool when entity damage rules are evaluated.")]
        private EntityTag[] _damageTags = Array.Empty<EntityTag>();

        public string ToolName => _toolName;

        public ToolType ToolType => _toolType;

        public int Power => _power;

        public PlayerAttackType AttackType => _attackType;

        public float StaminaCost => _staminaCost;

        public IReadOnlyList<ToolAction> Actions => _actions;
        public IReadOnlyList<EntityTag> DamageTags =>
            _damageTags ?? Array.Empty<EntityTag>();
        public bool HasActions
        {
            get
            {
                if (_actions == null)
                    return false;

                foreach (ToolAction action in _actions)
                {
                    if (action != null)
                        return true;
                }

                return false;
            }
        }

        /// <summary>Checks whether any configured action accepts the context.</summary>
        public bool CanPerform(ToolActionContext context)
        {
            if (_actions == null)
                return false;

            foreach (ToolAction action in _actions)
            {
                if (CanUseAction(action, context) &&
                    action.CanPerform(context))
                    return true;
            }

            return false;
        }

        /// <summary>Runs the first configured action that accepts the context.</summary>
        public bool Perform(ToolActionContext context)
        {
            if (_actions == null)
                return false;

            foreach (ToolAction action in _actions)
            {
                if (CanUseAction(action, context) &&
                    action.CanPerform(context))
                    return action.Perform(context);
            }

            return false;
        }

        private static bool CanUseAction(
            ToolAction action,
            ToolActionContext context)
        {
            return action != null &&
                   (action is not IRequiresToolProximity proximity ||
                    proximity.IsWithinToolRange(context));
        }
    }
    
    public enum ToolType
    {
        None = 0,
        Axe,
        Pickaxe,
        Hoe,
        WateringCan,
        Shovel,
        FishingRod,
        Hammer,
        Sword,
        Spear,
    }
}
