using System;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class WallDamageVisual : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite[] damageSprites = Array.Empty<Sprite>();
        private SpriteMask _entityMask;

        public int TileHealth { get; private set; }
        public int MaximumHealth { get; private set; }
        public int SpriteIndex { get; private set; } = -1;

        public void SetHealth(int tileHealth, int maximumHealth)
        {
            TileHealth = tileHealth;
            MaximumHealth = maximumHealth;
            spriteRenderer ??= GetComponent<SpriteRenderer>();
            SpriteIndex = CalculateSpriteIndex(
                tileHealth,
                maximumHealth,
                damageSprites?.Length ?? 0);
            spriteRenderer.sprite =
                SpriteIndex >= 0 ? damageSprites[SpriteIndex] : null;
        }

        public static int CalculateSpriteIndex(
            int tileHealth,
            int maximumHealth,
            int spriteCount)
        {
            if (spriteCount <= 0 ||
                maximumHealth == 0 ||
                tileHealth >= maximumHealth)
            {
                return -1;
            }

            int missingHealth = maximumHealth - tileHealth;
            // Divide the damaged portion into even, one-based stages:
            // damage 1..20 uses sprite 0, 21..40 uses sprite 1, etc.
            int index =
                (missingHealth - 1) * spriteCount / maximumHealth;
            return Mathf.Clamp(index, 0, spriteCount - 1);
        }

        public void ConfigureEntityMask(SpriteRenderer entityRenderer)
        {
            spriteRenderer ??= GetComponent<SpriteRenderer>();
            if (entityRenderer == null || entityRenderer.sprite == null)
            {
                ClearEntityMask();
                return;
            }

            _entityMask ??= GetComponent<SpriteMask>();
            if (_entityMask == null)
                _entityMask = gameObject.AddComponent<SpriteMask>();

            _entityMask.enabled = true;
            _entityMask.sprite = entityRenderer.sprite;
            _entityMask.alphaCutoff = 0.05f;
            _entityMask.isCustomRangeActive = true;
            _entityMask.frontSortingLayerID =
                entityRenderer.sortingLayerID;
            _entityMask.backSortingLayerID =
                entityRenderer.sortingLayerID;
            _entityMask.frontSortingOrder =
                entityRenderer.sortingOrder + 2;
            _entityMask.backSortingOrder =
                entityRenderer.sortingOrder;

            spriteRenderer.sortingLayerID =
                entityRenderer.sortingLayerID;
            spriteRenderer.sortingOrder =
                entityRenderer.sortingOrder + 1;
            spriteRenderer.maskInteraction =
                SpriteMaskInteraction.VisibleInsideMask;
            spriteRenderer.flipX = entityRenderer.flipX;
            spriteRenderer.flipY = entityRenderer.flipY;

            transform.SetParent(entityRenderer.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        private void OnValidate()
        {
            spriteRenderer ??= GetComponent<SpriteRenderer>();
        }
        public void Clear()
        {
            TileHealth = default;
            MaximumHealth = default;
            SpriteIndex = -1;
            spriteRenderer ??= GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = null;
            ClearEntityMask();
        }

        private void ClearEntityMask()
        {
            spriteRenderer ??= GetComponent<SpriteRenderer>();
            spriteRenderer.maskInteraction = SpriteMaskInteraction.None;
            if (_entityMask != null)
            {
                _entityMask.sprite = null;
                _entityMask.enabled = false;
            }
        }
    }
}
