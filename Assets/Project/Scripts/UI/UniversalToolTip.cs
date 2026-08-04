using System;
using Project.Scripts.Interface;
using UnityEngine;
using UnityEngine.UIElements;

namespace Project.Scripts.UI
{
    /// <summary>A single tooltip surface shared by every bound UI element.</summary>
    public sealed class UniversalToolTip : IDisposable
    {
        private const float Offset = 14f;
        private readonly VisualElement _root;
        private readonly VisualElement _view;
        private readonly Image _icon;
        private readonly Label _name;
        private readonly Label _description;
        private readonly Label _count;

        public UniversalToolTip(VisualElement root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _view = new VisualElement { name = "UniversalToolTip", pickingMode = PickingMode.Ignore, userData = this };
            _view.AddToClassList("universal-tooltip");
            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.AddToClassList("universal-tooltip__header");
            _icon = new Image { pickingMode = PickingMode.Ignore };
            _icon.AddToClassList("universal-tooltip__icon");
            _name = new Label { pickingMode = PickingMode.Ignore };
            _name.AddToClassList("universal-tooltip__name");
            _count = new Label { pickingMode = PickingMode.Ignore };
            _count.AddToClassList("universal-tooltip__count");
            _description = new Label { pickingMode = PickingMode.Ignore };
            _description.AddToClassList("universal-tooltip__description");
            header.Add(_icon);
            header.Add(_name);
            header.Add(_count);
            _view.Add(header);
            _view.Add(_description);
            _view.style.display = DisplayStyle.None;
            _root.Add(_view);
            _view.BringToFront();
        }

        public static void Bind(VisualElement element, Func<IToolTipData> data)
        {
            if (element == null || data == null)
                return;
            element.RegisterCallback<PointerEnterEvent>(evt => Find(element)?.Show(data(), evt.position));
            element.RegisterCallback<PointerMoveEvent>(evt => Find(element)?.Move(evt.position));
            element.RegisterCallback<PointerLeaveEvent>(_ => Find(element)?.Hide());
            element.RegisterCallback<DetachFromPanelEvent>(_ => Find(element)?.Hide());
        }

        private static UniversalToolTip Find(VisualElement element)
        {
            VisualElement root = element.panel?.visualTree;
            return root?.Q("UniversalToolTip")?.userData as UniversalToolTip;
        }

        private void Show(IToolTipData data, Vector2 position)
        {
            if (data == null)
                return;
            _icon.sprite = data.Sprite;
            _icon.style.display = data.Sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
            _name.text = data.DisplayName ?? string.Empty;
            _name.style.color = data.Color;
            _description.text = data.Description ?? string.Empty;
            _description.style.display = string.IsNullOrWhiteSpace(data.Description) ? DisplayStyle.None : DisplayStyle.Flex;
            _count.text = data.Count > 0 ? $"x{data.Count}" : string.Empty;
            _count.style.display = data.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _view.style.display = DisplayStyle.Flex;
            _view.BringToFront();
            Move(position);
        }

        private void Move(Vector2 position)
        {
            if (_view.style.display == DisplayStyle.None)
                return;
            float width = _view.resolvedStyle.width;
            float height = _view.resolvedStyle.height;
            float left = Mathf.Min(position.x + Offset, Mathf.Max(0f, _root.resolvedStyle.width - width - 4f));
            float top = Mathf.Min(position.y + Offset, Mathf.Max(0f, _root.resolvedStyle.height - height - 4f));
            _view.style.left = Mathf.Max(4f, left);
            _view.style.top = Mathf.Max(4f, top);
        }

        private void Hide() => _view.style.display = DisplayStyle.None;
        public void Dispose() => _view.RemoveFromHierarchy();
    }
}
