using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Interface
{
    /// <summary>Runtime behavior and display identity assigned to a hotbar slot.</summary>
    public interface IHotbarAction : IToolTipData
    {
        string PersistentId { get; }
        Sprite Icon { get; }
        string Tooltip { get; }
        
        bool CanPerform(ActionContext context);
        bool Perform(ActionContext context);
    }

    public interface IHotbarFill
    {
        bool DisplayFill { get; }
        float Fill01 { get; }
        Color FillColor { get; }
        float FillRefresh { get; }
    }

    public interface IHotbarFillBinding
    {
        void BindFillSource(GameObject owner);
    }

    public interface IItemDurabilityProvider
    {
        bool TryGetDurability(ItemData item, out byte durability);
    }
}
