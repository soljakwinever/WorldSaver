using System;
using System.Collections;
using Project.Scripts.AI;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    /// <summary>
    /// Owns a committed enemy attack from telegraph through recovery. Keeping
    /// this outside the behaviour tree prevents chase movement from competing
    /// with timed skills.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAttackController : MonoBehaviour,
        IMovementLock, IStunnable
    {
        public enum AttackPhase : byte
        {
            Ready,
            WindUp,
            Active,
            Recovery
        }

        private Coroutine _routine;
        private GameObject _telegraphRoot;
        private LineRenderer _telegraph;
        private LineRenderer _telegraphHead;
        private Material _telegraphMaterial;
        private SpriteRenderer[] _sprites = Array.Empty<SpriteRenderer>();
        private Color[] _originalColors = Array.Empty<Color>();
        private int _sequence;
        private int _lastCompletedSequence;
        private bool _lastSucceeded;

        public AttackPhase Phase { get; private set; }
        public bool IsMovementLocked => Phase != AttackPhase.Ready;
        public bool IsAttacking => Phase != AttackPhase.Ready;
        public int CurrentSequence => _sequence;

        public bool TryBegin(
            ConditionalEnemySkill attack,
            GameObject target,
            int attackPotential,
            out int sequence)
        {
            sequence = 0;
            if (attack?.skill == null || target == null || IsAttacking)
                return false;

            ISkillRuntime runtime = GetComponentInParent<ISkillRuntime>();
            if (runtime == null || !runtime.CanUse(
                    attack.skill,
                    target,
                    target.transform.position,
                    attackPotential,
                    attack.projectile))
                return false;

            sequence = ++_sequence;
            _routine = StartCoroutine(RunAttack(
                sequence, attack, target, attackPotential, runtime));
            return true;
        }

        public bool TryGetResult(int sequence, out bool succeeded)
        {
            succeeded = _lastSucceeded;
            return sequence > 0 && _lastCompletedSequence == sequence;
        }

        public void Stun(float duration)
        {
            if (duration > 0f && Phase == AttackPhase.WindUp)
                CancelAttack(false);
        }

        private IEnumerator RunAttack(
            int sequence,
            ConditionalEnemySkill attack,
            GameObject target,
            int attackPotential,
            ISkillRuntime runtime)
        {
            Vector3 targetPosition = target.transform.position;
            Face(targetPosition);
            Phase = AttackPhase.WindUp;
            ShowTelegraph(attack, targetPosition);

            float windUp = Mathf.Max(0f, attack.windUpDuration);
            float lockLead = Mathf.Clamp(
                attack.aimLockLeadTime, 0f, windUp);
            float trackingDuration = lockLead > 0f
                ? windUp - lockLead
                : 0f;
            float trackingEnds = Time.time + trackingDuration;
            while (Time.time < trackingEnds)
            {
                if (target == null)
                {
                    Complete(sequence, false);
                    yield break;
                }

                targetPosition = target.transform.position;
                if (!HasClearLineOfFire(
                        attack, targetPosition, target.transform))
                {
                    Complete(sequence, false);
                    yield break;
                }
                Face(targetPosition);
                UpdateTelegraph(attack, targetPosition);
                yield return null;
            }

            if (trackingDuration > 0f && target != null)
            {
                targetPosition = target.transform.position;
                Face(targetPosition);
                UpdateTelegraph(attack, targetPosition);
            }
            yield return Wait(lockLead > 0f ? lockLead : windUp);

            if (sequence != _sequence)
                yield break;

            if (!HasClearLineOfFire(
                    attack,
                    targetPosition,
                    target != null ? target.transform : null))
            {
                Complete(sequence, false);
                yield break;
            }

            HideTelegraph();
            Phase = AttackPhase.Active;
            GameObject releaseTarget = target != null
                ? target
                : attack.projectile != null
                    ? gameObject
                    : null;
            bool used = runtime.TryUseWithWeaponPresentation(
                attack.skill,
                releaseTarget,
                targetPosition,
                attack.weaponSwing,
                null,
                attackPotential,
                attack.projectile);
            if (used)
                yield return Wait(Mathf.Max(0f, attack.activeDuration));

            if (sequence != _sequence)
                yield break;

            Phase = AttackPhase.Recovery;
            yield return Wait(Mathf.Max(0f, attack.recoveryDuration));
            Complete(sequence, used);
        }

        private bool HasClearLineOfFire(
            ConditionalEnemySkill attack,
            Vector3 targetPosition,
            Transform target)
        {
            if (attack.projectile == null ||
                attack.lineOfFireBlockingLayers.value == 0)
                return true;

            return LineOfFireUtility.HasClearPath(
                transform.position,
                targetPosition,
                transform,
                target,
                attack.lineOfFireBlockingLayers);
        }

        private static IEnumerator Wait(float duration)
        {
            float end = Time.time + duration;
            while (Time.time < end)
                yield return null;
        }

        private void Face(Vector3 targetPosition)
        {
            Vector2 direction = targetPosition - transform.position;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            GetComponentInParent<IAttackFacing>()?.SetAttackFacing(direction);

            SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
            if (sprite != null && Mathf.Abs(direction.x) > 0.01f)
                sprite.flipX = direction.x < 0f;
        }

        private void ShowTelegraph(
            ConditionalEnemySkill attack,
            Vector3 targetPosition)
        {
            _sprites = GetComponentsInChildren<SpriteRenderer>();
            _originalColors = new Color[_sprites.Length];
            for (int i = 0; i < _sprites.Length; i++)
            {
                _originalColors[i] = _sprites[i].color;
                _sprites[i].color = Color.Lerp(
                    _sprites[i].color,
                    attack.telegraphColor,
                    0.55f);
            }

            _telegraphRoot = new GameObject("Attack Telegraph");
            _telegraphRoot.transform.SetParent(transform, false);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Destroy(_telegraphRoot);
                _telegraphRoot = null;
                RestoreSpriteColors();
                return;
            }

            _telegraphMaterial = new Material(shader);
            _telegraph = _telegraphRoot.AddComponent<LineRenderer>();
            if (_telegraph == null)
            {
                HideTelegraph();
                return;
            }
            _telegraph.useWorldSpace = true;
            _telegraph.positionCount = 2;
            _telegraph.startWidth = _telegraph.endWidth =
                Mathf.Max(0.01f, attack.telegraphWidth);
            _telegraph.startColor = attack.telegraphColor;
            _telegraph.endColor = new Color(
                attack.telegraphColor.r,
                attack.telegraphColor.g,
                attack.telegraphColor.b,
                0.15f);
            _telegraph.sharedMaterial = _telegraphMaterial;
            _telegraph.sortingOrder = 2;

            if (attack.telegraphShape == EnemyAttackTelegraphShape.Arrow)
            {
                GameObject arrowHead = new("Arrowhead");
                arrowHead.transform.SetParent(_telegraphRoot.transform, false);
                _telegraphHead = arrowHead.AddComponent<LineRenderer>();
                if (_telegraphHead == null)
                {
                    HideTelegraph();
                    return;
                }
                _telegraphHead.useWorldSpace = true;
                _telegraphHead.positionCount = 3;
                _telegraphHead.startWidth = _telegraphHead.endWidth =
                    Mathf.Max(0.01f, attack.telegraphWidth);
                _telegraphHead.startColor = _telegraphHead.endColor =
                    attack.telegraphColor;
                _telegraphHead.sharedMaterial = _telegraphMaterial;
                _telegraphHead.sortingOrder = 2;
            }

            UpdateTelegraph(attack, targetPosition);
        }

        private void UpdateTelegraph(
            ConditionalEnemySkill attack,
            Vector3 targetPosition)
        {
            if (_telegraph == null)
                return;

            Vector3 origin = transform.position;
            Vector3 direction = (targetPosition - origin).normalized;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            float length = Mathf.Max(0.25f, attack.maximumRange);
            Vector3 tip = origin + direction * length;
            _telegraph.SetPosition(0, origin);
            _telegraph.SetPosition(1, tip);

            if (_telegraphHead == null)
                return;

            Vector3 side = new(-direction.y, direction.x, 0f);
            float headLength = Mathf.Clamp(length * 0.15f, 0.25f, 0.65f);
            float headWidth = headLength * 0.65f;
            Vector3 basePoint = tip - direction * headLength;
            _telegraphHead.SetPosition(0, basePoint + side * headWidth);
            _telegraphHead.SetPosition(1, tip);
            _telegraphHead.SetPosition(2, basePoint - side * headWidth);
        }

        private void HideTelegraph()
        {
            RestoreSpriteColors();

            if (_telegraphMaterial != null)
                Destroy(_telegraphMaterial);
            if (_telegraphRoot != null)
                Destroy(_telegraphRoot);
            _telegraphRoot = null;
            _telegraphMaterial = null;
            _telegraph = null;
            _telegraphHead = null;
        }

        private void RestoreSpriteColors()
        {
            for (int i = 0; i < _sprites.Length &&
                            i < _originalColors.Length; i++)
                if (_sprites[i] != null)
                    _sprites[i].color = _originalColors[i];
        }

        private void Complete(int sequence, bool succeeded)
        {
            HideTelegraph();
            Phase = AttackPhase.Ready;
            _routine = null;
            _lastCompletedSequence = sequence;
            _lastSucceeded = succeeded;
        }

        private void CancelAttack(bool succeeded)
        {
            int sequence = _sequence;
            if (_routine != null)
                StopCoroutine(_routine);
            Complete(sequence, succeeded);
        }

        private void OnDisable()
        {
            if (IsAttacking)
                CancelAttack(false);
        }

        private void OnDestroy() => HideTelegraph();
    }
}
