using Project.Scripts.DataTypes;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    [CustomPropertyDrawer(typeof(TreasureItemCatalogEntry))]
    public sealed class TreasureItemCatalogEntryDrawer : PropertyDrawer
    {
        private const float Spacing = 6f;
        private const float WeightWidth = 105f;

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty item = property.FindPropertyRelative("item");
            SerializedProperty weight = property.FindPropertyRelative("weight");
            Rect itemRect = new(
                position.x,
                position.y,
                Mathf.Max(0f, position.width - WeightWidth - Spacing),
                position.height);
            Rect weightRect = new(
                itemRect.xMax + Spacing,
                position.y,
                WeightWidth,
                position.height);

            EditorGUI.PropertyField(itemRect, item, GUIContent.none);
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 45f;
            EditorGUI.PropertyField(weightRect, weight, new GUIContent("Weight"));
            EditorGUIUtility.labelWidth = previousLabelWidth;
            EditorGUI.EndProperty();
        }
    }
}
