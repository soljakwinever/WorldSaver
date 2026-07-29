using Project.Scripts.DataTypes;

namespace Project.Scripts.Interface
{
    /// <summary>
    /// Launches pooled projectiles for any caller that can provide an
    /// AttackContext. Damage is resolved by IAttackService on impact.
    /// </summary>
    public interface IProjectileService
    {
        bool TryLaunch(ProjectileLaunchContext context);
    }
}
