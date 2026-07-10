using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.Localization;

/// <summary>
/// Renames TabTrendy → TabCommunity in the UI string table.
/// Run once via Tools/Words/Rename TabTrendy → TabCommunity.
/// Cleanup: Delete this file after use.
/// </summary>
public static class RenameLocalizationKey
{
    [MenuItem("Tools/Words/Rename TabTrendy → TabCommunity")]
    public static void Execute()
    {
        string tableName = "UI";
        string oldKey = "UI.Level.TabTrendy";
        string newKey = "UI.Level.TabCommunity";

        // Find the string table collection
        var localizationSettings = LocalizationEditorSettings.GetStringTableCollection(tableName);
        if (localizationSettings == null)
        {
            Debug.LogError($"[RenameKey] String table collection '{tableName}' not found.");
            return;
        }

        int renamed = 0;
        foreach (var stringTable in localizationSettings.StringTables)
        {
            if (stringTable == null) continue;

            string localeCode = stringTable.LocaleIdentifier.Code;

            // Find the entry by key
            var entry = stringTable.GetEntry(oldKey);
            if (entry == null)
            {
                Debug.LogWarning($"[RenameKey] Entry '{oldKey}' not found in locale {localeCode}");
                continue;
            }

            // Remove old entry, add new one with same value
            string value = entry.Value;
            stringTable.Remove(entry.KeyId);
            stringTable.AddEntry(newKey, value);

            EditorUtility.SetDirty(stringTable);
            renamed++;
            Debug.Log($"[RenameKey] '{oldKey}' → '{newKey}' = '{value}' ({localeCode})");
        }

        if (renamed > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[RenameKey] Done. Renamed {renamed} locale(s).");
        }
        else
        {
            Debug.LogWarning("[RenameKey] No entries found to rename. Check the key exists.");
        }
    }
}
