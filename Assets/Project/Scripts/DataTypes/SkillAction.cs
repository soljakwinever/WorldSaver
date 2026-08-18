using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public readonly struct SkillActionContext
    {
        public GameObject User { get; }
        public GameObject Target { get; }
        public Vector3 TargetPosition { get; }
        public SkillData Skill { get; }
        public int ActionIndex { get; }
        public int AttackPotential { get; }
        public Func<GameObject, AttackContext, int> DealDamage { get; }
        public Action<AnimationClip> PlayAnimation { get; }
        public Action<EquipmentStat, int, float, string> ApplyModifier { get; }
        public Action<string> RemoveModifier { get; }
        public Action<ChargeSkillActionData, SkillActionContext> StartCharge { get; }
        public Action<DashSkillActionData, SkillActionContext> StartDash { get; }
        public Action<float, float> RevealFeatures { get; }
        public ProjectileData Projectile { get; }
        public Func<bool> ConsumeProjectile { get; }
        public int ExecutionId { get; }
        public Action<SkillActionData, SkillActionContext, Vector3>
            CompleteAction { get; }

        public string EffectKey => $"{Skill?.persistentId}:{ActionIndex}";

        public SkillActionContext AtPosition(Vector3 position) => new(
            User, Target, position, Skill, ActionIndex, AttackPotential,
            DealDamage, PlayAnimation, ApplyModifier, RemoveModifier,
            StartCharge, StartDash, RevealFeatures, Projectile,
            ConsumeProjectile, ExecutionId, CompleteAction);

        public void Complete(SkillActionData data, Vector3 position) =>
            CompleteAction?.Invoke(data, this, position);

        public SkillActionContext(
            GameObject user, GameObject target, Vector3 targetPosition,
            SkillData skill, int actionIndex,
            int attackPotential,
            Func<GameObject, AttackContext, int> dealDamage,
            Action<AnimationClip> playAnimation,
            Action<EquipmentStat, int, float, string> applyModifier,
            Action<string> removeModifier,
            Action<ChargeSkillActionData, SkillActionContext> startCharge,
            Action<DashSkillActionData, SkillActionContext> startDash,
            Action<float, float> revealFeatures,
            ProjectileData projectile = null,
            Func<bool> consumeProjectile = null,
            int executionId = 0,
            Action<SkillActionData, SkillActionContext, Vector3>
                completeAction = null)
        {
            User = user;
            Target = target;
            TargetPosition = targetPosition;
            Skill = skill;
            ActionIndex = actionIndex;
            AttackPotential = Mathf.Max(0, attackPotential);
            DealDamage = dealDamage;
            PlayAnimation = playAnimation;
            ApplyModifier = applyModifier;
            RemoveModifier = removeModifier;
            StartCharge = startCharge;
            StartDash = startDash;
            RevealFeatures = revealFeatures;
            Projectile = projectile;
            ConsumeProjectile = consumeProjectile;
            ExecutionId = executionId;
            CompleteAction = completeAction;
        }
    }

    public abstract class SkillAction : ScriptableObject
    {
        public abstract Type DataType { get; }
        public virtual bool SupportsMode(SkillActionMode mode) =>
            mode == SkillActionMode.Active;
        public virtual bool CompletesAsynchronously => false;
        public abstract bool CanPerform(SkillActionContext context, SkillActionData data);
        public abstract void Perform(SkillActionContext context, SkillActionData data);
        public virtual void Grant(SkillActionContext context, SkillActionData data) { }
        public virtual void Revoke(SkillActionContext context, SkillActionData data) { }

        public bool Accepts(SkillActionData data) =>
            data != null && DataType.IsInstanceOfType(data);
    }
}
