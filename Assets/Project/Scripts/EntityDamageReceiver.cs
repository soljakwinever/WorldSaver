using System;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts
{
    [DisallowMultipleComponent]
    public sealed class EntityDamageReceiver : MonoBehaviour, IDamageable
    {
        [InjectOptional] private IItemStackPickupPool _pickupPool;

        private NodeData _nodeData;
        private PersistentEntity _persistentEntity;
        private PersistentHealth _health;

        public void Initialize(
            NodeData nodeData,
            PersistentEntity persistentEntity,
            PersistentHealth health)
        {
            _nodeData = nodeData;
            _persistentEntity = persistentEntity;
            _health = health;
        }

        public bool CanReceiveDamage(AttackContext context)
        {
            if (_nodeData == null ||
                _persistentEntity == null ||
                !_persistentEntity.CanRemoveFromWorld ||
                context.Force <= 0)
            {
                return false;
            }

            EntityDamageRule[] rules = _nodeData.damageRules;
            if (rules != null && rules.Length > 0)
            {
                foreach (EntityDamageRule rule in rules)
                {
                    if (rule != null && rule.Allows(context))
                        return true;
                }
                return false;
            }

            return context.Source switch
            {
                EntityDamageSource.Enemy => true,
                EntityDamageSource.Tool =>
                    IsCompatibleTool(
                        _nodeData.toolRequirement,
                        context.Weapon),
                _ => false
            };
        }

        public int TakeDamage(AttackContext context)
        {
            if (!CanReceiveDamage(context))
                return 0;

            if (_health != null)
            {
                int delivered = _health.TakeDamage(context);
                if (_health.Health == 0 &&
                    _persistentEntity.CanRemoveFromWorld)
                {
                    SpawnDrop();
                    _persistentEntity.RemoveFromWorld();
                }
                return delivered;
            }

            SpawnDrop();
            _persistentEntity.RemoveFromWorld();
            return context.Force;
        }

        public static bool IsCompatibleTool(
            NodeData.ToolRequirement requirement,
            ToolData tool)
        {
            if (tool == null)
                return false;
            if (requirement == NodeData.ToolRequirement.None)
                return true;

            return requirement switch
            {
                NodeData.ToolRequirement.Pickaxe =>
                    tool.ToolType == ToolType.Pickaxe,
                NodeData.ToolRequirement.Axe =>
                    tool.ToolType == ToolType.Axe,
                NodeData.ToolRequirement.Sword =>
                    tool.ToolType == ToolType.Sword ||
                    tool.ToolType == ToolType.Spear,
                NodeData.ToolRequirement.Shovel =>
                    tool.ToolType == ToolType.Shovel,
                _ => false
            };
        }

        private void SpawnDrop()
        {
            if (_pickupPool == null ||
                _nodeData?.droppedItem == null ||
                _nodeData.dropChance <= 0f ||
                (_nodeData.dropChance < 1f &&
                 UnityEngine.Random.value >= _nodeData.dropChance))
            {
                return;
            }

            _pickupPool.Spawn(
                _nodeData.droppedItem,
                1,
                ItemData.Rarity.Common,
                transform.position);
        }
    }
}
