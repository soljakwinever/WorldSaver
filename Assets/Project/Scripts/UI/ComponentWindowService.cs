using System;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.UI
{
    public sealed class ComponentWindowService :
        MonoBehaviour,
        IComponentWindowService
    {
        private const int WindowId = 1874301;

        private ComponentWindowRequest _request;
        private ComponentWindowContext _context;
        private Rect _windowRect;

        public bool IsOpen => _request != null;

        public void Open(ComponentWindowRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            Close();
            _request = request;
            _context = new ComponentWindowContext(Close);

            Vector2 size = request.Size;
            size.x = Mathf.Clamp(size.x, 320f, Screen.width);
            size.y = Mathf.Clamp(size.y, 220f, Screen.height);
            _windowRect = new Rect(
                (Screen.width - size.x) * .5f,
                (Screen.height - size.y) * .5f,
                size.x,
                size.y);
        }

        public void Close()
        {
            if (_request == null)
                return;

            Action closed = _request.Closed;
            _request = null;
            _context = null;
            closed?.Invoke();
        }

        private void OnGUI()
        {
            if (!IsOpen)
                return;

            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape)
            {
                Event.current.Use();
                Close();
                return;
            }

            _windowRect = GUI.Window(
                WindowId,
                _windowRect,
                DrawWindow,
                _request.Title);
            _windowRect.x = Mathf.Clamp(
                _windowRect.x, 0,
                Mathf.Max(0, Screen.width - _windowRect.width));
            _windowRect.y = Mathf.Clamp(
                _windowRect.y, 0,
                Mathf.Max(0, Screen.height - _windowRect.height));
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(64f)))
            {
                Close();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUILayout.EndHorizontal();

            ComponentWindowRequest request = _request;
            if (request != null)
            {
                for (int i = 0; i < request.Sections.Count; i++)
                {
                    request.Sections[i]?.Draw(_context);
                    if (_request == null)
                        break;
                }
            }

            if (_request != null &&
                !string.IsNullOrWhiteSpace(_context.StatusMessage))
                GUILayout.Label(_context.StatusMessage, GUI.skin.box);

            GUILayout.EndVertical();
            if (_request != null)
                GUI.DragWindow(new Rect(
                    0, 0, _windowRect.width - 72f, 24f));
        }
    }
}
