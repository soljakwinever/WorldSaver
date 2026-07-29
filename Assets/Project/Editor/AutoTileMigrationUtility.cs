#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Project.Scripts.DataTypes;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using TileData = Project.Scripts.DataTypes.TileData;

/// <summary>
/// One-way migration helper for legacy RuleTile assets. It intentionally uses
/// SerializedObject rather than a RuleTile reference, so the runtime and data
/// assemblies have no dependency on Unity's RuleTile package.
/// </summary>
public static class AutoTileMigrationUtility
{
    private const string OutputFolder = "Assets/Project/Data/AutoTiles";
    private const string SessionMigrationKey =
        "WorldSaver.CustomAutoTileMigration.v1";

    [InitializeOnLoadMethod]
    private static void MigrateProjectAfterScriptsReload()
    {
        if (SessionState.GetBool(SessionMigrationKey, false))
            return;

        SessionState.SetBool(SessionMigrationKey, true);
        EditorApplication.delayCall += MigrateAllTileData;
    }

    [MenuItem("Tools/World/Migrate All TileData To Custom Auto Tiles")]
    public static void MigrateAllTileData()
    {
        EnsureFolder(OutputFolder);
        string[] guids = AssetDatabase.FindAssets("t:TileData");
        int converted = 0;

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TileData tileData = AssetDatabase.LoadAssetAtPath<TileData>(path);
                if (tileData == null ||
                    tileData.AutoTile != null ||
                    tileData.TileBase == null)
                {
                    continue;
                }

                SerializedObject legacyTile =
                    new SerializedObject(tileData.TileBase);
                SerializedProperty defaultSprite =
                    legacyTile.FindProperty("m_DefaultSprite");
                SerializedProperty rules =
                    legacyTile.FindProperty("m_TilingRules");
                if (defaultSprite == null || rules == null)
                    continue;

                AutoTileDefinition definition =
                    ScriptableObject.CreateInstance<AutoTileDefinition>();
                definition.name = $"{tileData.name} Auto Tile";
                definition.DefaultSprite =
                    defaultSprite.objectReferenceValue as Sprite;
                definition.DefaultColliderType = ReadCollider(
                    legacyTile.FindProperty("m_DefaultColliderType"));
                definition.Rules = ReadRules(rules);

                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{OutputFolder}/{SanitizeFileName(definition.name)}.asset");
                AssetDatabase.CreateAsset(definition, assetPath);
                tileData.AutoTile = definition;
                EditorUtility.SetDirty(tileData);
                converted++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log(
            $"Migrated {converted} TileData assets to the custom prebaked auto-tile system.");
    }

    private static AutoTileRule[] ReadRules(SerializedProperty rules)
    {
        List<AutoTileRule> converted = new(rules.arraySize);
        for (int ruleIndex = 0; ruleIndex < rules.arraySize; ruleIndex++)
        {
            SerializedProperty source = rules.GetArrayElementAtIndex(ruleIndex);
            SerializedProperty neighbors =
                source.FindPropertyRelative("m_Neighbors");
            SerializedProperty positions =
                source.FindPropertyRelative("m_NeighborPositions");
            SerializedProperty sprites =
                source.FindPropertyRelative("m_Sprites");

            AutoTileRule rule = new()
            {
                ColliderType = ReadCollider(
                    source.FindPropertyRelative("m_ColliderType")),
                RuleTransform = ReadRuleTransform(
                    source.FindPropertyRelative("m_RuleTransform")),
                RandomTransform = ReadRandomTransform(
                    source.FindPropertyRelative("m_RandomTransform")),
                Sprites = ReadSprites(sprites)
            };

            int conditionCount = Math.Min(
                neighbors?.arraySize ?? 0,
                positions?.arraySize ?? 0);
            for (int i = 0; i < conditionCount; i++)
            {
                Vector3Int position =
                    positions.GetArrayElementAtIndex(i).vector3IntValue;
                int directionIndex = TryGetDirectionIndex(position);
                if (directionIndex < 0)
                    continue;

                int condition =
                    neighbors.GetArrayElementAtIndex(i).intValue;
                if (condition == 1)
                    rule.RequiredNeighbors |= (byte)(1 << directionIndex);
                else if (condition == 2)
                    rule.ForbiddenNeighbors |= (byte)(1 << directionIndex);
            }

            converted.Add(rule);
        }

        return converted.ToArray();
    }

    private static Sprite[] ReadSprites(SerializedProperty sprites)
    {
        if (sprites == null || sprites.arraySize == 0)
            return Array.Empty<Sprite>();

        List<Sprite> result = new(sprites.arraySize);
        for (int i = 0; i < sprites.arraySize; i++)
        {
            Sprite sprite =
                sprites.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            if (sprite != null)
                result.Add(sprite);
        }

        return result.ToArray();
    }

    private static int TryGetDirectionIndex(Vector3Int position)
    {
        if (position.z != 0 ||
            position.x < -1 || position.x > 1 ||
            position.y < -1 || position.y > 1 ||
            (position.x == 0 && position.y == 0))
        {
            return -1;
        }

        return AutoTileDirections.IndexOf(
            new Vector2Int(position.x, position.y));
    }

    private static Tile.ColliderType ReadCollider(SerializedProperty property)
    {
        return property == null
            ? Tile.ColliderType.None
            : (Tile.ColliderType)property.intValue;
    }

    private static AutoTileRuleTransform ReadRuleTransform(
        SerializedProperty property)
    {
        return property?.intValue switch
        {
            1 => AutoTileRuleTransform.Rotated,
            2 => AutoTileRuleTransform.MirrorX,
            3 => AutoTileRuleTransform.MirrorY,
            _ => AutoTileRuleTransform.Fixed
        };
    }

    private static AutoTileRandomTransform ReadRandomTransform(
        SerializedProperty property)
    {
        return property?.intValue switch
        {
            1 => AutoTileRandomTransform.Rotated,
            2 => AutoTileRandomTransform.MirrorX,
            3 => AutoTileRandomTransform.MirrorY,
            _ => AutoTileRandomTransform.Fixed
        };
    }

    private static void EnsureFolder(string folder)
    {
        string current = "Assets";
        string[] segments = folder.Split('/');
        for (int i = 1; i < segments.Length; i++)
        {
            string next = $"{current}/{segments[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);
            current = next;
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }
}
#endif
