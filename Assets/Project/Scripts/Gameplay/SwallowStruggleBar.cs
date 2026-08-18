using Project.Scripts.DataTypes;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class SwallowStruggleBar : MonoBehaviour
    {
        private static Sprite _whiteSprite;

        private SwallowedStateController _source;
        private Transform _root;
        private SpriteRenderer _fill;
        private float _width;
        private float _height;
        private Vector2 _offset;

        public bool IsBound => _source != null && _root != null;

        public void Bind(
            SwallowedStateController source,
            SwallowSkillActionData settings)
        {
            Unbind();
            if (source == null || settings == null)
                return;

            _source = source;
            _source.StruggleChanged += OnStruggleChanged;
            _width = Mathf.Max(0.1f, settings.struggleBarWidth);
            _height = Mathf.Max(0.02f, settings.struggleBarHeight);
            _offset = settings.struggleBarOffset;

            var rootObject = new GameObject("Struggle Progress Bar");
            _root = rootObject.transform;
            _root.SetParent(transform, false);

            int sortingLayer = 0;
            int sortingOrder = 0;
            foreach (SpriteRenderer renderer in
                     GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.transform.IsChildOf(_root))
                    continue;
                sortingLayer = renderer.sortingLayerID;
                sortingOrder = Mathf.Max(sortingOrder, renderer.sortingOrder);
            }

            CreatePart(
                "Border",
                new Vector2(_width + 0.08f, _height + 0.08f),
                Color.black,
                sortingLayer,
                sortingOrder + 48);
            CreatePart(
                "Background",
                new Vector2(_width, _height),
                settings.struggleBarBackground,
                sortingLayer,
                sortingOrder + 49);
            _fill = CreatePart(
                "Fill",
                new Vector2(_width, _height),
                settings.struggleBarFill,
                sortingLayer,
                sortingOrder + 50);
            UpdateFill(_source.Struggle01);
            UpdateTransform();
        }

        public void Unbind()
        {
            if (_source != null)
                _source.StruggleChanged -= OnStruggleChanged;
            _source = null;
            _fill = null;
            if (_root != null)
            {
                _root.gameObject.SetActive(false);
                Destroy(_root.gameObject);
            }
            _root = null;
        }

        private void LateUpdate()
        {
            if (_source == null || !_source.IsSwallowed || _root == null)
            {
                if (_root != null)
                    Unbind();
                return;
            }
            UpdateTransform();
        }

        private void OnStruggleChanged(float value) => UpdateFill(value);

        private SpriteRenderer CreatePart(
            string objectName,
            Vector2 size,
            Color color,
            int sortingLayer,
            int sortingOrder)
        {
            var part = new GameObject(objectName);
            part.transform.SetParent(_root, false);
            part.AddComponent<FalseHeightVisual>();
            SpriteRenderer renderer = part.AddComponent<SpriteRenderer>();
            renderer.sprite = WhiteSprite;
            renderer.color = color;
            renderer.sortingLayerID = sortingLayer;
            renderer.sortingOrder = sortingOrder;
            part.transform.localScale = new Vector3(size.x, size.y, 1f);
            return renderer;
        }

        private void UpdateFill(float progress)
        {
            if (_fill == null)
                return;
            float value = Mathf.Clamp01(progress);
            float fillWidth = _width * value;
            Transform fillTransform = _fill.transform;
            fillTransform.localScale = new Vector3(
                fillWidth,
                _height,
                1f);
            fillTransform.localPosition = new Vector3(
                -_width * 0.5f + fillWidth * 0.5f,
                0f,
                0f);
            _fill.enabled = value > 0.0001f;
        }

        private void UpdateTransform()
        {
            if (_root == null)
                return;
            _root.position = transform.position + (Vector3)_offset;
            _root.rotation = Quaternion.identity;
            Vector3 scale = transform.lossyScale;
            _root.localScale = new Vector3(
                Divide(1f, scale.x),
                Divide(1f, scale.y),
                Divide(1f, scale.z));
        }

        private static float Divide(float value, float divisor) =>
            Mathf.Approximately(divisor, 0f) ? value : value / divisor;

        private static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite != null)
                    return _whiteSprite;
                Texture2D texture = Texture2D.whiteTexture;
                _whiteSprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    texture.width);
                _whiteSprite.name = "Runtime White Bar Sprite";
                return _whiteSprite;
            }
        }

        private void OnDestroy()
        {
            if (_root != null)
                Destroy(_root.gameObject);
        }
    }
}
