using System.Collections.Generic;
using UnityEngine;

namespace Project.Scripts
{
    /// <summary>
    /// Builds one CompositeCollider2D from every child SpriteRenderer sprite
    /// physics shape. Generated source colliders are kept on hidden children so
    /// existing colliders on the visual hierarchy are never modified.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpritePhysicsShapeCompositeCollider : MonoBehaviour
    {
        [SerializeField] private bool includeInactiveRenderers = true;
        [SerializeField] private bool isTrigger;

        private const string GeneratedName =
            "__Generated Sprite Physics Collider";

        [ContextMenu("Rebuild Composite Collider")]
        public void Rebuild()
        {
            EnsureComposite(out CompositeCollider2D composite);
            ClearGeneratedSources();

            SpriteRenderer[] renderers =
                GetComponentsInChildren<SpriteRenderer>(
                    includeInactiveRenderers);
            foreach (SpriteRenderer renderer in renderers)
                AddRendererShapes(renderer);

            composite.isTrigger = isTrigger;
            composite.GenerateGeometry();
        }

        [ContextMenu("Clear Generated Collider Sources")]
        public void ClearGeneratedSources()
        {
            Transform[] descendants =
                GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (Transform source in descendants)
            {
                if (source == null ||
                    source == transform ||
                    source.name != GeneratedName)
                {
                    continue;
                }

                source.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(source.gameObject);
                else
                    DestroyImmediate(source.gameObject);
            }
        }

        private void OnEnable() => Rebuild();

        private void OnDestroy()
        {
            ClearGeneratedSources();
        }

        private void EnsureComposite(out CompositeCollider2D composite)
        {
            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;
            }

            composite = GetComponent<CompositeCollider2D>();
            if (composite == null)
                composite = gameObject.AddComponent<CompositeCollider2D>();

            composite.geometryType =
                CompositeCollider2D.GeometryType.Polygons;
            composite.generationType =
                CompositeCollider2D.GenerationType.Synchronous;
        }

        private void AddRendererShapes(SpriteRenderer renderer)
        {
            Sprite sprite = renderer != null ? renderer.sprite : null;
            if (sprite == null)
                return;

            int shapeCount = sprite.GetPhysicsShapeCount();
            if (shapeCount == 0)
                return;

            GameObject sourceObject = new(GeneratedName);
            sourceObject.hideFlags = HideFlags.HideInHierarchy;
            sourceObject.layer = renderer.gameObject.layer;
            sourceObject.transform.SetParent(renderer.transform, false);

            PolygonCollider2D polygon =
                sourceObject.AddComponent<PolygonCollider2D>();
            polygon.pathCount = shapeCount;
            polygon.compositeOperation =
                Collider2D.CompositeOperation.Merge;

            List<Vector2> points = new();
            for (int shapeIndex = 0;
                 shapeIndex < shapeCount;
                 shapeIndex++)
            {
                points.Clear();
                sprite.GetPhysicsShape(shapeIndex, points);

                if (renderer.flipX || renderer.flipY)
                {
                    for (int pointIndex = 0;
                         pointIndex < points.Count;
                         pointIndex++)
                    {
                        Vector2 point = points[pointIndex];
                        if (renderer.flipX)
                            point.x = -point.x;
                        if (renderer.flipY)
                            point.y = -point.y;
                        points[pointIndex] = point;
                    }
                }

                polygon.SetPath(shapeIndex, points);
            }
        }
    }
}
