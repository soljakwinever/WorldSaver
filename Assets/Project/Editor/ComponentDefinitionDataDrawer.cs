using Project.Scripts.DataTypes;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>
    /// Draws inline component data with a compact component-specific header.
    /// </summary>
    [CustomPropertyDrawer(typeof(ComponentDefinitionData), true)]
    public sealed class ComponentDefinitionDataDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return height;

            SerializedProperty child = FirstChild(property);
            while (child != null)
            {
                height += Spacing +
                          EditorGUI.GetPropertyHeight(child, true);
                child = NextSibling(child, property);
            }

            if (TryGetValidationMessage(property, out _, out _))
            {
                height += Spacing +
                          EditorGUIUtility.singleLineHeight * 2f;
            }

            return height;
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
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
                    float height =
                        EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(
                        new Rect(position.x, y, position.width, height),
                        child,
                        true);
                    y += height + Spacing;
                    child = NextSibling(child, property);
                }

                if (TryGetValidationMessage(
                        property,
                        out string message,
                        out MessageType messageType))
                {
                    EditorGUI.HelpBox(
                        new Rect(
                            position.x,
                            y,
                            position.width,
                            EditorGUIUtility.singleLineHeight * 2f),
                        message,
                        messageType);
                }

                EditorGUI.indentLevel = oldIndent;
            }

            EditorGUI.EndProperty();
        }

        private static GUIContent GetHeader(
            SerializedProperty property,
            GUIContent fallback)
        {
            if (property.managedReferenceValue == null)
                return fallback;

            string typeName =
                property.managedReferenceValue.GetType().Name;
            const string suffix = "ComponentData";
            if (typeName.EndsWith(suffix))
                typeName = typeName.Substring(
                    0,
                    typeName.Length - suffix.Length);

            return new GUIContent(
                ObjectNames.NicifyVariableName(typeName),
                fallback.tooltip);
        }

        private static SerializedProperty FirstChild(
            SerializedProperty parent)
        {
            SerializedProperty child = parent.Copy();
            if (!child.NextVisible(true) ||
                child.depth <= parent.depth)
                return null;
            return child;
        }

        private static SerializedProperty NextSibling(
            SerializedProperty current,
            SerializedProperty parent)
        {
            if (!current.NextVisible(false) ||
                current.depth <= parent.depth)
                return null;
            return current;
        }

        private static bool TryGetValidationMessage(
            SerializedProperty property,
            out string message,
            out MessageType messageType)
        {
            if (property.managedReferenceValue is not
                ComponentDefinitionData data)
            {
                message = null;
                messageType = MessageType.None;
                return false;
            }

            if (data.ComponentDefinition == null)
            {
                message = "Assign the component definition that installs this data.";
                messageType = MessageType.Warning;
                return true;
            }

            if (!data.ComponentDefinition.DataType.IsInstanceOfType(data))
            {
                message =
                    $"{data.ComponentDefinition.name} expects " +
                    $"{data.ComponentDefinition.DataType.Name}.";
                messageType = MessageType.Error;
                return true;
            }

            message = null;
            messageType = MessageType.None;
            return false;
        }
    }
}
