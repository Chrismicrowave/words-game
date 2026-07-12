using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

public class CleanupOrphanEntries
{
    [MenuItem("Tools/Words/Cleanup Orphan Entries")]
    public static void Execute()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection("UI");
        if (collection == null) { Debug.LogError("UI collection not found!"); return; }

        // Build set of valid IDs from SharedData
        var validIds = new HashSet<long>();
        foreach (var entry in collection.SharedData.Entries)
            validIds.Add(entry.Id);

        string[] locales = { "en", "zh-Hans" };
        int removed = 0;

        foreach (var locale in locales)
        {
            var table = collection.GetTable(locale) as StringTable;
            if (table == null) continue;

            var toRemove = new List<long>();
            foreach (var entry in table)
            {
                if (!validIds.Contains(entry.Key))
                    toRemove.Add(entry.Key);
            }

            foreach (var id in toRemove)
            {
                table.Remove(id);
                removed++;
                Debug.Log($"Removed orphan entry id={id} from UI_{locale}");
            }

            if (toRemove.Count > 0)
                EditorUtility.SetDirty(table);
        }

        if (removed > 0)
        {
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Removed {removed} orphan entries.");
        }
        else
            Debug.Log("No orphans found.");
    }
}
