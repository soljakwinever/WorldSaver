using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.AI
{
    [CreateAssetMenu(fileName = "New AI Node Data", menuName = "AI/AI Node Data", order = 0)]
    public class AiNodeData : BehaviourTreeData
    {
        [SerializeReference, ManagedReferenceSelector(typeof(AiNode))]
        private AiNode _root = new RootNode();

        public AiNode Root => _root;

        public void SetRoot(AiNode root)
        {
            _root = root;
        }

        /// <summary>
        /// Creates an independent copy so runtime state is never shared by runners
        /// that reference the same asset.
        /// </summary>
        public AiNodeData CreateRuntimeInstance()
        {
            AiNodeData instance = Instantiate(this);
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }
    }
}
