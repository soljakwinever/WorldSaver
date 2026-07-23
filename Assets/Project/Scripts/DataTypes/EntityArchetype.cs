using UnityEngine;

namespace Project.Scripts.DataTypes
{
    [CreateAssetMenu(fileName = "New Entity Archetetype", menuName = "World/Entity Archetype", order = 0)]
    public class EntityArchetype : ScriptableObject
    {
        [SerializeField] private int id;
        [SerializeField] private NodeData nodeData;

        public int Id => id;
        public NodeData NodeData => nodeData;
    }
}
