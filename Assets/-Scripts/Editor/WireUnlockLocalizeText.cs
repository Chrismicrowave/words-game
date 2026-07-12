using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;

public class WireUnlockLocalizeText
{
    [MenuItem("Tools/Words/Wire Unlock LocalizeText")]
    public static void Execute()
    {
        var prefabPath = "Assets/-Prefabs/UI/LevelCellButton.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) { Debug.LogError("Prefab not found!"); return; }

        // Find the Unlocks child
        var unlocksTf = prefab.transform.Find("Unlocks");
        if (unlocksTf == null) { Debug.LogError("Unlocks child not found!"); return; }

        var localizeText = unlocksTf.GetComponent<LocalizeText>();
        if (localizeText == null) { Debug.LogError("LocalizeText not found on Unlocks!"); return; }

        // Wire the localized string
        localizeText.localizedString = new LocalizedString
        {
            TableReference = "UI",
            TableEntryReference = "UI.Level.UnlockCustom"
        };

        EditorUtility.SetDirty(localizeText);
        EditorUtility.SetDirty(unlocksTf.gameObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications(localizeText);

        AssetDatabase.SaveAssets();
        Debug.Log("Wired LocalizeText on Unlocks to UI.Level.UnlockCustom");
    }
}
