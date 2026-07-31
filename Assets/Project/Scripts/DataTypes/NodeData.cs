using System;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;

namespace Project.Scripts
{
    [CreateAssetMenu(fileName = "New Node Data", menuName = "Node Data", order = 0)]
    public sealed class NodeData : ScriptableObject
    {
        public Sprite sprite;
        public bool isTrigger;
        public Sprite[] sprites;
        public NodeType nodeType;
        public ToolRequirement toolRequirement;
        
        public Material overrideMaterial;
        public Color tintColor = Color.white;

        public GameObject overrideVisual;

        public float lightness = 1;
        public float lightnessVariance = 0.0f;

        [SerializeReference]
        [ManagedReferenceSelector(typeof(ComponentDefinitionData))]
        [Tooltip("Persistent component installers and their per-node configuration.")]
        public ComponentDefinitionData[] persistentComponents =
            Array.Empty<ComponentDefinitionData>();

        [Header("Destruction Drop")]
        [Tooltip("Item dropped when this node is successfully destroyed.")]
        public ItemData droppedItem;

        [Range(0f, 1f)]
        [Tooltip("Chance that destroying this node drops its associated item.")]
        public float dropChance = 1f;

        [SerializeField]
        [Tooltip("Categories used by tools and other node filters.")]
        private EntityTag[] tags = Array.Empty<EntityTag>();

        [Header("Incoming Damage")]
        [Tooltip("Rules describing which sources can damage this entity. Empty uses tool-requirement compatibility and allows enemy damage.")]
        public EntityDamageRule[] damageRules =
            Array.Empty<EntityDamageRule>();

        /// <summary>Returns whether this node has the given tag.</summary>
        public bool HasTag(EntityTag tag)
        {
            if (tag == null)
                return false;

            for (int i = 0; i < (tags?.Length ?? 0); i++)
            {
                if (tags[i] == tag)
                    return true;
            }

            return false;
        }

        private void OnEnable()
        {
            persistentComponents ??=
                Array.Empty<ComponentDefinitionData>();
            damageRules ??= Array.Empty<EntityDamageRule>();
        }

        //Todo: Resource

        public enum NodeType : byte
        {
            Harvestable,
            Buildable,
            Entity,
        }
        
        public enum ToolRequirement
        {
            None,   // No tool requirement, can pick up with interact
            Pickaxe,
            Axe,
            Sword,
            Shovel,
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            persistentComponents ??=
                Array.Empty<ComponentDefinitionData>();
            damageRules ??= Array.Empty<EntityDamageRule>();
        }
#endif
    }
}
