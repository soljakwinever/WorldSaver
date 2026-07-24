using UnityEngine.UIElements;

namespace Project.Scripts.UI
{
    [UxmlElement]
    public partial class HotbarSlot : VisualElement
    {
        public int SlotId { get; set; }
    }
}