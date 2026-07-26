using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    /// <summary>Runtime behavior and display identity assigned to a hotbar slot.</summary>
    public interface IHotbarAction
    {
        string PersistentId { get; }
        Sprite Icon { get; }
        string DisplayName { get; }
        
        string Tooltip { get; }
        
        bool CanPerform(ActionContext context);
        bool Perform(ActionContext context);
    }
}
