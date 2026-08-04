using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class SkillActionBinding : IHotbarAction, IDisplayable,
        IHotbarFill, IHotbarFillBinding
    {
        private SkillRuntime _runtime;
        public SkillData SkillData { get; }
        public string PersistentId => SkillData?.persistentId ?? string.Empty;
        public Sprite Icon => SkillData?.sprite;
        public string DisplayName => SkillData != null ? SkillData.name : string.Empty;
        public string Tooltip => SkillData?.description ?? string.Empty;
        public string Description => Tooltip;
        public Color Color => SkillData?.Color ?? Color.white;
        public Sprite Sprite => Icon;
        public int Count => Mathf.CeilToInt(_runtime?.GetRemainingCooldown(SkillData) ?? 0f);
        public float Refresh => 0.1f;
        public bool DisplayCount => true;
        public bool DisplayFill => SkillData != null && SkillData.cooldown > 0f;
        public float Fill01 => DisplayFill
            ? Mathf.Clamp01((_runtime?.GetRemainingCooldown(SkillData) ?? 0f) /
                            SkillData.cooldown)
            : 0f;
        public Color FillColor => new(0f, 0f, 0f, 0.65f);
        public float FillRefresh => 0.05f;

        public SkillActionBinding(SkillData skillData) => SkillData = skillData;

        public void BindFillSource(GameObject owner) =>
            _runtime = ResolveRuntime(owner);

        public bool CanPerform(ActionContext context)
        {
            _runtime = ResolveRuntime(context.User);
            return _runtime != null &&
                   _runtime.CanUse(SkillData, context.Target, context.TargetPosition);
        }

        public bool Perform(ActionContext context)
        {
            _runtime = ResolveRuntime(context.User);
            return _runtime != null &&
                   _runtime.TryUse(SkillData, context.Target, context.TargetPosition);
        }

        private static SkillRuntime ResolveRuntime(GameObject user) =>
            user != null ? user.GetComponentInParent<SkillRuntime>() : null;
    }
}
