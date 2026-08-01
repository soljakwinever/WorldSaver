using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class SkillCatalog
    {
        private readonly Dictionary<string, SkillData> _skills;

        public SkillCatalog() : this(Resources.LoadAll<SkillData>("Skills")) { }

        public SkillCatalog(IReadOnlyList<SkillData> skills)
        {
            _skills = new Dictionary<string, SkillData>(StringComparer.Ordinal);
            for (int i = 0; i < (skills?.Count ?? 0); i++)
            {
                SkillData skill = skills[i];
                if (skill == null || string.IsNullOrWhiteSpace(skill.persistentId))
                    throw new InvalidOperationException($"Skill catalog entry {i} has no persistent ID.");
                if (!_skills.TryAdd(skill.persistentId, skill))
                    throw new InvalidOperationException($"Duplicate skill ID '{skill.persistentId}'.");
            }
        }

        public bool TryGet(string persistentId, out SkillData skill) =>
            _skills.TryGetValue(persistentId, out skill);
    }
}
