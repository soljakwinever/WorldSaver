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
        
        public ResourceType resourceType;

        //Todo: Resource

        public enum NodeType : byte
        {
            Plant,
            Stone,
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