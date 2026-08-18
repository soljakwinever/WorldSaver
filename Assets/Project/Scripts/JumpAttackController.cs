using System.Collections;
using System.Collections.Generic;
using Project.Scripts.AI;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class TimedMovementSpeedMultiplier : MonoBehaviour,
        IMovementSpeedMultiplier
    {
        private readonly Dictionary<string, TimedEffect> _effects = new();

        private readonly struct TimedEffect
        {
            public readonly float Multiplier;
            public readonly float ExpiresAt;
            public TimedEffect(float multiplier, float expiresAt)
            {
                Multiplier = multiplier;
                ExpiresAt = expiresAt;
            }
        }

        public float MovementSpeedMultiplier
        {
            get
            {
                RemoveExpired();
                float result = 1f;
                foreach (TimedEffect effect in _effects.Values)
                    result *= effect.Multiplier;
                return result;
            }
        }

        public void ApplyOrRefresh(
            string effectKey,
            float multiplier,
            float duration)
        {
            if (string.IsNullOrWhiteSpace(effectKey) || multiplier <= 1f ||
                duration <= 0f)
                return;
            _effects[effectKey] = new TimedEffect(
                multiplier,
                Time.time + duration);
        }

        private void RemoveExpired()
        {
            if (_effects.Count == 0)
                return;
            float now = Time.time;
            List<string> expired = null;
            foreach (KeyValuePair<string, TimedEffect> pair in _effects)
                if (pair.Value.ExpiresAt <= now)
                    (expired ??= new List<string>()).Add(pair.Key);
            if (expired == null)
                return;
            foreach (string key in expired)
                _effects.Remove(key);
        }
    }

    [DisallowMultipleComponent]
    public sealed class JumpAttackController : MonoBehaviour, IMovementLock
    {
        private Coroutine _routine;
        public bool IsMovementLocked => _routine != null;

        public static bool CanLand(GameObject user, GameObject target,
            Vector3 landingPosition, JumpAttackSkillActionData data)
        {
            if (user == null || target == null || data == null)
                return false;
            AiNodeRunner runner = user.GetComponent<AiNodeRunner>();
            IPathFindingMap map = runner?.Blackboard.GetOrDefault(
                AiKeys.PathFindingMap);
            Vector2Int cell = Vector2Int.FloorToInt(landingPosition);
            if (map == null || !map.IsWalkable(cell))
                return false;
            return LineOfFireUtility.HasClearPath(
                user.transform.position, landingPosition,
                user.transform, target.transform, data.blockingLayers);
        }

        public bool TryBegin(SkillActionContext context,
            JumpAttackSkillActionData data)
        {
            if (_routine != null || !CanLand(context.User, context.Target,
                    context.TargetPosition, data))
                return false;
            _routine = StartCoroutine(Jump(context, data));
            return true;
        }

        private IEnumerator Jump(SkillActionContext context,
            JumpAttackSkillActionData data)
        {
            Vector3 start = transform.position;
            Vector3 landing = context.TargetPosition;
            landing.z = start.z;
            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body != null) body.linearVelocity = Vector2.zero;
            FalseHeightController height =
                GetComponent<FalseHeightController>() ??
                gameObject.AddComponent<FalseHeightController>();
            height.BeginPhasedJump(data.peakHeight, data.ascentDuration,
                data.hoverDuration, data.descentDuration);
            TransientHealth health = GetComponent<TransientHealth>();

            float ascentStart = Time.time;
            while (Time.time - ascentStart < data.ascentDuration)
            {
                if (health != null && health.IsDead)
                {
                    height.CancelHeight();
                    _routine = null;
                    yield break;
                }
                float t = Mathf.Clamp01(
                    (Time.time - ascentStart) / data.ascentDuration);
                transform.position = Vector3.Lerp(start, landing, t);
                if (body != null) body.linearVelocity = Vector2.zero;
                yield return null;
            }
            transform.position = landing;
            while (height.IsPhasedJump)
            {
                if (health != null && health.IsDead)
                {
                    height.CancelHeight();
                    _routine = null;
                    yield break;
                }
                if (body != null) body.linearVelocity = Vector2.zero;
                yield return null;
            }

            AreaAttackService area = FindFirstObjectByType<AreaAttackService>();
            AreaAttackResult result = default;
            if (area != null)
                result = area.Execute(context.AtPosition(landing),
                    new AreaAttackSkillActionData
                    {
                        baseDamage = data.baseDamage,
                        radius = data.impactRadius,
                        knockbackImpulse = data.knockbackImpulse,
                        stunDuration = data.stunDuration,
                        targetLayers = data.targetLayers,
                        attackType = data.attackType,
                        damageWalls = false,
                        impactParticlePrefab = data.impactParticlePrefab,
                        impactParticleLifetime = data.impactParticleLifetime,
                        element = data.element,
                        damageTags = data.damageTags
                    });
            if (result.DamagedTargetCount > 0 &&
                data.successfulHitMovementMultiplier > 1f &&
                data.successfulHitMovementDuration > 0f)
            {
                TimedMovementSpeedMultiplier speed =
                    GetComponent<TimedMovementSpeedMultiplier>() ??
                    gameObject.AddComponent<TimedMovementSpeedMultiplier>();
                speed.ApplyOrRefresh(
                    context.EffectKey,
                    data.successfulHitMovementMultiplier,
                    data.successfulHitMovementDuration);
            }
            ScreenShakeService shake =
                FindAnyObjectByType<ScreenShakeService>();
            shake?.Shake(data.screenShake);
            context.Complete(data, landing);
            _routine = null;
        }

        private void OnDisable()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            GetComponent<FalseHeightController>()?.CancelHeight();
        }
    }
}
