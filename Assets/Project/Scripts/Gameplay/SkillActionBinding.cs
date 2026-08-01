using Project.Scripts.DataTypes;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class SkillActionBinding : IHotbarAction, IDisplayable
    {
        private SkillRuntime _runtime;
        public SkillData SkillData { get; }
        public string PersistentId => SkillData?.persistentId ?? string.Empty;
        public Sprite Icon => SkillData?.sprite;
        public string DisplayName => SkillData != null ? SkillData.name : string.Empty;
        public string Tooltip => SkillData?.description ?? string.Empty;
        public Sprite Sprite => Icon;
        public string Label => DisplayName;
        public int Count => Mathf.CeilToInt(_runtime?.GetRemainingCooldown(SkillData) ?? 0f);
        public float Refresh => 0.1f;
        public bool DisplayCount => true;

        public SkillActionBinding(SkillData skillData) => SkillData = skillData;

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
