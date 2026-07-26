using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public interface IAttackService
    {
        int CalculateDamage(AttackContext context);
        int Attack(IDamageable target, AttackContext context);
    }
}
