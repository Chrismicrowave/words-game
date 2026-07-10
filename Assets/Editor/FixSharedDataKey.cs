using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

public static class FixSharedDataKey
{
    [MenuItem("Tools/Words/Fix Shared Data Key")]
    public static void Execute()
    {
        string tableName = "UI";
        string oldKey = "UI.Level.TabTrendy";
        string newKey = "UI.Level.TabCommunity";

        var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
        if (collection == null) { Debug.LogError("Collection not found"); return; }

        // 1. Rename the key in Shared Data
        var sharedData = collection.SharedData;
        if (sharedData == null) { Debug.LogError("SharedData not found"); return; }

        sharedData.RenameKey(oldKey, newKey);
        EditorUtility.SetDirty(sharedData);
        Debug.Log($"[FixKey] Renamed SharedData key: '{oldKey}' → '{newKey}'");

        // 2. Update the per-locale values for the same key ID
        var keyId = sharedData.GetId(newKey);
        foreach (var st in collection.StringTables)
        {
            if (st == null) continue;
            var entry = st.GetEntry(keyId);
            if (entry == null) continue;
            string locale = st.LocaleIdentifier.Code;
            string newValue = locale == "zh-Hans" ? "社区" : "Community";
            entry.Value = newValue;
            EditorUtility.SetDirty(st);
            Debug.Log($"[FixKey] Updated value: '{newKey}' = '{newValue}' ({locale})");
        }

        // 3. Find the community tab button and set its LocalizeText
        var go = Find("--- UI ---/Menus/LevelPanel/Card/TabRows/TabBtn1 - Community/TabNameTMP");
        if (go == null) { Debug.LogError("GO not found — skipping LocalizeText update"); }
        else
        {
            var old = go.GetComponent<LocalizeText>();
            if (old != null) Object.DestroyImmediate(old);

            var lt = go.AddComponent<LocalizeText>();
            var so = new SerializedObject(lt);
            var ls = so.FindProperty("localizedString");
            ls.FindPropertyRelative("m_TableReference").FindPropertyRelative("m_TableCollectionName").stringValue = "UI";
            ls.FindPropertyRelative("m_TableEntryReference").FindPropertyRelative("m_Key").stringValue = newKey;
            ls.FindPropertyRelative("m_TableEntryReference").FindPropertyRelative("m_KeyId").longValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp != null)
            {
                var soTmp = new SerializedObject(tmp);
                soTmp.FindProperty("m_text").stringValue = "Community";
                soTmp.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(go);
            Debug.Log($"[FixKey] Set LocalizeText + TMP text on Community tab");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FixKey] DONE.");
    }

    static GameObject Find(string path)
    {
        var parts = path.Split('/');
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                Transform found = null;
                for (int c = 0; c < t.childCount; c++)
                    if (t.GetChild(c).name == parts[i]) { found = t.GetChild(c); break; }
                if (found == null) return null;
                t = found;
            }
            return t.gameObject;
        }
        return null;
    }
}
