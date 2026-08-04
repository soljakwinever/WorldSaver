using System;
using System.Collections.Generic;
using UnityEngine;
using Project.Scripts.Interface;

namespace Project.Scripts.DataTypes
{
    public enum SkillTargetMode : byte { Self, Entity, Point }
    public enum SkillActionMode : byte { Active, Passive }
    public enum SkillCategory : byte { General, Combat, Magic, Survival, Crafting }
    public enum SkillElement : byte { None, Physical, Fire, Water, Earth, Air, Poison, Arcane }

    [CreateAssetMenu(fileName = "New Skill Data", menuName = "Data/Skill Data")]
    public sealed class SkillData : ScriptableObject, IToolTipData
    {
        [Tooltip("Stable identifier used by save data. Do not change after shipping.")]
        public string persistentId;
        [TextArea(2, 4)] public string description;
        public Sprite sprite;
        public SkillCategory category = SkillCategory.General;
        public SkillElement element = SkillElement.None;
        public SkillTargetMode targetMode = SkillTargetMode.Entity;
        [Min(0f)] public float staminaCost;
        [Min(0f)] public float manaCost;
        public int power;
        [Min(0f)] public float duration;
        [Min(0f)] public float cooldown;
        public EntityTag[] tags = Array.Empty<EntityTag>();

        public string DisplayName => name;
        public string Description => description;
        public Color Color => element switch
        {
            SkillElement.Fire => new Color(1f, .38f, .2f),
            SkillElement.Water => new Color(.25f, .6f, 1f),
            SkillElement.Earth => new Color(.65f, .48f, .24f),
            SkillElement.Air => new Color(.65f, .9f, 1f),
            SkillElement.Poison => new Color(.55f, .85f, .25f),
            SkillElement.Arcane => new Color(.75f, .4f, 1f),
            _ => Color.white
        };
        public int Count => 0;
        public Sprite Sprite => sprite;

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
            manaCost = Mathf.Max(0f, manaCost);
            duration = Mathf.Max(0f, duration);
            cooldown = Mathf.Max(0f, cooldown);
            actionData ??= Array.Empty<SkillActionData>();
            tags ??= Array.Empty<EntityTag>();
        }
#endif
    }
}
