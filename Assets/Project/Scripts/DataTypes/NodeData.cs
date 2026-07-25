using System;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;
using UnityEngine;
using Zenject.ReflectionBaking.Mono.Cecil;

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

        public NodeComponentDefinition[] persistentComponents;
        
        public ResourceType resourceType;

        [SerializeField]
        [Tooltip("Categories used by tools and other node filters.")]
        private EntityTag[] tags = Array.Empty<EntityTag>();

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
    }
}
