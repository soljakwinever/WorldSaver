using Project.Scripts.DataTypes;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    [CustomPropertyDrawer(typeof(SkillActionData), true)]
    public sealed class SkillActionDataDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return height;
            SerializedProperty child = FirstChild(property);
            while (child != null)
            {
                if (ShouldDraw(property, child))
                    height += Spacing + EditorGUI.GetPropertyHeight(child, true);
                child = NextSibling(child, property);
            }
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            Rect header = new(position.x, position.y, position.width,
                EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(
                header, property.isExpanded, Header(property, label), true);
            if (property.isExpanded)
            {
                int indent = EditorGUI.indentLevel++;
                float y = header.yMax + Spacing;
                SerializedProperty child = FirstChild(property);
                while (child != null)
                {
                    if (!ShouldDraw(property, child))
                    {
                        child = NextSibling(child, property);
                        continue;
                    }
                    float height = EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(
                        new Rect(position.x, y, position.width, height), child, true);
                    y += height + Spacing;
                    child = NextSibling(child, property);
                }
                EditorGUI.indentLevel = indent;
            }
            EditorGUI.EndProperty();
        }

        private static SerializedProperty FirstChild(SerializedProperty parent)
        {
            SerializedProperty child = parent.Copy();
            return child.NextVisible(true) && child.depth > parent.depth ? child : null;
        }

        private static SerializedProperty NextSibling(
            SerializedProperty current, SerializedProperty parent) =>
            current.NextVisible(false) && current.depth > parent.depth ? current : null;

        private static bool ShouldDraw(
            SerializedProperty parent,
            SerializedProperty child)
        {
            if (child.name != nameof(SkillActionData.finishEruptions))
                return true;
            SerializedProperty enabled = parent.FindPropertyRelative(
                nameof(SkillActionData.enableFinishEruptions));
            return enabled?.boolValue == true;
        }

        private static GUIContent Header(SerializedProperty property, GUIContent fallback)
        {
            if (property.managedReferenceValue == null) return fallback;
            string name = property.managedReferenceValue.GetType().Name;
            const string suffix = "SkillActionData";
            if (name.EndsWith(suffix)) name = name[..^suffix.Length];
            return new GUIContent(ObjectNames.NicifyVariableName(name), fallback.tooltip);
        }
    }
}
