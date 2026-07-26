using System;
using System.Collections.Generic;
using System.Linq;
using Project.Scripts.DataTypes;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>
    /// Draws SerializeReference arrays/lists with a menu of concrete descendants.
    /// The collection UI is drawn directly so Unity's default null-adding button
    /// is never present.
    /// </summary>
    [CustomPropertyDrawer(typeof(ManagedReferenceSelectorAttribute))]
    public sealed class ManagedReferenceSelectorDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;
        private const float RemoveButtonWidth = 22f;
        private readonly ItemActionDataDrawer itemActionDataDrawer = new();

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            if (!property.isArray)
                return GetElementHeight(property, label);

            return GetPropertyHeight(
                property,
                label,
                ((ManagedReferenceSelectorAttribute)attribute).BaseType);
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            if (!property.isArray)
            {
                DrawElement(position, property, label);
                return;
            }

            OnGUI(
                position,
                property,
                label,
                ((ManagedReferenceSelectorAttribute)attribute).BaseType);
        }

        /// <summary>
        /// Collection-level entry point for custom inspectors. Unity can apply
        /// array field attributes to their elements instead of the array itself.
        /// </summary>
        public float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label,
            Type baseType)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return height;

            for (int i = 0; i < property.arraySize; i++)
            {
                height += Spacing + EditorGUI.GetPropertyHeight(
                    property.GetArrayElementAtIndex(i),
                    true);
            }

            return height + Spacing + EditorGUIUtility.singleLineHeight;
        }

        /// <summary>Draws a managed-reference collection from a custom inspector.</summary>
        public void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            Type baseType)
        {
            if (!property.isArray)
            {
                EditorGUI.HelpBox(
                    position,
                    $"{nameof(ManagedReferenceSelectorAttribute)} requires an array or List field.",
                    MessageType.Error);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            Rect headerRect = new(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(
                headerRect,
                property.isExpanded,
                $"{label.text} ({property.arraySize})",
                true);

            if (property.isExpanded)
                DrawExpanded(position, property, baseType, headerRect.yMax + Spacing);

            EditorGUI.EndProperty();
        }

        private static void DrawExpanded(
            Rect position,
            SerializedProperty property,
            Type baseType,
            float y)
        {
            int removeIndex = -1;
            int oldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = oldIndent + 1;

            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                float elementHeight = EditorGUI.GetPropertyHeight(element, true);
                Rect elementRect = new(
                    position.x,
                    y,
                    position.width - RemoveButtonWidth - Spacing,
                    elementHeight);
                Rect removeRect = new(
                    elementRect.xMax + Spacing,
                    y,
                    RemoveButtonWidth,
                    EditorGUIUtility.singleLineHeight);

                EditorGUI.PropertyField(elementRect, element, GUIContent.none, true);
                if (GUI.Button(removeRect, EditorGUIUtility.IconContent("Toolbar Minus")))
                    removeIndex = i;

                y += elementHeight + Spacing;
            }

            EditorGUI.indentLevel = oldIndent;

            Rect addRect = new(
                position.x,
                y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            if (EditorGUI.DropdownButton(
                    addRect,
                    new GUIContent($"Add {ObjectNames.NicifyVariableName(baseType.Name)}"),
                    FocusType.Keyboard))
            {
                ShowTypeMenu(property, baseType, addRect);
            }

            if (removeIndex >= 0)
                Remove(property, removeIndex);
        }

        private static void ShowTypeMenu(
            SerializedProperty property,
            Type baseType,
            Rect buttonRect)
        {
            UnityEngine.Object[] targets =
                property.serializedObject.targetObjects;
            string propertyPath = property.propertyPath;
            GenericMenu menu = new();
            List<Type> types = TypeCache.GetTypesDerivedFrom(baseType)
                .Where(IsConstructible)
                .OrderBy(GetMenuName)
                .ToList();

            if (types.Count == 0)
            {
                menu.AddDisabledItem(
                    new GUIContent($"No concrete {baseType.Name} types found"));
            }
            else
            {
                foreach (Type type in types)
                {
                    Type selectedType = type;
                    menu.AddItem(
                        new GUIContent(GetMenuName(type)),
                        false,
                        () => Append(
                            targets,
                            propertyPath,
                            selectedType));
                }
            }

            menu.DropDown(buttonRect);
        }

        private static bool IsConstructible(Type type)
        {
            return !type.IsAbstract &&
                   !type.IsGenericTypeDefinition &&
                   type.IsSerializable &&
                   type.GetConstructor(Type.EmptyTypes) != null;
        }

        private static string GetMenuName(Type type)
        {
            return ObjectNames.NicifyVariableName(type.Name);
        }

        private static void Append(
            UnityEngine.Object[] targets,
            string propertyPath,
            Type type)
        {
            SerializedObject serializedObject = new(targets);
            serializedObject.Update();
            SerializedProperty collection =
                serializedObject.FindProperty(propertyPath);
            if (collection == null || !collection.isArray)
                return;

            int index = collection.arraySize;
            collection.InsertArrayElementAtIndex(index);
            collection.GetArrayElementAtIndex(index).managedReferenceValue =
                Activator.CreateInstance(type);
            serializedObject.ApplyModifiedProperties();
        }

        private static void Remove(SerializedProperty property, int index)
        {
            property.serializedObject.Update();
            int oldSize = property.arraySize;
            property.DeleteArrayElementAtIndex(index);
            if (property.arraySize == oldSize)
                property.DeleteArrayElementAtIndex(index);
            property.serializedObject.ApplyModifiedProperties();
        }

        private float GetElementHeight(
            SerializedProperty property,
            GUIContent label)
        {
            Type baseType =
                ((ManagedReferenceSelectorAttribute)attribute).BaseType;
            if (typeof(ItemActionData).IsAssignableFrom(baseType))
                return itemActionDataDrawer.GetPropertyHeight(property, label);

            return GetGenericElementHeight(property);
        }

        private void DrawElement(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            Type baseType =
                ((ManagedReferenceSelectorAttribute)attribute).BaseType;
            if (typeof(ItemActionData).IsAssignableFrom(baseType))
            {
                itemActionDataDrawer.OnGUI(position, property, label);
                return;
            }

            DrawGenericElement(position, property, label, baseType);
        }

        private static float GetGenericElementHeight(SerializedProperty property)
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

            return height;
        }

        private static void DrawGenericElement(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            Type baseType)
        {
            Rect header = new(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            const float pickerWidth = 140f;
            Rect foldoutRect = new(
                header.x,
                header.y,
                Mathf.Max(0f, header.width - pickerWidth - Spacing),
                header.height);
            Rect pickerRect = new(
                foldoutRect.xMax + Spacing,
                header.y,
                pickerWidth,
                header.height);

            property.isExpanded = EditorGUI.Foldout(
                foldoutRect,
                property.isExpanded,
                GetElementLabel(property, label),
                true);

            string pickerLabel = property.managedReferenceValue == null
                ? $"Select {ObjectNames.NicifyVariableName(baseType.Name)}"
                : ObjectNames.NicifyVariableName(
                    property.managedReferenceValue.GetType().Name);
            if (EditorGUI.DropdownButton(
                    pickerRect,
                    new GUIContent(pickerLabel),
                    FocusType.Keyboard))
            {
                ShowSingleTypeMenu(property, baseType, pickerRect);
            }

            if (!property.isExpanded)
                return;

            int oldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = oldIndent + 1;
            float y = header.yMax + Spacing;
            SerializedProperty child = FirstChild(property);
            while (child != null)
            {
                float height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(
                    new Rect(position.x, y, position.width, height),
                    child,
                    true);
                y += height + Spacing;
                child = NextSibling(child, property);
            }

            EditorGUI.indentLevel = oldIndent;
        }

        private static void ShowSingleTypeMenu(
            SerializedProperty property,
            Type baseType,
            Rect buttonRect)
        {
            UnityEngine.Object[] targets = property.serializedObject.targetObjects;
            string propertyPath = property.propertyPath;
            GenericMenu menu = new();

            menu.AddItem(
                new GUIContent("None"),
                property.managedReferenceValue == null,
                () => Assign(targets, propertyPath, null));
            menu.AddSeparator(string.Empty);

            foreach (Type type in TypeCache.GetTypesDerivedFrom(baseType)
                         .Where(IsConstructible)
                         .OrderBy(GetMenuName))
            {
                Type selectedType = type;
                bool selected = property.managedReferenceValue?.GetType() == type;
                menu.AddItem(
                    new GUIContent(GetMenuName(type)),
                    selected,
                    () => Assign(targets, propertyPath, selectedType));
            }

            menu.DropDown(buttonRect);
        }

        private static void Assign(
            UnityEngine.Object[] targets,
            string propertyPath,
            Type type)
        {
            SerializedObject serializedObject = new(targets);
            serializedObject.Update();
            SerializedProperty property =
                serializedObject.FindProperty(propertyPath);
            if (property == null)
                return;

            property.managedReferenceValue =
                type == null ? null : Activator.CreateInstance(type);
            serializedObject.ApplyModifiedProperties();
        }

        private static GUIContent GetElementLabel(
            SerializedProperty property,
            GUIContent fallback)
        {
            if (property.managedReferenceValue == null)
                return fallback;

            return new GUIContent(
                ObjectNames.NicifyVariableName(
                    property.managedReferenceValue.GetType().Name),
                fallback.tooltip);
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
    }
}
