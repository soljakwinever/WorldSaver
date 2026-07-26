using Project.Scripts.DataTypes;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>
    /// Draws polymorphic ItemActionData records and routes any EntityTag array
    /// declared by a derived record through the shared pill renderer.
    /// </summary>
    [CustomPropertyDrawer(typeof(ItemActionData), true)]
    public sealed class ItemActionDataDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;
        private readonly EntityTagArrayDrawer tagDrawer = new();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return height;

            SerializedProperty child = FirstChild(property);
            while (child != null)
            {
                height += Spacing + GetChildHeight(child);
                child = NextSibling(child, property);
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect header = new(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(
                header,
                property.isExpanded,
                GetHeader(property, label),
                true);

            if (property.isExpanded)
            {
                int oldIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = oldIndent + 1;
                float y = header.yMax + Spacing;

                SerializedProperty child = FirstChild(property);
                while (child != null)
                {
                    float height = GetChildHeight(child);
                    Rect childRect = new(position.x, y, position.width, height);
                    GUIContent childLabel = new(child.displayName, child.tooltip);

                    if (IsEntityTagArray(child))
                        tagDrawer.OnGUI(childRect, child, childLabel);
                    else
                        EditorGUI.PropertyField(childRect, child, childLabel, true);

                    y += height + Spacing;
                    child = NextSibling(child, property);
                }

                EditorGUI.indentLevel = oldIndent;
            }

            EditorGUI.EndProperty();
        }

        private float GetChildHeight(SerializedProperty child)
        {
            GUIContent label = new(child.displayName, child.tooltip);
            return IsEntityTagArray(child)
                ? tagDrawer.GetPropertyHeight(child, label)
                : EditorGUI.GetPropertyHeight(child, label, true);
        }

        private static bool IsEntityTagArray(SerializedProperty property)
        {
            return property.isArray &&
                   property.arrayElementType.Contains(nameof(EntityTag));
        }

        private static SerializedProperty FirstChild(SerializedProperty parent)
        {
            SerializedProperty child = parent.Copy();
            if (!child.NextVisible(true) || child.depth <= parent.depth)
                return null;
            return child;
        }

        private static SerializedProperty NextSibling(
            SerializedProperty current,
            SerializedProperty parent)
        {
            if (!current.NextVisible(false) || current.depth <= parent.depth)
                return null;
            return current;
        }

        private static GUIContent GetHeader(SerializedProperty property, GUIContent fallback)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference ||
                property.managedReferenceValue == null)
                return fallback;

            string typeName = property.managedReferenceValue.GetType().Name;
            const string suffix = "ItemActionData";
            if (typeName.EndsWith(suffix))
                typeName = typeName.Substring(0, typeName.Length - suffix.Length);

            return new GUIContent(ObjectNames.NicifyVariableName(typeName), fallback.tooltip);
        }
    }
}
