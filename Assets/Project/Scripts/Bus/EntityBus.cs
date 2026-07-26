using Project.Scripts.DataTypes;
using Project.Scripts.Interface;

namespace Project.Scripts.Bus
{
    public delegate void DamageDeliveredHandler(
        IDamageable target,
        AttackContext context,
        int damageDelivered);

    public sealed class EntityBus
    {
        public event DamageDeliveredHandler DamageDelivered;

        public void RaiseDamageDelivered(
            IDamageable target,
            AttackContext context,
            int damageDelivered)
        {
            DamageDelivered?.Invoke(target, context, damageDelivered);
        }
    }
}
