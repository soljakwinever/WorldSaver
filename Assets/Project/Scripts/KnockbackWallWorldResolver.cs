using Project.Scripts.DataTypes.SaveData;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public static class KnockbackWallWorldResolver
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() =>
            KnockbackWallResolver.DamageWall = TryDamageWall;

        private static bool TryDamageWall(
            Vector2 contactPoint,
            Vector2 travelDirection,
            int damage,
            bool allowDestruction,
            out bool destroyed)
        {
            destroyed = false;
            Chunkloader loader = Object.FindFirstObjectByType<Chunkloader>();
            if (loader == null)
                return false;

            Vector2 direction = travelDirection.sqrMagnitude > 0f
                ? travelDirection.normalized
                : Vector2.zero;
            Vector3Int cell = Vector3Int.FloorToInt(
                contactPoint + direction * 0.12f);
            if (!loader.TryGetLoadedChunk(cell, out Chunk chunk) ||
                !chunk.TryGetWallHealth(cell, out _))
                return false;

            if (!allowDestruction)
                return true;

            return chunk.TryDamageWall(
                cell,
                Mathf.Max(1, damage),
                WallDestructionType.Destroyed,
                out destroyed);
        }
    }
}
