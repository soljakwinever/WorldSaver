using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    public interface IDamageable
    {
        /// <returns>The amount of damage actually applied.</returns>
        int TakeDamage(AttackContext context);
    }
}
