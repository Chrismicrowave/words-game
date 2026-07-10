using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

public static class AddUsernameKey
{
    [MenuItem("Tools/Words/Add Username Key")]
    public static void Execute()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection("UI");
        var shared = collection.SharedData;
        string key = "UI.Settings.Gameplay.Username";

        // Register in shared data first
        if (shared.GetId(key) == 0)
            shared.AddKey(key);
        EditorUtility.SetDirty(shared);

        long id = shared.GetId(key);
        foreach (var st in collection.StringTables)
        {
            if (st == null) continue;
            string val = st.LocaleIdentifier.Code == "zh-Hans" ? "用户名" : "Username";
            var entry = st.AddEntry(id, val);
            EditorUtility.SetDirty(st);
            Debug.Log($"Added '{key}' (id={id}) = '{val}' [{st.LocaleIdentifier.Code}]");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("DONE");
    }
}
