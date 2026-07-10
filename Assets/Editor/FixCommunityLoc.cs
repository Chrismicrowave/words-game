using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;

public static class FixCommunityLoc
{
    [MenuItem("Tools/Words/Fix Community Loc")]
    public static void Execute()
    {
        var go = Find("--- UI ---/Menus/LevelPanel/Card/TabRows/TabBtn1 - Community/TabNameTMP");
        if (go == null) { Debug.LogError("GO not found"); return; }

        // Set TMP text via SerializedObject so it persists to scene
        var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
        if (tmp != null)
        {
            var soTmp = new SerializedObject(tmp);
            soTmp.FindProperty("m_text").stringValue = "Community";
            soTmp.ApplyModifiedPropertiesWithoutUndo();
        }

        // Remove old LocalizeText
        var old = go.GetComponent<LocalizeText>();
        if (old != null) Object.DestroyImmediate(old);

        // Add fresh LocalizeText, set localizedString via SerializedObject
        var lt = go.AddComponent<LocalizeText>();
        var so = new SerializedObject(lt);
        var ls = so.FindProperty("localizedString");
        ls.FindPropertyRelative("m_TableReference").FindPropertyRelative("m_TableCollectionName").stringValue = "UI";
        ls.FindPropertyRelative("m_TableEntryReference").FindPropertyRelative("m_Key").stringValue = "UI.Level.TabCommunity";

        // Also set the keyId to 0 so it uses the string key, not a numeric ID
        ls.FindPropertyRelative("m_TableEntryReference").FindPropertyRelative("m_KeyId").longValue = 0;

        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(lt);
        EditorUtility.SetDirty(go);
        AssetDatabase.SaveAssets();
        Debug.Log($"[FixLoc] DONE. localizedString=UI/UI.Level.TabCommunity, tmp.text=Community");
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
