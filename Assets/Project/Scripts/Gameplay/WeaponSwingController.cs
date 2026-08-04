using System;
using System.Collections;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    /// <summary>
    /// Displays and evaluates data-driven weapon swings for any GameObject.
    /// Four-directional animations rotate authored frames to the nearest cardinal
    /// direction; 360-degree animations continuously face their target.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponSwingController : MonoBehaviour, IMovementLock
    {
        private SpriteRenderer _weaponRenderer;
        private Coroutine _routine;

        public bool IsSwinging => _routine != null;
        public bool IsMovementLocked { get; private set; }

        public bool TryPlay(
            WeaponSwingAnimation animation,
            Vector3 targetPosition,
            Action<Collider2D> onHurtBoxHit = null,
            Action onRelease = null,
            Transform trackedTarget = null,
            Sprite spriteOverride = null)
        {
            if (animation == null || animation.frames == null ||
                animation.frames.Length == 0 || IsSwinging)
                return false;

            EnsureRenderer();
            _routine = StartCoroutine(PlayRoutine(
                animation, targetPosition, onHurtBoxHit, onRelease,
                trackedTarget, spriteOverride));
            return true;
        }

        public void CancelSwing()
        {
            if (_routine != null)
                StopCoroutine(_routine);
            FinishSwing();
        }

        private IEnumerator PlayRoutine(
            WeaponSwingAnimation animation,
            Vector3 targetPosition,
            Action<Collider2D> onHurtBoxHit,
            Action onRelease,
            Transform trackedTarget,
            Sprite spriteOverride)
        {
            _weaponRenderer.sprite = spriteOverride != null
                ? spriteOverride
                : animation.weaponSprite;
            _weaponRenderer.sortingOrder = animation.sortingOrder;
            _weaponRenderer.enabled = _weaponRenderer.sprite != null;
            bool released = false;

            foreach (WeaponSwingFrame frame in animation.frames)
            {
                if (frame == null)
                    continue;

                float endTime = Time.time + Mathf.Max(0.01f, frame.duration);
                IsMovementLocked = !frame.canMoveWhileSwinging;
                do
                {
                    float directionAngle = ResolveDirectionAngle(
                        animation.displayMode,
                        trackedTarget != null
                            ? trackedTarget.position
                            : targetPosition);
                    ApplyPose(frame, directionAngle, animation.spriteAngleOffset);
                    if (frame.hurtBoxActive && onHurtBoxHit != null &&
                        frame.hurtBox.size.x > 0f && frame.hurtBox.size.y > 0f)
                    {
                        Vector2 localCenter = frame.position + Rotate(
                            frame.hurtBox.center,
                            frame.rotation);
                        Vector2 center = transform.TransformPoint(
                            Rotate(localCenter, directionAngle));
                        Vector2 hurtBoxSize = new(
                            frame.hurtBox.size.x * Mathf.Abs(frame.scale.x),
                            frame.hurtBox.size.y * Mathf.Abs(frame.scale.y));
                        Collider2D[] hits = Physics2D.OverlapBoxAll(
                            center,
                            hurtBoxSize,
                            directionAngle + frame.rotation,
                            animation.targetLayers);
                        foreach (Collider2D hit in hits)
                        {
                            if (hit != null && !hit.transform.IsChildOf(transform) &&
                                !transform.IsChildOf(hit.transform))
                                onHurtBoxHit(hit);
                        }
                    }

                    if (frame.releaseAttack && !released)
                    {
                        released = true;
                        onRelease?.Invoke();
                    }
                    yield return null;
                } while (Time.time < endTime);
            }

            if (!released)
                onRelease?.Invoke();
            FinishSwing();
        }

        private float ResolveDirectionAngle(
            WeaponDisplayMode mode,
            Vector3 targetPosition)
        {
            Vector2 direction = mode == WeaponDisplayMode.FourDirectional
                ? GetComponentInParent<ISkillFacing>()?.FacingDirection ??
                  Vector2.zero
                : targetPosition - transform.position;
            if (direction.sqrMagnitude < 0.0001f)
                direction = targetPosition - transform.position;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.right;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            return mode == WeaponDisplayMode.FourDirectional
                ? Mathf.Round(angle / 90f) * 90f
                : angle;
        }

        private void ApplyPose(
            WeaponSwingFrame frame,
            float directionAngle,
            float spriteAngleOffset)
        {
            _weaponRenderer.transform.localPosition =
                Rotate(frame.position, directionAngle);
            _weaponRenderer.transform.localScale = new Vector3(
                frame.scale.x, frame.scale.y, 1f);
            _weaponRenderer.transform.localRotation = Quaternion.Euler(
                0f, 0f, directionAngle + frame.rotation + spriteAngleOffset);
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(
                value.x * cos - value.y * sin,
                value.x * sin + value.y * cos);
        }

        private void EnsureRenderer()
        {
            if (_weaponRenderer != null)
                return;
            GameObject visual = new("Weapon Swing Visual");
            visual.transform.SetParent(transform, false);
            _weaponRenderer = visual.AddComponent<SpriteRenderer>();
            _weaponRenderer.enabled = false;
        }

        private void FinishSwing()
        {
            IsMovementLocked = false;
            if (_weaponRenderer != null)
                _weaponRenderer.enabled = false;
            _routine = null;
        }

        private void OnDisable() => CancelSwing();

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_weaponRenderer == null || !_weaponRenderer.enabled)
                return;
            Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.55f);
            Gizmos.DrawWireSphere(_weaponRenderer.transform.position, 0.06f);
        }
#endif
    }
}
