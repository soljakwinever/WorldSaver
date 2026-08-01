using System;
using System.Linq;
using Project.Scripts.DataTypes;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    public static class ValueTagMigration
    {
        private const string Folder = "Assets/Project/Data/Tags";

        [MenuItem("Tools/World Saver/Migrate Item Values To Value Tags")]
        public static void Migrate()
        {
            EnsureFolder();
            ValueTag gold = GetOrCreate("Gold Value", ItemData.GoldValueTagId,
                ValueTag.NumberType.Integer);
            ValueTag magic = GetOrCreate("Magic Value", ItemData.MagicValueTagId,
                ValueTag.NumberType.Float);
            ValueTag fuelValue = GetOrCreate("Fuel Value", ItemData.FuelValueTagId,
                ValueTag.NumberType.Integer);
            EntityTag fuel = AssetDatabase.FindAssets("t:EntityTag Fuel", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<EntityTag>)
                .FirstOrDefault(tag => tag != null && tag is not ValueTag && tag.name == "Fuel");

            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (item == null)
                    continue;

                SerializedObject serialized = new(item);
                serialized.Update();
                SerializedProperty assignments = serialized.FindProperty("valueTags");
                bool dirty = false;
                if (item.goldValue != 0)
                    dirty |= AddIfMissing(assignments, gold, item.goldValue, 0f);
                if (!Mathf.Approximately(item.magicValue, 0f))
                    dirty |= AddIfMissing(assignments, magic, 0, item.magicValue);
                if (fuel != null && item.HasTag(fuel))
                    dirty |= AddIfMissing(assignments, fuelValue, item.fuelValue, 0f);

                if (!dirty)
                    continue;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(item);
                changed++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Value tag migration complete: updated {changed} ItemData assets.");
        }

        private static bool AddIfMissing(SerializedProperty assignments,
            ValueTag tag, int intValue, float floatValue)
        {
            for (int i = 0; i < assignments.arraySize; i++)
            {
                if (assignments.GetArrayElementAtIndex(i).FindPropertyRelative("tag")
                        .objectReferenceValue == tag)
                    return false;
            }

            int index = assignments.arraySize;
            assignments.arraySize = index + 1;
            SerializedProperty assignment = assignments.GetArrayElementAtIndex(index);
            assignment.FindPropertyRelative("tag").objectReferenceValue = tag;
            assignment.FindPropertyRelative("intValue").intValue = intValue;
            assignment.FindPropertyRelative("floatValue").floatValue = floatValue;
            return true;
        }

        private static ValueTag GetOrCreate(string name, string id,
            ValueTag.NumberType type)
        {
            ValueTag existing = AssetDatabase.FindAssets("t:ValueTag", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ValueTag>)
                .FirstOrDefault(tag => tag != null && tag.PersistentId == id);
            if (existing != null)
                return existing;

            ValueTag tag = ScriptableObject.CreateInstance<ValueTag>();
            tag.name = name;
            tag.Configure(id, type);
            AssetDatabase.CreateAsset(tag, $"{Folder}/{name}.asset");
            return tag;
        }

        private static void EnsureFolder()
        {
            string current = "Assets";
            foreach (string segment in Folder.Substring(7).Split('/'))
            {
                string child = $"{current}/{segment}";
                if (!AssetDatabase.IsValidFolder(child))
                    AssetDatabase.CreateFolder(current, segment);
                current = child;
            }
        }
    }
}
