using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public enum SkillTargetMode : byte { Self, Entity, Point }
    public enum SkillActionMode : byte { Active, Passive }

    [CreateAssetMenu(fileName = "New Skill Data", menuName = "Data/Skill Data")]
    public sealed class SkillData : ScriptableObject
    {
        [Tooltip("Stable identifier used by save data. Do not change after shipping.")]
        public string persistentId;
        [TextArea(2, 4)] public string description;
        public Sprite sprite;
        public SkillTargetMode targetMode = SkillTargetMode.Entity;
        [Min(0f)] public float staminaCost;
        public int power;
        [Min(0f)] public float duration;
        [Min(0f)] public float cooldown;
        public EntityTag[] tags = Array.Empty<EntityTag>();

        [SerializeReference, ManagedReferenceSelector(typeof(SkillActionData))]
        private SkillActionData[] actionData = Array.Empty<SkillActionData>();

        public IReadOnlyList<SkillActionData> ActionData =>
            actionData ?? Array.Empty<SkillActionData>();

        public float GetDuration(SkillActionData data) =>
            data != null && data.durationOverride >= 0f
                ? data.durationOverride
                : duration;

        private void OnEnable()
        {
            actionData ??= Array.Empty<SkillActionData>();
            tags ??= Array.Empty<EntityTag>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            persistentId = persistentId?.Trim();
            staminaCost = Mathf.Max(0f, staminaCost);
            duration = Mathf.Max(0f, duration);
            cooldown = Mathf.Max(0f, cooldown);
            actionData ??= Array.Empty<SkillActionData>();
            tags ??= Array.Empty<EntityTag>();
        }
#endif
    }
}
