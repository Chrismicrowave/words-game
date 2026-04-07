using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public class WireSettingsManager
{
    public static void Execute()
    {
        var go = GameObject.Find("--- Settings Manager ---");
        if (go == null) { Debug.LogError("WireSettingsManager: GO not found"); return; }

        var sm = go.GetComponent<SettingsManager>();
        if (sm == null) { Debug.LogError("WireSettingsManager: SettingsManager component not found"); return; }

        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/GameMixer.mixer");
        if (mixer == null) { Debug.LogError("WireSettingsManager: GameMixer.mixer not found"); return; }

        var so = new SerializedObject(sm);
        so.FindProperty("mainMixer").objectReferenceValue = mixer;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("WireSettingsManager: mainMixer assigned to SettingsManager");
    }
}
