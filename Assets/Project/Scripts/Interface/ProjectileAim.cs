using UnityEngine;

namespace Project.Scripts.Interface
{
    public static class ProjectileAim
    {
        private const float MaximumInaccuracyDegrees = 30f;

        public static Vector2 PredictDirection(
            Vector3 origin,
            Transform target,
            float projectileSpeed,
            float maximumPredictionTime)
        {
            if (target == null)
                return Vector2.zero;

            Vector2 displacement = target.position - origin;
            float speed = Mathf.Max(0f, projectileSpeed);
            Rigidbody2D targetBody = target.GetComponentInParent<Rigidbody2D>();
            Vector2 targetVelocity = targetBody != null
                ? targetBody.linearVelocity
                : Vector2.zero;
            if (speed <= Mathf.Epsilon ||
                targetVelocity.sqrMagnitude <= Mathf.Epsilon)
            {
                return displacement.normalized;
            }

            float a = targetVelocity.sqrMagnitude - speed * speed;
            float b = 2f * Vector2.Dot(displacement, targetVelocity);
            float c = displacement.sqrMagnitude;
            float interceptTime = 0f;

            if (Mathf.Abs(a) <= Mathf.Epsilon)
            {
                if (Mathf.Abs(b) > Mathf.Epsilon)
                    interceptTime = -c / b;
            }
            else
            {
                float discriminant = b * b - 4f * a * c;
                if (discriminant >= 0f)
                {
                    float root = Mathf.Sqrt(discriminant);
                    float first = (-b - root) / (2f * a);
                    float second = (-b + root) / (2f * a);
                    if (first > 0f && second > 0f)
                        interceptTime = Mathf.Min(first, second);
                    else
                        interceptTime = Mathf.Max(first, second);
                }
            }

            if (interceptTime <= 0f)
                return displacement.normalized;

            interceptTime = Mathf.Min(
                interceptTime,
                Mathf.Max(0f, maximumPredictionTime));
            return (displacement + targetVelocity * interceptTime).normalized;
        }

        public static Vector3 ResolveDirectionalOrigin(
            Vector3 position,
            Vector2 direction,
            Vector2 offset)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon)
                return position + (Vector3)offset;

            direction.Normalize();
            Vector2 perpendicular = new(-direction.y, direction.x);
            return position +
                   (Vector3)(direction * offset.x + perpendicular * offset.y);
        }

        public static Vector2 ApplyAccuracy(
            Vector2 direction,
            float accuracy)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon)
                return Vector2.zero;

            float maximumError =
                (1f - Mathf.Clamp01(accuracy)) *
                MaximumInaccuracyDegrees;
            if (maximumError <= Mathf.Epsilon)
                return direction.normalized;

            float error = Random.Range(-maximumError, maximumError);
            return (Quaternion.Euler(0f, 0f, error) * direction).normalized;
        }
    }
}
