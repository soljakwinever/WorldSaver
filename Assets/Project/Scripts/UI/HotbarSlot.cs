using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.UIElements;

namespace Project.Scripts.UI
{
    [UxmlElement]
    public partial class HotbarSlot : Image
    {
        public const string UssClassName = "hotbar-slot";
        public const string LabelClassName = "hotbar-slot__count";
        public const string ReadyClassName = "hotbar-slot__fade";
        public const string ActiveClassName = "hotbar-slot__active";
        
        public int SlotId { get; set; }
        
        private readonly Label _countLabel;
        private readonly VisualElement _fillOverlay;
        private IVisualElementScheduledItem _refreshItem;


        private IHotbarAction action;
        public IHotbarAction Action
        {
            get => action;
            set
            {
                _refreshItem?.Pause();
                _refreshItem = null;
                action = value;
                if (action is IDisplayable displayable)
                {
                    _countLabel.style.display 
                        = displayable.DisplayCount 
                            ? DisplayStyle.Flex : DisplayStyle.None;

                    sprite = displayable.Sprite;
                    RefreshDisplay();

                }
                else
                {
                    _countLabel.style.display = DisplayStyle.None;
                }

                float refresh = action is IDisplayable refreshedDisplay
                    ? refreshedDisplay.Refresh
                    : 0f;
                if (action is IHotbarFill fill && fill.FillRefresh > 0f)
                    refresh = refresh > 0f
                        ? Mathf.Min(refresh, fill.FillRefresh)
                        : fill.FillRefresh;
                if (refresh > 0f)
                {
                    _refreshItem = schedule.Execute(RefreshDisplay).Every(
                        Mathf.Max(1, Mathf.RoundToInt(refresh * 1000f)));
                }

                sprite = action?.Icon;
                _countLabel.visible  = value != null;
                RefreshDisplay();
            }
        }

        private void RefreshDisplay()
        {
            if (action is IDisplayable displayable)
                _countLabel.text = displayable.Count.ToString();
            if (action is IHotbarFill fill && fill.DisplayFill)
            {
                _fillOverlay.style.display = DisplayStyle.Flex;
                _fillOverlay.style.height =
                    Length.Percent(Mathf.Clamp01(fill.Fill01) * 100f);
                _fillOverlay.style.backgroundColor = fill.FillColor;
            }
            else
            {
                _fillOverlay.style.display = DisplayStyle.None;
            }
        }

        private bool isActive;
        public bool Active
        {
            get => isActive;
            set
            {
                isActive = value;
                if(isActive)
                    AddToClassList(ActiveClassName);
                else
                    RemoveFromClassList(ActiveClassName);
            }
        }
        
        public HotbarSlot()
        {
            AddToClassList(UssClassName);
            pickingMode = PickingMode.Position;
            UniversalToolTip.Bind(this, () => action);
            
            style.borderBottomLeftRadius 
                = style.borderBottomRightRadius 
                    = style.borderTopLeftRadius 
                        = style.borderTopRightRadius = 8;
            
            style.width = 64;
            style.aspectRatio = 1;
            style.overflow = Overflow.Hidden;

            _fillOverlay = new VisualElement
            {
                name = "hotbar-slot__fill",
                pickingMode = PickingMode.Ignore
            };
            _fillOverlay.style.position = Position.Absolute;
            _fillOverlay.style.left = 0;
            _fillOverlay.style.right = 0;
            _fillOverlay.style.bottom = 0;
            _fillOverlay.style.height = 0;
            _fillOverlay.style.display = DisplayStyle.None;
            
            _countLabel = new Label()
            {
                name = LabelClassName,
                pickingMode = PickingMode.Ignore,
                text = "{0}"
            };
            _countLabel.AddToClassList(LabelClassName);
            
            
            style.flexDirection = FlexDirection.Row;
            style.marginLeft = style.marginRight = 1;
            
            hierarchy.Add(_fillOverlay);
            hierarchy.Add(_countLabel);
        }
    }
}
