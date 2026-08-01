using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Project.Scripts.DataTypes;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Project.Editor
{
    public sealed class ValueTagArrayDrawer : PropertyDrawer
    {
        private const float RowHeight = 20f;
        private const float Spacing = 3f;
        private const float AddWidth = 24f;
        private const float RemoveWidth = 18f;
        private const string DefaultTagFolder = "Assets/Project/Data/Tags";

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            Mathf.Max(EditorGUIUtility.singleLineHeight,
                property.arraySize * (RowHeight + Spacing) + RowHeight);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            Rect labelRect = new(position.x, position.y, EditorGUIUtility.labelWidth,
                EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, label);

            float x = position.x + EditorGUIUtility.labelWidth;
            float width = position.xMax - x;
            float y = position.y;
            int removeIndex = -1;

            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty assignment = property.GetArrayElementAtIndex(i);
                SerializedProperty tagProperty = assignment.FindPropertyRelative("tag");
                SerializedProperty intProperty = assignment.FindPropertyRelative("intValue");
                SerializedProperty floatProperty = assignment.FindPropertyRelative("floatValue");
                ValueTag tag = tagProperty.objectReferenceValue as ValueTag;

                Rect row = new(x, y, width, RowHeight);
                Rect remove = new(row.xMax - RemoveWidth, row.y, RemoveWidth, RowHeight);
                Rect value = new(remove.x - 78f, row.y, 74f, RowHeight);
                Rect pill = new(row.x, row.y, Mathf.Max(40f, value.x - row.x - 4f), RowHeight);

                Color color = tag != null ? ColorForName(tag.name) : new Color(0.55f, 0.2f, 0.2f);
                EditorGUI.DrawRect(pill, color);
                if (GUI.Button(pill, tag != null ? tag.name : "Missing", EditorStyles.miniBoldLabel) && tag != null)
                {
                    Selection.activeObject = tag;
                    EditorGUIUtility.PingObject(tag);
                }

                if (tag == null || tag.ValueType == ValueTag.NumberType.Integer)
                    EditorGUI.PropertyField(value, intProperty, GUIContent.none);
                else
                    EditorGUI.PropertyField(value, floatProperty, GUIContent.none);

                if (GUI.Button(remove, EditorGUIUtility.IconContent("Toolbar Minus"), GUIStyle.none))
                    removeIndex = i;
                y += RowHeight + Spacing;
            }

            Rect add = new(x, y, AddWidth, RowHeight);
            if (GUI.Button(add, EditorGUIUtility.IconContent("Toolbar Plus"), EditorStyles.miniButton))
            {
                new ValueTagDropdown(new AdvancedDropdownState(), property.serializedObject,
                    property.propertyPath).Show(add);
            }

            if (removeIndex >= 0)
            {
                property.DeleteArrayElementAtIndex(removeIndex);
                property.serializedObject.ApplyModifiedProperties();
            }
            EditorGUI.EndProperty();
        }

        private static Color ColorForName(string name)
        {
            unchecked
            {
                uint hash = (uint)name.GetHashCode();
                return Color.HSVToRGB((hash % 1000u) / 1000f, 0.58f,
                    EditorGUIUtility.isProSkin ? 0.58f : 0.72f);
            }
        }

        private sealed class ValueTagDropdown : AdvancedDropdown
        {
            private readonly UnityEngine.Object[] targets;
            private readonly string propertyPath;
            private readonly HashSet<ValueTag> assigned;
            private readonly Dictionary<AdvancedDropdownItem, ValueTag> tags = new();
            private AdvancedDropdownItem createInteger;
            private AdvancedDropdownItem createFloat;

            public ValueTagDropdown(AdvancedDropdownState state,
                SerializedObject serializedObject, string propertyPath) : base(state)
            {
                targets = serializedObject.targetObjects;
                this.propertyPath = propertyPath;
                assigned = ReadAssigned(serializedObject.FindProperty(propertyPath));
                minimumSize = new Vector2(280f, 320f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                AdvancedDropdownItem root = new("Value Tags");
                createInteger = new AdvancedDropdownItem("Create New/Integer Value Tag...");
                createFloat = new AdvancedDropdownItem("Create New/Float Value Tag...");
                root.AddChild(createInteger);
                root.AddChild(createFloat);

                foreach (ValueTag tag in AssetDatabase.FindAssets("t:ValueTag")
                             .Select(AssetDatabase.GUIDToAssetPath)
                             .Select(AssetDatabase.LoadAssetAtPath<ValueTag>)
                             .Where(tag => tag != null && !assigned.Contains(tag))
                             .OrderBy(tag => tag.name, StringComparer.OrdinalIgnoreCase))
                {
                    AdvancedDropdownItem item = new(tag.name);
                    tags[item] = tag;
                    root.AddChild(item);
                }
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (ReferenceEquals(item, createInteger) || ReferenceEquals(item, createFloat))
                {
                    ValueTag.NumberType type = ReferenceEquals(item, createInteger)
                        ? ValueTag.NumberType.Integer
                        : ValueTag.NumberType.Float;
                    EditorApplication.delayCall += () => CreateAndAssign(type);
                }
                else if (tags.TryGetValue(item, out ValueTag tag))
                    Assign(tag);
            }

            private void CreateAndAssign(ValueTag.NumberType type)
            {
                EnsureFolder();
                string path = EditorUtility.SaveFilePanelInProject("Create Value Tag",
                    "New Value Tag", "asset", "Choose a location for the new ValueTag.",
                    DefaultTagFolder);
                if (string.IsNullOrEmpty(path))
                    return;
                ValueTag tag = ScriptableObject.CreateInstance<ValueTag>();
                tag.name = Path.GetFileNameWithoutExtension(path);
                tag.Configure(ToPersistentId(tag.name), type);
                AssetDatabase.CreateAsset(tag, path);
                AssetDatabase.SaveAssets();
                ProjectWindowUtil.ShowCreatedAsset(tag);
                Assign(tag);
            }

            private void Assign(ValueTag tag)
            {
                foreach (UnityEngine.Object target in targets)
                {
                    SerializedObject serialized = new(target);
                    serialized.Update();
                    SerializedProperty array = serialized.FindProperty(propertyPath);
                    if (array == null || !array.isArray)
                        continue;
                    int index = array.arraySize++;
                    SerializedProperty assignment = array.GetArrayElementAtIndex(index);
                    assignment.FindPropertyRelative("tag").objectReferenceValue = tag;
                    serialized.ApplyModifiedProperties();
                }
            }

            private static HashSet<ValueTag> ReadAssigned(SerializedProperty array)
            {
                HashSet<ValueTag> result = new();
                for (int i = 0; i < (array?.arraySize ?? 0); i++)
                {
                    ValueTag tag = array.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("tag").objectReferenceValue as ValueTag;
                    if (tag != null)
                        result.Add(tag);
                }
                return result;
            }

            private static string ToPersistentId(string value) =>
                string.Join("-", value.Trim().ToLowerInvariant()
                    .Split(new[] { ' ', '_' }, StringSplitOptions.RemoveEmptyEntries));

            private static void EnsureFolder()
            {
                string current = "Assets";
                foreach (string segment in DefaultTagFolder.Substring(7).Split('/'))
                {
                    string child = $"{current}/{segment}";
                    if (!AssetDatabase.IsValidFolder(child))
                        AssetDatabase.CreateFolder(current, segment);
                    current = child;
                }
            }
        }
    }
}
