using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    /// <summary>Runtime inputs shared by item actions.</summary>
    public readonly struct ActionContext
    {
        public GameObject User { get; }
        public Vector3 TargetPosition { get; }
        public ItemData Item { get; }
        public Action<ItemData, Vector3> SpawnItemDrop { get; }

        /// <param name="item">
        /// Item bound to the action. Bindings supply this automatically.
        /// </param>
        public ActionContext(
            GameObject user,
            Vector3 targetPosition,
            ItemData item = null,
            Action<ItemData, Vector3> spawnItemDrop = null)
        {
            User = user;
            TargetPosition = targetPosition;
            Item = item;
            SpawnItemDrop = spawnItemDrop;
        }
    }

    /// <summary>Base type for actions that can validate and execute.</summary>
    public abstract class AssignableAction : ScriptableObject
    {
        public abstract bool CanPerform(ActionContext context);
        public abstract bool Perform(ActionContext context);
    }

    /// <summary>
    /// Reusable item behavior. Item-specific values come from
    /// <see cref="ItemData.ActionData"/>.
    /// </summary>
    public abstract class ItemAction : AssignableAction
    {
        [field: SerializeField]
        [field: Tooltip("Remove one item after a successful action.")]
        public virtual bool ConsumesItem { get; private set; }

        // Display methods receive the item because one action asset may be shared.
        public virtual string GetPersistentId(ItemData item) =>
            item != null ? item.persistentId : string.Empty;
        public virtual string GetDisplayName(ItemData item) =>
            item != null ? item.name : string.Empty;
        public virtual string GetTooltip(ItemData item) =>
            item != null ? item.description : string.Empty;
        public virtual int GetCount(ItemData item) => 0;
        public virtual float Refresh => 0f;
        public virtual bool DisplayCount => false;
    }

}
