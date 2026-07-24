using UnityEngine;

namespace Project.Scripts.DataTypes
{
    public readonly struct ItemActionContext
    {
        public GameObject User { get; }
        public Vector3 TargetPosition { get; }

        public ItemActionContext(GameObject user, Vector3 targetPosition)
        {
            User = user;
            TargetPosition = targetPosition;
        }
    }

    public abstract class ItemAction : ScriptableObject
    {
        [field: SerializeField]
        [field: Tooltip("Remove one item after a successful action.")]
        public bool ConsumesItem { get; private set; }

        public abstract bool CanPerform(ItemActionContext context);
        public abstract bool Perform(ItemActionContext context);
    }
}
