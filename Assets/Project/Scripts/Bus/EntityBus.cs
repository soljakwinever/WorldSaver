using Project.Scripts.DataTypes;
using Project.Scripts.Interface;

namespace Project.Scripts.Bus
{
    public delegate void EnemyDefeatedHandler(
        EnemyData enemy,
        UnityEngine.Vector3 position,
        int experienceValue);

    public delegate void DamageDeliveredHandler(
        IDamageable target,
        AttackContext context,
        int damageDelivered);

    public sealed class EntityBus
    {
        public event DamageDeliveredHandler DamageDelivered;
        public event EnemyDefeatedHandler EnemyDefeated;

        public void RaiseDamageDelivered(
            IDamageable target,
            AttackContext context,
            int damageDelivered)
        {
            DamageDelivered?.Invoke(target, context, damageDelivered);
        }

        public void RaiseEnemyDefeated(
            EnemyData enemy,
            UnityEngine.Vector3 position,
            int experienceValue)
        {
            EnemyDefeated?.Invoke(enemy, position, experienceValue);
        }
    }
}
