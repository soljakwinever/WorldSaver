using Project.Scripts.DataTypes;
using System.Collections.Generic;

namespace Project.Scripts.Interface
{
    public interface IDamageable
    {
        /// <returns>The amount of damage actually applied.</returns>
        int TakeDamage(AttackContext context);
    }

    public interface IEntityDamageSource
    {
        EntityDamageSource DamageSource { get; }
        IReadOnlyList<EntityTag> DamageTags { get; }
    }
}
