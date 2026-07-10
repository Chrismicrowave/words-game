using UnityEditor;
using UnityEngine;
using TMPro;
using UnityEngine.Localization;

public static class DebugLocalizeText
{
    [MenuItem("Tools/Words/Debug Loc")]
    public static void Execute()
    {
        Check("Challenges",  "--- UI ---/Menus/LevelPanel/Card/TabRows/TabBtn1/TabNameTMP");
        Check("Custom",      "--- UI ---/Menus/LevelPanel/Card/TabRows/TabBtn1 (1)/TabNameTMP");
        Check("Community",   "--- UI ---/Menus/LevelPanel/Card/TabRows/TabBtn1 - Community/TabNameTMP");
    }

    static void Check(string label, string path)
    {
        var go = Find(path);
        if (go == null) { Debug.LogError($"[DebugLoc] {label}: NOT FOUND"); return; }

        var lt = go.GetComponent<LocalizeText>();
        if (lt == null) { Debug.LogError($"[DebugLoc] {label}: NO LocalizeText"); return; }

        var so = new SerializedObject(lt);
        var ls = so.FindProperty("localizedString");
        string table = ls?.FindPropertyRelative("m_TableReference")?.FindPropertyRelative("m_TableCollectionName")?.stringValue ?? "NULL";
        string key = ls?.FindPropertyRelative("m_TableEntryReference")?.FindPropertyRelative("m_Key")?.stringValue ?? "NULL";

        var tmp = go.GetComponent<TextMeshProUGUI>();
        string tmpText = tmp != null ? tmp.text : "NO TMP";

        Debug.Log($"[DebugLoc] {label}: table='{table}' key='{key}' tmp.text='{tmpText}'");
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
