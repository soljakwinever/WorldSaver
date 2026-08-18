namespace Project.Scripts.Interface
{
    public interface IKnockbackState
    {
        bool IsLaunched { get; }
        void Launch(UnityEngine.Vector2 direction, float power);
    }

    public delegate bool KnockbackWallDamageHandler(
        UnityEngine.Vector2 contactPoint,
        UnityEngine.Vector2 travelDirection,
        int damage,
        bool allowDestruction,
        out bool destroyed);

    public static class KnockbackWallResolver
    {
        public static KnockbackWallDamageHandler DamageWall { get; set; }

        public static bool TryDamageWall(
            UnityEngine.Vector2 contactPoint,
            UnityEngine.Vector2 travelDirection,
            int damage,
            bool allowDestruction,
            out bool destroyed)
        {
            destroyed = false;
            return DamageWall?.Invoke(
                contactPoint,
                travelDirection,
                damage,
                allowDestruction,
                out destroyed) == true;
        }
    }
}
