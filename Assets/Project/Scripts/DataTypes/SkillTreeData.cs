using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [Serializable]
    public sealed class SkillTreeNode
    {
        public SkillData skill;
        [Tooltip("Offset from the parent, measured in skill-node UI units.")]
        public Vector2Int position = Vector2Int.up;
        [SerializeReference, ManagedReferenceSelector(typeof(SkillTreeNode))]
        public List<SkillTreeNode> connections = new();

        [NonSerialized] private SkillTreeNode _parent;
        public SkillTreeNode Parent => _parent;

        internal void SetParent(SkillTreeNode parent) => _parent = parent;
    }

    [CreateAssetMenu(fileName = "New Skill Tree", menuName = "Data/Skill Tree")]
    public sealed class SkillTreeData : ScriptableObject
    {
        public SkillCategory category = SkillCategory.General;
        [SerializeReference, ManagedReferenceSelector(typeof(SkillTreeNode))]
        public SkillTreeNode root;

        /// <summary>
        /// Assigns runtime parents and returns positions relative to the root.
        /// The root is (0,0); positive Y therefore grows upward on screen.
        /// </summary>
        public IReadOnlyDictionary<SkillTreeNode, Vector2Int> EvaluateLayout()
        {
            var positions = new Dictionary<SkillTreeNode, Vector2Int>();
            var occupied = new Dictionary<Vector2Int, SkillTreeNode>();
            var visiting = new HashSet<SkillTreeNode>();
            if (root != null)
                Evaluate(root, null, Vector2Int.zero, positions, occupied, visiting);
            return positions;
        }

        private void Evaluate(
            SkillTreeNode node,
            SkillTreeNode parent,
            Vector2Int absolutePosition,
            IDictionary<SkillTreeNode, Vector2Int> positions,
            IDictionary<Vector2Int, SkillTreeNode> occupied,
            ISet<SkillTreeNode> visiting)
        {
            if (node == null)
                throw new InvalidOperationException($"Skill tree '{name}' contains a null connection.");
            if (!visiting.Add(node))
                throw new InvalidOperationException($"Skill tree '{name}' contains a cycle at '{NodeName(node)}'.");
            if (positions.ContainsKey(node))
                throw new InvalidOperationException($"Skill tree node '{NodeName(node)}' has more than one parent.");
            if (node.skill == null)
                throw new InvalidOperationException($"Skill tree '{name}' contains a node with no skill.");
            if (absolutePosition.y < 0)
                throw new InvalidOperationException(
                    $"Skill tree '{name}' places '{NodeName(node)}' below its root. The root must remain at the bottom.");
            if (occupied.TryGetValue(absolutePosition, out SkillTreeNode other))
                throw new InvalidOperationException(
                    $"Skill tree '{name}' places '{NodeName(node)}' and '{NodeName(other)}' at {absolutePosition}.");

            node.SetParent(parent);
            positions.Add(node, absolutePosition);
            occupied.Add(absolutePosition, node);
            node.connections ??= new List<SkillTreeNode>();
            foreach (SkillTreeNode child in node.connections)
                Evaluate(child, node, absolutePosition + child.position, positions, occupied, visiting);
            visiting.Remove(node);
        }

        private static string NodeName(SkillTreeNode node) =>
            node?.skill != null ? node.skill.name : "unnamed node";
    }
}
