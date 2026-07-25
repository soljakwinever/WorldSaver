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
    [CustomPropertyDrawer(typeof(EntityTag[]))]
    public sealed class EntityTagArrayDrawer : PropertyDrawer
    {
        private const float PillHeight = 20f;
        private const float PillSpacing = 4f;
        private const float RemoveWidth = 16f;
        private const float AddWidth = 24f;
        private const string DefaultTagFolder = "Assets/Project/Data/Tags";

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float availableWidth = Mathf.Max(
                100f,
                EditorGUIUtility.currentViewWidth - EditorGUIUtility.labelWidth - 42f);

            int rows = CountRows(property, availableWidth);
            return Mathf.Max(EditorGUIUtility.singleLineHeight, rows * (PillHeight + PillSpacing) - PillSpacing);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!property.isArray)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            Rect labelRect = new(position.x, position.y, EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, label);

            Rect contentRect = new(
                position.x + EditorGUIUtility.labelWidth,
                position.y,
                position.width - EditorGUIUtility.labelWidth,
                position.height);

            DrawPills(contentRect, property);
            EditorGUI.EndProperty();
        }

        private static void DrawPills(Rect area, SerializedProperty property)
        {
            float x = area.x;
            float y = area.y;
            int removeIndex = -1;

            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                EntityTag tag = element.objectReferenceValue as EntityTag;
                string tagName = tag != null ? tag.name : "Missing";
                float width = GetPillWidth(tagName);

                WrapIfNeeded(area, width, ref x, ref y);
                Rect pillRect = new(x, y, width, PillHeight);
                DrawPill(pillRect, tagName, tag);

                Rect removeRect = new(
                    pillRect.xMax - RemoveWidth - 2f,
                    pillRect.y + 2f,
                    RemoveWidth,
                    PillHeight - 4f);

                if (GUI.Button(removeRect, EditorGUIUtility.IconContent("Toolbar Minus"), GUIStyle.none))
                    removeIndex = i;

                x += width + PillSpacing;
            }

            WrapIfNeeded(area, AddWidth, ref x, ref y);
            Rect addRect = new(x, y, AddWidth, PillHeight);
            if (GUI.Button(addRect, EditorGUIUtility.IconContent("Toolbar Plus"), EditorStyles.miniButton))
            {
                EntityTagDropdown dropdown = new(
                    new AdvancedDropdownState(),
                    property.serializedObject,
                    property.propertyPath);
                dropdown.Show(addRect);
            }

            if (removeIndex >= 0)
            {
                property.DeleteArrayElementAtIndex(removeIndex);
                // Object-reference arrays need a second deletion after their value is nulled.
                if (removeIndex < property.arraySize &&
                    property.GetArrayElementAtIndex(removeIndex).objectReferenceValue == null)
                    property.DeleteArrayElementAtIndex(removeIndex);
                property.serializedObject.ApplyModifiedProperties();
            }
        }

        private static void DrawPill(Rect rect, string tagName, EntityTag tag)
        {
            Color background = tag != null ? ColorForName(tagName) : new Color(0.55f, 0.2f, 0.2f);
            EditorGUI.DrawRect(rect, background);

            Color oldColor = GUI.color;
            GUI.color = Color.white;
            Rect textRect = new(rect.x + 7f, rect.y, rect.width - RemoveWidth - 10f, rect.height);
            GUI.Label(textRect, tagName, EditorStyles.miniBoldLabel);
            GUI.color = oldColor;
        }

        private static Color ColorForName(string name)
        {
            unchecked
            {
                uint hash = (uint)name.GetHashCode();
                float hue = (hash % 1000u) / 1000f;
                float saturation = 0.48f + ((hash >> 10) & 0xffu) / 255f * 0.17f;
                float value = EditorGUIUtility.isProSkin ? 0.58f : 0.72f;
                return Color.HSVToRGB(hue, saturation, value);
            }
        }

        private static int CountRows(SerializedProperty property, float availableWidth)
        {
            int rows = 1;
            float used = 0f;

            for (int i = 0; i < property.arraySize; i++)
            {
                EntityTag tag = property.GetArrayElementAtIndex(i).objectReferenceValue as EntityTag;
                float width = GetPillWidth(tag != null ? tag.name : "Missing");
                AddWidthToRows(width, availableWidth, ref used, ref rows);
            }

            AddWidthToRows(AddWidth, availableWidth, ref used, ref rows);
            return rows;
        }

        private static void AddWidthToRows(float width, float availableWidth, ref float used, ref int rows)
        {
            if (used > 0f && used + width > availableWidth)
            {
                rows++;
                used = 0f;
            }

            used += width + PillSpacing;
        }

        private static float GetPillWidth(string text)
        {
            return Mathf.Clamp(EditorStyles.miniBoldLabel.CalcSize(new GUIContent(text)).x + RemoveWidth + 15f, 50f, 180f);
        }

        private static void WrapIfNeeded(Rect area, float width, ref float x, ref float y)
        {
            if (x > area.x && x + width > area.xMax)
            {
                x = area.x;
                y += PillHeight + PillSpacing;
            }
        }

        private sealed class EntityTagDropdown : AdvancedDropdown
        {
            private readonly SerializedObject serializedObject;
            private readonly string propertyPath;
            private readonly HashSet<EntityTag> assignedTags;
            private readonly Dictionary<int, EntityTag> tagsById = new();
            private int nextId = 1;

            public EntityTagDropdown(
                AdvancedDropdownState state,
                SerializedObject serializedObject,
                string propertyPath) : base(state)
            {
                this.serializedObject = serializedObject;
                this.propertyPath = propertyPath;
                minimumSize = new Vector2(260f, 320f);
                assignedTags = ReadAssignedTags(serializedObject.FindProperty(propertyPath));
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                AdvancedDropdownItem root = new("Entity Tags");
                root.AddChild(new AdvancedDropdownItem("Create New Entity Tag...") { id = 0 });

                string[] guids = AssetDatabase.FindAssets("t:EntityTag");
                IEnumerable<EntityTag> tags = guids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<EntityTag>)
                    .Where(tag => tag != null && !assignedTags.Contains(tag))
                    .OrderBy(tag => tag.name, StringComparer.OrdinalIgnoreCase);

                foreach (EntityTag tag in tags)
                {
                    int id = nextId++;
                    tagsById[id] = tag;
                    root.AddChild(new AdvancedDropdownItem(tag.name) { id = id });
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item.id == 0)
                {
                    CreateTagAndAssign();
                    return;
                }

                if (tagsById.TryGetValue(item.id, out EntityTag tag))
                    Assign(tag);
            }

            private void CreateTagAndAssign()
            {
                EnsureDefaultFolderExists();
                string path = EditorUtility.SaveFilePanelInProject(
                    "Create Entity Tag",
                    "New Entity Tag",
                    "asset",
                    "Choose a name and location for the new EntityTag.",
                    DefaultTagFolder);

                if (string.IsNullOrEmpty(path))
                    return;

                EntityTag tag = ScriptableObject.CreateInstance<EntityTag>();
                tag.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(tag, path);
                AssetDatabase.SaveAssets();
                ProjectWindowUtil.ShowCreatedAsset(tag);
                Assign(tag);
            }

            private void Assign(EntityTag tag)
            {
                serializedObject.Update();
                SerializedProperty array = serializedObject.FindProperty(propertyPath);
                if (array == null || !array.isArray)
                    return;

                int index = array.arraySize;
                array.InsertArrayElementAtIndex(index);
                array.GetArrayElementAtIndex(index).objectReferenceValue = tag;
                serializedObject.ApplyModifiedProperties();
            }

            private static HashSet<EntityTag> ReadAssignedTags(SerializedProperty array)
            {
                HashSet<EntityTag> result = new();
                if (array == null || !array.isArray)
                    return result;

                for (int i = 0; i < array.arraySize; i++)
                {
                    if (array.GetArrayElementAtIndex(i).objectReferenceValue is EntityTag tag)
                        result.Add(tag);
                }

                return result;
            }

            private static void EnsureDefaultFolderExists()
            {
                string current = "Assets";
                foreach (string segment in DefaultTagFolder.Substring("Assets/".Length).Split('/'))
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
