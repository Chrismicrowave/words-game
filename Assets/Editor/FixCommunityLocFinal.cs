using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

public static class FixCommunityLocFinal
{
    [MenuItem("Tools/Words/Fix Community Loc Final")]
    public static void Execute()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection("UI");
        long keyId = collection.SharedData.GetId("UI.Level.TabCommunity");

        foreach (var st in collection.StringTables)
        {
            if (st == null) continue;

            string locale = st.LocaleIdentifier.Code;
            string val = locale == "zh-Hans" ? "社区" : "Community";

            // Set value at the correct key ID from Shared Data
            st.AddEntry(keyId, val);
            EditorUtility.SetDirty(st);
            Debug.Log($"[Fix] locale={locale} keyId={keyId} value='{val}'");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[Fix] DONE");
    }
}
