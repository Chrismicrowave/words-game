using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

public static class AddUsernameLoc
{
    [MenuItem("Tools/Words/Add Username Loc")]
    public static void Execute()
    {
        string tableName = "UI";
        string key = "UI.Settings.Gameplay.Username";

        var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
        if (collection == null) { Debug.LogError("Collection not found"); return; }

        foreach (var st in collection.StringTables)
        {
            if (st == null) continue;
            string value = st.LocaleIdentifier.Code == "zh-Hans" ? "用户名" : "Username";
            st.AddEntry(key, value);
            EditorUtility.SetDirty(st);
            Debug.Log($"[AddLoc] '{key}' = '{value}' ({st.LocaleIdentifier.Code})");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[AddLoc] DONE");
    }
}
