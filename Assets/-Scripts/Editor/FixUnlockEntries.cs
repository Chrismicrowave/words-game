using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

public class FixUnlockEntries
{
    [MenuItem("Tools/Words/Fix Unlock Entries")]
    public static void Execute()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection("UI");
        if (collection == null) { Debug.LogError("UI collection not found!"); return; }

        string[] keys = { "UI.Level.UnlockCustom", "UI.Level.UnlockCommunity" };
        string[] enVals = { "*Unlocks Custom List", "*Unlocks Community List (coming soon)" };
        string[] zhVals = { "*解锁自定义列表", "*解锁社区列表（即将推出）" };

        for (int i = 0; i < keys.Length; i++)
        {
            var enTable = collection.GetTable("en") as StringTable;
            if (enTable != null)
            {
                enTable.AddEntry(keys[i], enVals[i]);
                EditorUtility.SetDirty(enTable);
            }

            var zhTable = collection.GetTable("zh-Hans") as StringTable;
            if (zhTable != null)
            {
                zhTable.AddEntry(keys[i], zhVals[i]);
                EditorUtility.SetDirty(zhTable);
            }
        }

        EditorUtility.SetDirty(collection);
        EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Done. Check Window > Asset Management > Localization Tables.");
    }
}
