using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class SkillRuntime : MonoBehaviour
    {
        [SerializeField] private SkillData[] startingPassives = Array.Empty<SkillData>();

        private readonly Dictionary<SkillData, float> _cooldownEnds = new();
        private readonly Dictionary<SkillData, int> _passiveGrants = new();
        private readonly Dictionary<string, ModifierState> _modifiers = new();
        private IAttackService _attackService;

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
        public void Construct(IAttackService attackService) =>
            _attackService = attackService;

        public void Initialize(IAttackService attackService) =>
            _attackService = attackService;

        private void Start()
        {
            foreach (SkillData skill in startingPassives ?? Array.Empty<SkillData>())
                GrantPassive(skill);
        }

        private void Update()
        {
            if (_modifiers.Count == 0)
                return;
            float now = Time.time;
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
            SkillData skill, GameObject target = null, Vector3 targetPosition = default)
        {
            return TryPrepare(skill, target, targetPosition, false, out _, out _);
        }

        public bool TryUse(
            SkillData skill, GameObject target = null, Vector3 targetPosition = default)
        {
            if (!TryPrepare(skill, target, targetPosition, true,
                    out List<(SkillAction action, SkillActionData data, SkillActionContext context)> actions,
                    out ISkillStamina stamina))
                return false;

            if (stamina != null && !stamina.TrySpendStamina(skill.staminaCost))
                return false;

            _cooldownEnds[skill] = Time.time + skill.cooldown;
            foreach (var entry in actions)
                entry.action.Perform(entry.context, entry.data);
            return true;
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
            SkillData skill, GameObject target, Vector3 targetPosition, bool collect,
            out List<(SkillAction, SkillActionData, SkillActionContext)> actions,
            out ISkillStamina stamina)
        {
            actions = collect ? new List<(SkillAction, SkillActionData, SkillActionContext)>() : null;
            stamina = GetComponentInParent<ISkillStamina>();
            if (skill == null || GetRemainingCooldown(skill) > 0f ||
                stamina != null && stamina.CurrentStamina + 0.0001f < skill.staminaCost)
                return false;
            if (skill.targetMode == SkillTargetMode.Entity && target == null)
                return false;
            if (skill.targetMode == SkillTargetMode.Self)
                target = gameObject;

            bool found = false;
            for (int i = 0; i < skill.ActionData.Count; i++)
            {
                SkillActionData data = skill.ActionData[i];
                if (data == null || data.mode != SkillActionMode.Active)
                    continue;
                SkillAction action = data.action;
                SkillActionContext context = CreateContext(skill, target, targetPosition, i);
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
                SkillActionContext context = CreateContext(skill, gameObject, transform.position, i);
                if (grant) action.Grant(context, data);
                else action.Revoke(context, data);
            }
        }

        private SkillActionContext CreateContext(
            SkillData skill, GameObject target, Vector3 position, int index) =>
            new(gameObject, target, position, skill, index, DealDamage,
                PlayAnimation, ApplyModifier, RemoveModifier);

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
    }
}
