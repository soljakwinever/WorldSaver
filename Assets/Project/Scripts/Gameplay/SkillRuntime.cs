using System;
using System.Collections.Generic;
using System.Collections;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using Project.Scripts.Interface.Decorator;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class SkillRuntime : MonoBehaviour, ISkillRuntime
    {
        [SerializeField] private SkillData[] startingPassives = Array.Empty<SkillData>();

        private readonly Dictionary<SkillData, float> _cooldownEnds = new();
        private readonly Dictionary<SkillData, int> _passiveGrants = new();
        private readonly Dictionary<string, ModifierState> _modifiers = new();
        private readonly Dictionary<string, float> _dashMultipliers = new();
        private readonly Dictionary<string, float> _dashExpires = new();
        private IAttackService _attackService;
        private IProjectileService _projectileService;
        private ISenseService _senseService;
        public Sprite WeaponSpriteOverride { get; private set; }
        public WeaponSwingAnimation WeaponSwingOverride { get; private set; }

        private readonly struct ModifierState
        {
            public readonly EquipmentStat Stat;
            public readonly int Amount;
            public readonly float ExpiresAt;
            public ModifierState(EquipmentStat stat, int amount, float expiresAt)
            {
                Stat = stat;
                Amount = amount;
                ExpiresAt = expiresAt;
            }
        }

        [Inject]
        public void Construct(
            IAttackService attackService,
            IProjectileService projectileService)
        {
            _attackService = attackService;
            _projectileService = projectileService;
            EnsureProjectileBridge();
        }

        public void Initialize(
            IAttackService attackService,
            ISenseService senseService = null,
            IProjectileService projectileService = null)
        {
            _attackService = attackService;
            if (projectileService != null)
                _projectileService = projectileService;
            EnsureProjectileBridge();
            if (senseService != null)
                _senseService = senseService;
        }

        private void Start()
        {
            foreach (SkillData skill in startingPassives ?? Array.Empty<SkillData>())
                GrantPassive(skill);
        }

        private void Update()
        {
            float now = Time.time;
            if (_modifiers.Count > 0)
                RemoveExpiredModifiers(now);
            if (_dashExpires.Count > 0)
                RemoveExpiredDashes(now);
        }

        private void RemoveExpiredModifiers(float now)
        {
            List<string> expired = null;
            foreach (KeyValuePair<string, ModifierState> pair in _modifiers)
            {
                if (!float.IsPositiveInfinity(pair.Value.ExpiresAt) && pair.Value.ExpiresAt <= now)
                    (expired ??= new List<string>()).Add(pair.Key);
            }
            if (expired == null)
                return;
            foreach (string key in expired)
                _modifiers.Remove(key);
        }

        private void RemoveExpiredDashes(float now)
        {
            List<string> expired = null;
            foreach (KeyValuePair<string, float> pair in _dashExpires)
            {
                if (pair.Value <= now)
                    (expired ??= new List<string>()).Add(pair.Key);
            }
            if (expired == null)
                return;
            foreach (string key in expired)
            {
                _dashExpires.Remove(key);
                _dashMultipliers.Remove(key);
            }
        }

        public float GetMovementSpeedMultiplier()
        {
            float multiplier = 1f;
            float now = Time.time;
            foreach (KeyValuePair<string, float> pair in _dashMultipliers)
            {
                if (_dashExpires.TryGetValue(pair.Key, out float expires) && expires > now)
                    multiplier *= pair.Value;
            }
            return multiplier;
        }

        public float GetRemainingCooldown(SkillData skill)
        {
            if (skill == null || !_cooldownEnds.TryGetValue(skill, out float end))
                return 0f;
            return Mathf.Max(0f, end - Time.time);
        }

        public int GetStatModifier(EquipmentStat stat)
        {
            int total = 0;
            float now = Time.time;
            foreach (ModifierState modifier in _modifiers.Values)
            {
                if (modifier.Stat == stat &&
                    (float.IsPositiveInfinity(modifier.ExpiresAt) || modifier.ExpiresAt > now))
                    total = checked(total + modifier.Amount);
            }
            return total;
        }

        public bool CanUse(
            SkillData skill, GameObject target = null,
            Vector3 targetPosition = default, int attackPotential = 0,
            ProjectileData projectile = null)
        {
            return TryPrepare(
                skill, target, targetPosition, attackPotential, projectile,
                false, out _, out _, out _);
        }

        public bool TryUse(
            SkillData skill, GameObject target = null,
            Vector3 targetPosition = default, int attackPotential = 0,
            ProjectileData projectile = null)
        {
            if (!TryPrepare(
                    skill, target, targetPosition, attackPotential, projectile,
                    true,
                    out List<(SkillAction action, SkillActionData data, SkillActionContext context)> actions,
                    out ISkillStamina stamina,
                    out IHasMana mana))
                return false;

            if (stamina != null && !stamina.TrySpendStamina(skill.staminaCost))
                return false;
            if (mana != null && !mana.TrySpendMana(skill.manaCost))
                return false;

            _cooldownEnds[skill] = Time.time + skill.cooldown;
            foreach (var entry in actions)
                entry.action.Perform(entry.context, entry.data);
            return true;
        }

        /// <summary>
        /// Uses a skill with an item-specific weapon sprite for this execution.
        /// The override is scoped so shared skill and animation assets remain
        /// immutable.
        /// </summary>
        public bool TryUseWithWeaponSprite(
            SkillData skill,
            GameObject target,
            Vector3 targetPosition,
            Sprite weaponSpriteOverride,
            int attackPotential = 0,
            ProjectileData projectile = null)
        {
            return TryUseWithWeaponPresentation(
                skill,
                target,
                targetPosition,
                null,
                weaponSpriteOverride,
                attackPotential,
                projectile);
        }

        public bool TryUseWithWeaponPresentation(
            SkillData skill,
            GameObject target,
            Vector3 targetPosition,
            WeaponSwingAnimation weaponSwingOverride,
            Sprite weaponSpriteOverride = null,
            int attackPotential = 0,
            ProjectileData projectile = null)
        {
            Sprite previousSprite = WeaponSpriteOverride;
            WeaponSwingAnimation previousSwing = WeaponSwingOverride;
            WeaponSpriteOverride = weaponSpriteOverride;
            WeaponSwingOverride = weaponSwingOverride;
            try
            {
                return TryUse(
                    skill,
                    target,
                    targetPosition,
                    attackPotential,
                    projectile);
            }
            finally
            {
                WeaponSpriteOverride = previousSprite;
                WeaponSwingOverride = previousSwing;
            }
        }

        public void GrantPassive(SkillData skill)
        {
            if (skill == null)
                return;
            if (_passiveGrants.TryGetValue(skill, out int count))
            {
                _passiveGrants[skill] = count + 1;
                return;
            }
            _passiveGrants.Add(skill, 1);
            VisitPassive(skill, true);
        }

        public void RevokePassive(SkillData skill)
        {
            if (skill == null || !_passiveGrants.TryGetValue(skill, out int count))
                return;
            if (count > 1)
            {
                _passiveGrants[skill] = count - 1;
                return;
            }
            _passiveGrants.Remove(skill);
            VisitPassive(skill, false);
        }

        private bool TryPrepare(
            SkillData skill, GameObject target, Vector3 targetPosition,
            int attackPotential, ProjectileData suppliedProjectile, bool collect,
            out List<(SkillAction, SkillActionData, SkillActionContext)> actions,
            out ISkillStamina stamina,
            out IHasMana mana)
        {
            actions = collect ? new List<(SkillAction, SkillActionData, SkillActionContext)>() : null;
            stamina = GetComponentInParent<ISkillStamina>();
            mana = GetComponentInParent<IHasMana>();
            if (skill == null || GetRemainingCooldown(skill) > 0f ||
                stamina != null && stamina.CurrentStamina + 0.0001f < skill.staminaCost ||
                skill.manaCost > 0f && (mana == null || mana.CurrentMana + 0.0001f < skill.manaCost))
                return false;
            if (skill.targetMode == SkillTargetMode.Entity && target == null)
                return false;
            if (skill.targetMode == SkillTargetMode.Self)
                target = gameObject;

            ResolveProjectile(
                suppliedProjectile,
                out ProjectileData projectile,
                out Func<bool> consumeProjectile);

            bool found = false;
            for (int i = 0; i < skill.ActionData.Count; i++)
            {
                SkillActionData data = skill.ActionData[i];
                if (data == null || data.mode != SkillActionMode.Active)
                    continue;
                SkillAction action = data.action;
                SkillActionContext context = CreateContext(
                    skill, target, targetPosition, i, attackPotential,
                    projectile, consumeProjectile);
                if (action == null || !action.Accepts(data) ||
                    !action.SupportsMode(data.mode) || !action.CanPerform(context, data))
                    return false;
                found = true;
                actions?.Add((action, data, context));
            }
            return found;
        }

        private void VisitPassive(SkillData skill, bool grant)
        {
            for (int i = 0; i < skill.ActionData.Count; i++)
            {
                SkillActionData data = skill.ActionData[i];
                SkillAction action = data?.action;
                if (data == null || data.mode != SkillActionMode.Passive || action == null ||
                    !action.Accepts(data) || !action.SupportsMode(data.mode))
                    continue;
                SkillActionContext context = CreateContext(
                    skill, gameObject, transform.position, i, 0,
                    null, null);
                if (grant) action.Grant(context, data);
                else action.Revoke(context, data);
            }
        }

        private SkillActionContext CreateContext(
            SkillData skill, GameObject target, Vector3 position, int index,
            int attackPotential, ProjectileData projectile,
            Func<bool> consumeProjectile)
        {
            ISenseService senseService = ResolveSenseService();
            return new SkillActionContext(
                gameObject, target, position, skill, index, attackPotential, DealDamage,
                PlayAnimation, ApplyModifier, RemoveModifier, StartCharge,
                StartDash,
                senseService?.CanSense(gameObject) == true
                    ? RevealFeatures
                    : null,
                projectile,
                consumeProjectile);
        }

        private void ResolveProjectile(
            ProjectileData supplied,
            out ProjectileData projectile,
            out Func<bool> consume)
        {
            projectile = supplied;
            consume = null;
            if (projectile != null)
                return;

            IInventory inventory =
                GetComponentInParent<PersistentInventory>();
            if (inventory == null)
                return;

            foreach (IItemStack stack in inventory.Stacks)
            {
                if (stack?.Item?.projectile == null || stack.Count <= 0)
                    continue;
                ItemData item = stack.Item;
                ItemData.Rarity rarity = stack.Rarity;
                projectile = item.projectile;
                consume = () => inventory.TryRemove(item, 1, rarity);
                return;
            }
        }

        private void EnsureProjectileBridge()
        {
            if (_projectileService == null)
                return;
            SkillRuntimeProjectileBridge bridge =
                GetComponent<SkillRuntimeProjectileBridge>() ??
                gameObject.AddComponent<SkillRuntimeProjectileBridge>();
            bridge.Initialize(_projectileService);
        }

        private ISenseService ResolveSenseService()
        {
            if (_senseService != null)
                return _senseService;
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is ISenseService senseService)
                {
                    _senseService = senseService;
                    break;
                }
            }
            return _senseService;
        }

        private void RevealFeatures(float radius, float duration) =>
            _senseService?.Reveal(gameObject, radius, duration);

        private int DealDamage(GameObject target, AttackContext context)
        {
            if (_attackService == null || target == null)
                return 0;
            IDamageable damageable = target.GetComponentInParent<IDamageable>() ??
                                     target.GetComponentInChildren<IDamageable>();
            return damageable != null ? _attackService.Attack(damageable, context) : 0;
        }

        private void PlayAnimation(AnimationClip clip)
        {
            if (clip != null)
                GetComponentInParent<ISkillAnimationPlayer>()?.PlaySkillAnimation(clip);
        }

        private void ApplyModifier(EquipmentStat stat, int amount, float duration, string key)
        {
            float expires = duration < 0f ? float.PositiveInfinity : Time.time + duration;
            _modifiers[key] = new ModifierState(stat, amount, expires);
        }

        private void RemoveModifier(string key) => _modifiers.Remove(key);

        private void StartCharge(
            ChargeSkillActionData data,
            SkillActionContext context) =>
            StartCoroutine(ChargeRoutine(data, context));

        private void StartDash(DashSkillActionData data, SkillActionContext context)
        {
            float duration = Mathf.Max(0.02f, context.Skill.GetDuration(data));
            string key = context.EffectKey;
            float expires = Time.time + duration;
            _dashMultipliers[key] = Mathf.Max(1f, data.speedMultiplier);
            _dashExpires[key] = expires;
            StartCoroutine(DashAfterimageRoutine(data, key, expires));
        }

        private IEnumerator DashAfterimageRoutine(
            DashSkillActionData data, string key, float expires)
        {
            float interval = Mathf.Max(0.01f, data.afterimageInterval);
            while (_dashExpires.TryGetValue(key, out float currentExpiry) &&
                   Mathf.Approximately(currentExpiry, expires) && Time.time < expires)
            {
                SpawnAfterimage(data);
                yield return new WaitForSeconds(interval);
            }
        }

        private void SpawnAfterimage(DashSkillActionData data)
        {
            SpriteRenderer[] sources = GetComponentsInChildren<SpriteRenderer>();
            GameObject root = new($"{name} Dash Afterimage");
            root.transform.position = Vector3.zero;
            List<SpriteRenderer> copies = new();
            List<float> startingAlphas = new();
            foreach (SpriteRenderer source in sources)
            {
                if (source == null || !source.enabled || source.sprite == null)
                    continue;
                GameObject ghost = new(source.gameObject.name);
                ghost.transform.SetParent(root.transform, false);
                ghost.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                ghost.transform.localScale = source.transform.lossyScale;
                SpriteRenderer copy = ghost.AddComponent<SpriteRenderer>();
                copy.sprite = source.sprite;
                copy.flipX = source.flipX;
                copy.flipY = source.flipY;
                copy.sharedMaterial = source.sharedMaterial;
                copy.sortingLayerID = source.sortingLayerID;
                copy.sortingOrder = source.sortingOrder - 1;
                Color tint = data.afterimageTint;
                tint.a *= source.color.a;
                copy.color = tint;
                copies.Add(copy);
                startingAlphas.Add(tint.a);
            }
            if (copies.Count == 0)
            {
                Destroy(root);
                return;
            }
            StartCoroutine(FadeAfterimage(
                root, copies, startingAlphas,
                Mathf.Max(0.01f, data.afterimageFadeDuration)));
        }

        private static IEnumerator FadeAfterimage(
            GameObject root, List<SpriteRenderer> renderers,
            List<float> startingAlphas, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = 1f - Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < renderers.Count; i++)
                {
                    SpriteRenderer renderer = renderers[i];
                    if (renderer == null) continue;
                    Color color = renderer.color;
                    color.a = startingAlphas[i] * alpha;
                    renderer.color = color;
                }
                yield return null;
            }
            Destroy(root);
        }

        private IEnumerator ChargeRoutine(
            ChargeSkillActionData data,
            SkillActionContext context)
        {
            Rigidbody2D body = GetComponentInParent<Rigidbody2D>();
            if (body == null)
                yield break;

            Vector2 direction = ResolveChargeDirection(
                data.directionMode,
                body.position,
                context.TargetPosition,
                GetComponentInParent<ISkillFacing>()?.FacingDirection ?? Vector2.down);

            float duration = Mathf.Max(0.02f, data.travelDuration);
            float speed = Mathf.Max(0f, data.distance) / duration;
            float remaining = Mathf.Max(0f, data.distance);
            HashSet<IDamageable> hitTargets = new();
            Transform userTransform = context.User.transform;
            while (remaining > 0f)
            {
                yield return new WaitForFixedUpdate();
                float step = Mathf.Min(remaining, speed * Time.fixedDeltaTime);
                Vector2 start = body.position;
                Collider2D[] hits = Physics2D.OverlapCapsuleAll(
                    start + direction * (step * 0.5f),
                    new Vector2(data.hitRadius * 2f, step + data.hitRadius * 2f),
                    CapsuleDirection2D.Vertical,
                    Mathf.Atan2(-direction.x, direction.y) * Mathf.Rad2Deg);
                foreach (Collider2D hit in hits)
                {
                    if (hit == null ||
                        hit.transform == userTransform ||
                        hit.transform.IsChildOf(userTransform) ||
                        userTransform.IsChildOf(hit.transform))
                        continue;

                    IDamageable target =
                        hit.GetComponentInParent<IDamageable>() ??
                        hit.GetComponentInChildren<IDamageable>();
                    if (target == null || !hitTargets.Add(target))
                        continue;

                    GameObject targetObject = target is Component component
                        ? component.gameObject
                        : hit.gameObject;
                    HitChargedTarget(
                        targetObject, direction, data, context);
                }
                body.MovePosition(start + direction * step);
                remaining -= step;
            }
        }

        public static Vector2 ResolveChargeDirection(
            ChargeDirectionMode mode,
            Vector2 userPosition,
            Vector2 targetPosition,
            Vector2 facingDirection)
        {
            Vector2 direction = mode == ChargeDirectionMode.TowardCursor
                ? targetPosition - userPosition
                : facingDirection;
            if (direction.sqrMagnitude < 0.0001f)
                direction = facingDirection;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.down;
            return direction.normalized;
        }

        private void HitChargedTarget(
            GameObject target,
            Vector2 direction,
            ChargeSkillActionData data,
            SkillActionContext context)
        {
            List<EntityTag> tags = new();
            IEntityDamageSource damageSource =
                context.User.GetComponentInParent<IEntityDamageSource>();
            if (damageSource?.DamageTags != null)
                tags.AddRange(damageSource.DamageTags);
            if (context.Skill?.tags != null) tags.AddRange(context.Skill.tags);
            if (data.damageTags != null) tags.AddRange(data.damageTags);
            if (data.element != null) tags.Add(data.element);
            DealDamage(target, new AttackContext(
                gameObject, null,
                checked(data.baseDamage + context.AttackPotential),
                damageSource?.DamageSource ?? EntityDamageSource.Skill, tags,
                context.Skill, data.attackType, SkillPowerMode.Multiplier));

            Rigidbody2D targetBody = target.GetComponentInParent<Rigidbody2D>();
            if (targetBody != null && data.knockbackImpulse > 0f)
                targetBody.AddForce(
                    direction * data.knockbackImpulse,
                    ForceMode2D.Impulse);
        }
    }
}
