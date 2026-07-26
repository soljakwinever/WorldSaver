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
                    tooltip = displayable.Label;
                    RefreshDisplay();

                    if (displayable.Refresh > 0f)
                    {
                        long refreshMilliseconds =
                            Mathf.Max(
                                1,
                                Mathf.RoundToInt(displayable.Refresh * 1000f));
                        _refreshItem = schedule
                            .Execute(RefreshDisplay)
                            .Every(refreshMilliseconds);
                    }
                }
                else
                {
                    _countLabel.style.display = DisplayStyle.None;
                    tooltip = action?.Tooltip;
                }

                sprite = action?.Icon;
                _countLabel.visible  = value != null;
            }
        }

        private void RefreshDisplay()
        {
            if (action is IDisplayable displayable)
                _countLabel.text = displayable.Count.ToString();
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
            
            style.borderBottomLeftRadius 
                = style.borderBottomRightRadius 
                    = style.borderTopLeftRadius 
                        = style.borderTopRightRadius = 8;
            
            style.width = 64;
            style.aspectRatio = 1;
            
            _countLabel = new Label()
            {
                name = LabelClassName,
                pickingMode = PickingMode.Ignore,
                text = "{0}"
            };
            _countLabel.AddToClassList(LabelClassName);
            
            
            style.flexDirection = FlexDirection.Row;
            style.marginLeft = style.marginRight = 1;
            
            hierarchy.Add(_countLabel);
        }
    }
}
