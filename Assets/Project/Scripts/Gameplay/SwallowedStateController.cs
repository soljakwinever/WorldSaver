using System;
using System.Collections;
using Project.Scripts.AI;
using Project.Scripts.Core;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class SwallowOccupancyState : MonoBehaviour,
        ISwallowOccupancyState, IOffscreenRetreatState,
        IOffscreenDespawnHandler
    {
        private SwallowedStateController _occupant;
        public bool IsFull => _occupant != null && _occupant.IsSwallowed;
        public bool ShouldRetreatOffscreen { get; private set; }

        public bool TryOccupy(
            SwallowedStateController occupant,
            bool requestOffscreenRetreat)
        {
            if (occupant == null || IsFull)
                return false;
            _occupant = occupant;
            if (requestOffscreenRetreat)
            {
                ShouldRetreatOffscreen = true;
                GetComponent<AiNodeRunner>()?.TickEarly();
            }
            return true;
        }

        public void Clear(SwallowedStateController occupant)
        {
            if (_occupant == occupant)
                _occupant = null;
        }

        public void PrepareForOffscreenDespawn()
        {
            _occupant?.FinishDigestionForOwnerDespawn();
            _occupant = null;
        }
    }

    [DisallowMultipleComponent]
    public sealed class SwallowedStateController : MonoBehaviour,
        ISwallowedState, ITargetableState, IMovementLock
    {
        private GameObject _owner;
        private Transform _originalParent;
        private SwallowSkillActionData _settings;
        private PlayerDataController _player;
        private PersistentHealth _health;
        private TransientHealth _transientHealth;
        private IDamageable _damageable;
        private TransientHealth _ownerHealth;
        private FalseHeightController _height;
        private IWorldClock _clock;
        private long _nextDamageTick;
        private float _struggleLockedUntil;
        private bool _releasing;
        private bool _wasPresentationVisible = true;
        private Rigidbody2D _body;
        private bool _bodyWasSimulated;
        private Vector3 _ownerOriginalScale;
        private Coroutine _scaleRoutine;
        private SwallowStruggleBar _struggleBar;
        private Vector3 _playerOriginalLocalScale;
        private Quaternion _playerOriginalLocalRotation;
        private AiNodeRunner _enemyAi;
        private bool _enemyAiWasEnabled;
        private EnemyAttackController _enemyAttack;
        private bool _enemyAttackWasEnabled;
        private SwallowOccupancyState _occupancy;

        public bool IsSwallowed => _owner != null;
        public bool CanBeTargeted => !IsSwallowed;
        public bool IsMovementLocked => IsSwallowed;
        public float Struggle01 { get; private set; }
        public event Action<float> StruggleChanged;

        private void Awake()
        {
            _player = GetComponent<PlayerDataController>();
            _health = GetComponent<PersistentHealth>();
            _transientHealth = GetComponent<TransientHealth>();
            _damageable = GetComponent<IDamageable>();
            _height = GetComponent<FalseHeightController>() ??
                      gameObject.AddComponent<FalseHeightController>();
            _body = GetComponent<Rigidbody2D>();
            _clock = FindAnyObjectByType<WorldClock>();
        }

        private void Update()
        {
            if (!IsSwallowed)
                return;
            if (_owner == null || !_owner.activeInHierarchy)
            {
                Release(false);
                return;
            }

            SetStruggle(Struggle01 -
                        _settings.struggleDecayPerSecond * Time.deltaTime);
            long tick = _clock?.CurrentTick ?? 0;
            if (tick < _nextDamageTick)
                return;

            _nextDamageTick = tick + Mathf.Max(1, _settings.damageIntervalTicks);
            _struggleLockedUntil = Time.time + _settings.struggleLockDuration;
            if (_damageable != null && IsAlive())
            {
                GameObject owner = _owner;
                SwallowSkillActionData settings = _settings;
                int delivered = _damageable.TakeDamage(new AttackContext(
                    owner,
                    null,
                    Mathf.Max(1, _settings.stomachDamage),
                    EntityDamageSource.Skill,
                    cause: DamageCause.Digestion));
                if (_player == null && delivered > 0 && owner != null)
                {
                    TransientHealth ownerHealth =
                        owner.GetComponent<TransientHealth>();
                    ownerHealth?.Heal(Mathf.RoundToInt(
                        delivered * Mathf.Max(0f,
                            settings.digestionHealingRatio)));
                }
            }
        }

        public bool TrySwallow(GameObject owner, SwallowSkillActionData settings)
        {
            if (owner == null || settings == null || IsSwallowed)
                return false;

            _occupancy = owner.GetComponent<SwallowOccupancyState>() ??
                         owner.AddComponent<SwallowOccupancyState>();
            bool npcCaptive = _player == null &&
                              GetComponent<EnemyRuntime>() != null;
            if (!_occupancy.TryOccupy(
                    this,
                    npcCaptive && settings.retreatOffscreenAfterNpcCapture))
                return false;

            _owner = owner;
            _settings = settings;
            _originalParent = transform.parent;
            _playerOriginalLocalScale = transform.localScale;
            _playerOriginalLocalRotation = transform.localRotation;
            SetStruggle(0f);
            _struggleLockedUntil = 0f;
            _clock ??= FindAnyObjectByType<WorldClock>();
            _nextDamageTick = (_clock?.CurrentTick ?? 0) +
                              Mathf.Max(1, settings.damageIntervalTicks);
            _height ??= GetComponent<FalseHeightController>() ??
                        gameObject.AddComponent<FalseHeightController>();
            _wasPresentationVisible = _height == null ||
                                      _height.PresentationVisible;
            _height?.SetPresentationVisible(false);

            _body ??= GetComponent<Rigidbody2D>();
            if (_body != null)
            {
                _bodyWasSimulated = _body.simulated;
                _body.linearVelocity = Vector2.zero;
                _body.simulated = false;
            }
            _enemyAi = GetComponent<AiNodeRunner>();
            if (_enemyAi != null)
            {
                _enemyAiWasEnabled = _enemyAi.enabled;
                _enemyAi.enabled = false;
            }
            _enemyAttack = GetComponent<EnemyAttackController>();
            if (_enemyAttack != null)
            {
                _enemyAttackWasEnabled = _enemyAttack.enabled;
                _enemyAttack.enabled = false;
            }
            transform.SetParent(owner.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            _ownerOriginalScale = owner.transform.localScale;
            StartScaleTransition(
                owner.transform,
                _ownerOriginalScale,
                Vector3.Scale(
                    _ownerOriginalScale,
                    Vector3.one * Mathf.Max(1f, settings.swallowedScaleMultiplier)),
                settings.scaleTransitionDuration);

            _player ??= GetComponent<PlayerDataController>();
            if (_player != null)
            {
                _struggleBar = owner.GetComponent<SwallowStruggleBar>() ??
                               owner.AddComponent<SwallowStruggleBar>();
                _struggleBar.Bind(this, settings);
            }
            _ownerHealth = owner.GetComponent<TransientHealth>();
            if (_ownerHealth != null)
                _ownerHealth.Died += OnOwnerDeath;
            return true;
        }

        public bool TryStruggle()
        {
            if (!IsSwallowed || Time.time < _struggleLockedUntil)
                return false;

            ISkillStamina stamina = GetComponent<ISkillStamina>();
            float cost = Mathf.Max(0f, _settings.struggleEnergyCost);
            float spent = stamina != null
                ? Mathf.Min(cost, Mathf.Max(0f, stamina.CurrentStamina))
                : 0f;
            if (spent > 0f && !stamina.TrySpendStamina(spent))
                spent = 0f;
            float funded = cost > 0f ? Mathf.Clamp01(spent / cost) : 1f;
            float gain = Mathf.Lerp(
                _settings.exhaustedStruggleGain,
                _settings.fullStruggleGain,
                funded);
            SetStruggle(Struggle01 + Mathf.Max(0f, gain));
            if (Struggle01 >= 1f)
                Release(true);
            return true;
        }

        public bool IsCombatAction(IHotbarAction action)
        {
            SkillData skill = action switch
            {
                SkillActionBinding binding => binding.SkillData,
                ItemActionBinding { ItemData: not null } binding
                    when binding.ItemData.TryGetActionData(
                        out UseSkillItemActionData data) => data.skill,
                _ => null
            };
            if (skill == null)
                return false;
            foreach (SkillActionData data in skill.ActionData)
                if (data is AttackSkillActionData or
                    ForwardBoxAttackSkillActionData or
                    AreaAttackSkillActionData or
                    ProjectileSkillActionData or
                    ExplosiveProjectileSkillActionData or
                    ChargeSkillActionData or JumpAttackSkillActionData)
                    return true;
            return false;
        }

        public static bool OwnerHasSwallowedTarget(GameObject owner)
        {
            if (owner == null)
                return false;
            SwallowOccupancyState occupancy =
                owner.GetComponent<SwallowOccupancyState>();
            if (occupancy != null)
                return occupancy.IsFull;
            foreach (SwallowedStateController state in
                     FindObjectsByType<SwallowedStateController>(
                         FindObjectsInactive.Include))
                if (state != null && state._owner == owner)
                    return true;
            return false;
        }

        public static bool ReleaseOwnedBy(GameObject owner)
        {
            if (owner == null)
                return false;
            bool released = false;
            foreach (SwallowedStateController state in
                     FindObjectsByType<SwallowedStateController>(
                         FindObjectsInactive.Include))
            {
                if (state == null || state._owner != owner)
                    continue;
                state.Release(false);
                released = true;
            }
            return released;
        }

        public bool ConsumeForDeath()
        {
            if (!IsSwallowed || _releasing)
                return false;
            _releasing = true;
            GameObject owner = _owner;
            _struggleBar?.Unbind();
            _struggleBar = null;
            Unsubscribe();
            _occupancy?.Clear(this);
            _occupancy = null;
            StopScaleTransition();
            if (owner != null)
                owner.transform.localScale = _ownerOriginalScale;
            SetStruggle(0f);
            _owner = null;
            _settings = null;
            _releasing = false;
            return true;
        }

        public void FinishDigestionForOwnerDespawn()
        {
            if (!IsSwallowed || _player != null || _damageable == null)
                return;
            GameObject owner = _owner;
            SwallowSkillActionData settings = _settings;
            if (owner == null)
                return;
            int delivered = _damageable.TakeDamage(new AttackContext(
                owner,
                null,
                int.MaxValue,
                EntityDamageSource.Skill,
                cause: DamageCause.Digestion));
            if (delivered > 0 && owner != null)
                owner.GetComponent<TransientHealth>()?.Heal(Mathf.RoundToInt(
                    delivered * Mathf.Max(0f,
                        settings?.digestionHealingRatio ?? 0f)));
        }

        public void Release(bool stunOwner)
        {
            if (!IsSwallowed || _releasing)
                return;
            _releasing = true;
            GameObject owner = _owner;
            SwallowSkillActionData settings = _settings;
            _struggleBar?.Unbind();
            _struggleBar = null;
            Unsubscribe();
            _occupancy?.Clear(this);
            _occupancy = null;
            Vector3 ownerPosition = owner != null
                ? owner.transform.position
                : transform.position;
            Vector2 away = (Vector2)(transform.position - ownerPosition);
            if (away.sqrMagnitude < 0.001f)
                away = owner != null
                    ? -(owner.GetComponent<ISkillFacing>()?.FacingDirection ?? Vector2.down)
                    : Vector2.down;

            transform.SetParent(_originalParent, true);
            transform.localScale = _playerOriginalLocalScale;
            transform.localRotation = _playerOriginalLocalRotation;
            Vector3 releasePosition = ResolveReleasePosition(
                ownerPosition, away, settings);
            transform.position = releasePosition;
            if (_body != null)
            {
                _body.position = releasePosition;
                _body.linearVelocity = Vector2.zero;
                _body.simulated = _bodyWasSimulated;
            }
            if (_enemyAi != null)
                _enemyAi.enabled = _enemyAiWasEnabled;
            _enemyAi = null;
            if (_enemyAttack != null)
                _enemyAttack.enabled = _enemyAttackWasEnabled;
            _enemyAttack = null;
            _height?.SetPresentationVisible(_wasPresentationVisible);
            SetStruggle(0f);
            _owner = null;
            _settings = null;

            if (owner != null)
            {
                if (stunOwner)
                    StartScaleTransition(
                        owner.transform,
                        owner.transform.localScale,
                        _ownerOriginalScale,
                        settings.scaleTransitionDuration);
                else
                {
                    StopScaleTransition();
                    owner.transform.localScale = _ownerOriginalScale;
                }
            }

            if (stunOwner && owner != null)
            {
                float duration = Mathf.Max(0f, settings.escapeStunDuration);
                foreach (MonoBehaviour behaviour in
                         owner.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour is IStunnable stunnable)
                        stunnable.Stun(duration);
                AiNodeRunner runner = owner.GetComponent<AiNodeRunner>();
                runner?.Blackboard.Set<Transform>(AiKeys.Target, null);
                runner?.TickEarly();
            }
            _releasing = false;
        }

        private void SetStruggle(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(Struggle01, clamped))
                return;
            Struggle01 = clamped;
            StruggleChanged?.Invoke(Struggle01);
        }

        private void StartScaleTransition(
            Transform target,
            Vector3 from,
            Vector3 to,
            float duration)
        {
            StopScaleTransition();
            if (target == null)
                return;
            if (duration <= 0f)
            {
                target.localScale = to;
                return;
            }
            _scaleRoutine = StartCoroutine(
                ScaleOverTime(target, from, to, duration));
        }

        private IEnumerator ScaleOverTime(
            Transform target,
            Vector3 from,
            Vector3 to,
            float duration)
        {
            float start = Time.time;
            while (target != null)
            {
                float t = Mathf.Clamp01((Time.time - start) / duration);
                float eased = t * t * (3f - 2f * t);
                target.localScale = Vector3.LerpUnclamped(from, to, eased);
                if (t >= 1f)
                    break;
                yield return null;
            }
            _scaleRoutine = null;
        }

        private void StopScaleTransition()
        {
            if (_scaleRoutine == null)
                return;
            StopCoroutine(_scaleRoutine);
            _scaleRoutine = null;
        }

        private void OnOwnerDeath() => Release(false);

        private Vector3 ResolveReleasePosition(
            Vector3 origin,
            Vector2 preferredDirection,
            SwallowSkillActionData settings)
        {
            float distance = Mathf.Max(0f, settings.escapeDistance);
            Vector2 preferred = preferredDirection.sqrMagnitude > 0.001f
                ? preferredDirection.normalized
                : Vector2.down;
            for (int index = 0; index < 8; index++)
            {
                float angle = index == 0
                    ? 0f
                    : (index % 2 == 1 ? (index + 1) / 2 : -index / 2) * 45f;
                Vector2 direction = Quaternion.Euler(0f, 0f, angle) * preferred;
                Vector3 candidate = origin + (Vector3)(direction * distance);
                bool blocked = false;
                foreach (Collider2D hit in Physics2D.OverlapCircleAll(
                             candidate, 0.25f, settings.releaseBlockingLayers))
                {
                    if (hit == null || hit.transform.IsChildOf(transform) ||
                        _owner != null && hit.transform.IsChildOf(_owner.transform))
                        continue;
                    blocked = true;
                    break;
                }
                if (!blocked)
                    return candidate;
            }
            return origin + (Vector3)(preferred * distance);
        }

        private void Unsubscribe()
        {
            if (_ownerHealth != null)
                _ownerHealth.Died -= OnOwnerDeath;
            _ownerHealth = null;
        }

        private void OnDisable()
        {
            // Unity forbids changing a child hierarchy while an ancestor is
            // in the middle of activation/deactivation. Enemy death releases
            // explicitly before that process starts.
        }

        private void OnDestroy()
        {
            _occupancy?.Clear(this);
            _struggleBar?.Unbind();
            StopScaleTransition();
            Unsubscribe();
        }

        private bool IsAlive()
        {
            if (_health != null)
                return _health.Health > 0;
            if (_transientHealth != null)
                return !_transientHealth.IsDead && _transientHealth.Health > 0;
            return true;
        }
    }
}
