#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

/// <summary>
/// Marks Smart Format entries in the Gameplay table as IsSmart = true.
/// Run via Tools > Words > Fix Smart Strings.
/// </summary>
public class FixSmartStrings
{
    static readonly string[] SmartKeys = { "Gameplay.ExpectedTo", "Gameplay.ActionPrompt" };

    [MenuItem("Tools/Words/Fix Smart Strings")]
    public static void Execute()
    {
        var col = GetCollection("Gameplay");
        if (col == null) { Debug.LogError("[FixSmartStrings] Gameplay collection not found."); return; }

        foreach (var table in col.StringTables)
        {
            foreach (var key in SmartKeys)
            {
                var entry = table.GetEntry(key);
                if (entry == null) { Debug.LogWarning($"[FixSmartStrings] Key '{key}' not found in {table.name}."); continue; }
                entry.IsSmart = true;
                EditorUtility.SetDirty(table);
                Debug.Log($"[FixSmartStrings] Marked IsSmart=true: {key} in {table.name}");
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[FixSmartStrings] DONE.");
    }

    static StringTableCollection GetCollection(string name)
    {
        foreach (var c in LocalizationEditorSettings.GetStringTableCollections())
            if (c.TableCollectionName == name) return c;
        return null;
    }
}
#endif
