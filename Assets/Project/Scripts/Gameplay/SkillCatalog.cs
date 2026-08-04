using System;
using System.Collections.Generic;
using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class SkillCatalog
    {
        private readonly Dictionary<string, SkillData> _skills;
        private readonly List<SkillData> _skillList;
        private readonly Dictionary<SkillCategory, SkillTreeData> _trees;

        public IReadOnlyList<SkillData> Skills => _skillList;
        public IReadOnlyDictionary<SkillCategory, SkillTreeData> Trees => _trees;

        public SkillCatalog() : this(
            Resources.LoadAll<SkillData>("Skills"),
            Resources.LoadAll<SkillTreeData>("SkillTrees")) { }

        public SkillCatalog(IReadOnlyList<SkillData> skills) : this(skills, null) { }

        public SkillCatalog(IReadOnlyList<SkillData> skills, IReadOnlyList<SkillTreeData> trees)
        {
            _skills = new Dictionary<string, SkillData>(StringComparer.Ordinal);
            _skillList = new List<SkillData>(skills?.Count ?? 0);
            for (int i = 0; i < (skills?.Count ?? 0); i++)
            {
                SkillData skill = skills[i];
                if (skill == null || string.IsNullOrWhiteSpace(skill.persistentId))
                    throw new InvalidOperationException($"Skill catalog entry {i} has no persistent ID.");
                if (!_skills.TryAdd(skill.persistentId, skill))
                    throw new InvalidOperationException($"Duplicate skill ID '{skill.persistentId}'.");
                _skillList.Add(skill);
            }

            _skillList.Sort((left, right) =>
            {
                int categoryComparison = left.category.CompareTo(right.category);
                return categoryComparison != 0
                    ? categoryComparison
                    : string.Compare(
                        left.name,
                        right.name,
                        StringComparison.OrdinalIgnoreCase);
            });

            _trees = new Dictionary<SkillCategory, SkillTreeData>();
            for (int i = 0; i < (trees?.Count ?? 0); i++)
            {
                SkillTreeData tree = trees[i];
                if (tree == null)
                    continue;
                if (!_trees.TryAdd(tree.category, tree))
                    throw new InvalidOperationException($"Duplicate skill tree category '{tree.category}'.");
                tree.EvaluateLayout();
            }
        }

        public bool TryGet(string persistentId, out SkillData skill) =>
            _skills.TryGetValue(persistentId, out skill);
    }
}
