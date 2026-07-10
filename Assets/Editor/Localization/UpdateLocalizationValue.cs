using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

/// <summary>
/// Updates the value of UI.Level.TabCommunity in all locales.
/// Cleanup: Delete this file after use.
/// </summary>
public static class UpdateLocalizationValue
{
    [MenuItem("Tools/Words/Update TabCommunity Value")]
    public static void Execute()
    {
        string tableName = "UI";
        string key = "UI.Level.TabCommunity";

        var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
        if (collection == null) { Debug.LogError("Not found"); return; }

        foreach (var st in collection.StringTables)
        {
            if (st == null) continue;
            var entry = st.GetEntry(key);
            if (entry == null) continue;

            string locale = st.LocaleIdentifier.Code;
            string newVal = locale == "zh-Hans" ? "社区" : "Community";
            entry.Value = newVal;
            EditorUtility.SetDirty(st);
            Debug.Log($"[UpdateValue] '{key}' = '{newVal}' ({locale})");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[UpdateValue] Done.");
    }
}
