using System.Collections.Generic;
using Project.Scripts;
using Project.Scripts.Actions;
using Project.Scripts.DataTypes;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>
    /// Unity applies property attributes placed on arrays to their elements in
    /// some inspector paths. These inspectors deliberately hand the array
    /// container to the pill drawer instead.
    /// </summary>
    public abstract class EntityTagArrayInspector : UnityEditor.Editor
    {
        private readonly EntityTagArrayDrawer tagDrawer = new();
        private readonly ManagedReferenceSelectorDrawer managedReferenceDrawer = new();

        protected abstract HashSet<string> TagPropertyNames { get; }
        protected virtual IReadOnlyDictionary<string, System.Type>
            ManagedReferencePropertyTypes { get; } =
                new Dictionary<string, System.Type>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
                {
                    if (TagPropertyNames.Contains(property.name) && property.isArray)
                        DrawTagArray(property);
                    else if (ManagedReferencePropertyTypes.TryGetValue(
                                 property.name,
                                 out System.Type baseType) &&
                             property.isArray)
                        DrawManagedReferenceArray(property, baseType);
                    else
                        EditorGUILayout.PropertyField(property, true);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTagArray(SerializedProperty property)
        {
            GUIContent label = new(property.displayName, property.tooltip);
            Rect rect = EditorGUILayout.GetControlRect(
                true,
                tagDrawer.GetPropertyHeight(property, label));
            tagDrawer.OnGUI(rect, property, label);
        }

        private void DrawManagedReferenceArray(
            SerializedProperty property,
            System.Type baseType)
        {
            GUIContent label = new(property.displayName, property.tooltip);
            Rect rect = EditorGUILayout.GetControlRect(
                true,
                managedReferenceDrawer.GetPropertyHeight(property, label, baseType));
            managedReferenceDrawer.OnGUI(rect, property, label, baseType);
        }
    }

    [CustomEditor(typeof(NodeData))]
    public sealed class NodeDataEditor : EntityTagArrayInspector
    {
        private static readonly HashSet<string> Properties = new() { "tags" };
        protected override HashSet<string> TagPropertyNames => Properties;
    }

    [CustomEditor(typeof(TileData))]
    public sealed class TileDataEditor : EntityTagArrayInspector
    {
        private static readonly HashSet<string> Properties = new() { "tags" };
        protected override HashSet<string> TagPropertyNames => Properties;
    }

    [CustomEditor(typeof(ItemData))]
    public sealed class ItemDataEditor : EntityTagArrayInspector
    {
        private static readonly HashSet<string> Properties = new() { "tags" };
        private static readonly IReadOnlyDictionary<string, System.Type>
            ManagedReferenceProperties =
                new Dictionary<string, System.Type>
                {
                    { "actionData", typeof(ItemActionData) }
                };

        protected override HashSet<string> TagPropertyNames => Properties;
        protected override IReadOnlyDictionary<string, System.Type>
            ManagedReferencePropertyTypes => ManagedReferenceProperties;
    }

    [CustomEditor(typeof(DestroyNodeToolAction))]
    public sealed class DestroyNodeToolActionEditor : EntityTagArrayInspector
    {
        private static readonly HashSet<string> Properties = new() { "targetTags" };
        protected override HashSet<string> TagPropertyNames => Properties;
    }
}
