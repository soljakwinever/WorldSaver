using UnityEngine;

namespace Project.Scripts.AI
{
    /// <summary>
    /// Resolves a physical line of fire while distinguishing the intended
    /// target from cover on the same physics layer.
    /// </summary>
    public static class LineOfFireUtility
    {
        public static bool HasClearPath(
            Vector2 origin,
            Vector2 targetPosition,
            Transform attacker,
            Transform target,
            LayerMask collisionLayers)
        {
            Vector2 direction = targetPosition - origin;
            float distance = direction.magnitude;
            if (distance <= Mathf.Epsilon || collisionLayers.value == 0)
                return true;

            RaycastHit2D[] hits = Physics2D.RaycastAll(
                origin,
                direction / distance,
                distance,
                collisionLayers);
            foreach (RaycastHit2D hit in hits)
            {
                Transform hitTransform = hit.collider != null
                    ? hit.collider.transform
                    : null;
                if (hitTransform == null ||
                    BelongsTo(hitTransform, attacker))
                    continue;

                return target != null && BelongsTo(hitTransform, target);
            }

            return true;
        }

        private static bool BelongsTo(Transform candidate, Transform owner)
        {
            return owner != null &&
                   (candidate == owner ||
                    candidate.IsChildOf(owner) ||
                    owner.IsChildOf(candidate));
        }
    }
}
