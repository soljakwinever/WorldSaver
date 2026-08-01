using Project.Scripts.DataTypes;
using Project.Scripts.Interface;

namespace Project.Scripts.Bus
{
    public delegate void EnemyDefeatedHandler(
        EnemyData enemy,
        UnityEngine.Vector3 position,
        int experienceValue,
        UnityEngine.GameObject defeatedBy);

    public delegate void DamageDeliveredHandler(
        IDamageable target,
        AttackContext context,
        int damageDelivered);

    public delegate void AttackResolvedHandler(
        IDamageable target,
        AttackContext context,
        int damageDelivered);

    public delegate void EntityRemovedHandler(
        NodeData nodeData,
        Project.Scripts.DataTypes.SaveData.NodeId entityId,
        UnityEngine.Vector3 position);

    public sealed class EntityBus
    {
        public event DamageDeliveredHandler DamageDelivered;
        public event AttackResolvedHandler AttackResolved;
        public event EnemyDefeatedHandler EnemyDefeated;
        public event EntityRemovedHandler EntityRemoved;

        public void RaiseDamageDelivered(
            IDamageable target,
            AttackContext context,
            int damageDelivered)
        {
            DamageDelivered?.Invoke(target, context, damageDelivered);
        }

        public void RaiseAttackResolved(
            IDamageable target,
            AttackContext context,
            int damageDelivered)
        {
            AttackResolved?.Invoke(target, context, damageDelivered);
        }

        public void RaiseEnemyDefeated(
            EnemyData enemy,
            UnityEngine.Vector3 position,
            int experienceValue,
            UnityEngine.GameObject defeatedBy)
        {
            EnemyDefeated?.Invoke(
                enemy,
                position,
                experienceValue,
                defeatedBy);
        }

        public void RaiseEntityRemoved(
            NodeData nodeData,
            Project.Scripts.DataTypes.SaveData.NodeId entityId,
            UnityEngine.Vector3 position)
        {
            EntityRemoved?.Invoke(nodeData, entityId, position);
        }
    }
}
