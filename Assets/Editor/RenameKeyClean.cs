using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

public static class RenameKeyClean
{
    [MenuItem("Tools/Words/Rename Key Clean")]
    public static void Execute()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection("UI");
        var sharedData = collection.SharedData;

        // 1. Rename key in Shared Data (keeps same ID 741335077)
        sharedData.RenameKey("UI.Level.TabTrendy", "UI.Level.TabCommunity");
        EditorUtility.SetDirty(sharedData);

        // 2. Update values in per-locale tables (same ID, new text)
        long keyId = sharedData.GetId("UI.Level.TabCommunity");
        foreach (var st in collection.StringTables)
        {
            if (st == null) continue;
            string value = st.LocaleIdentifier.Code == "zh-Hans" ? "社区" : "Community";
            st.AddEntry(keyId, value);
            EditorUtility.SetDirty(st);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[RenameKeyClean] DONE");
    }
}
