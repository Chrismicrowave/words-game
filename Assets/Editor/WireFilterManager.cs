using UnityEngine;
using UnityEditor;

public class WireFilterManager
{
    public static void Execute()
    {
        GameObject gameSystems = GameObject.Find("GameSystems");
        if (gameSystems == null) { Debug.LogError("[WireFilterManager] GameSystems not found."); return; }

        FilterManager fm = gameSystems.GetComponent<FilterManager>();
        if (fm == null) { Debug.LogError("[WireFilterManager] FilterManager not found on GameSystems."); return; }

        GameObject camGO = GameObject.Find("--- Cam&Ltg ---/Main Camera");
        if (camGO == null) { Debug.LogError("[WireFilterManager] Main Camera not found."); return; }

        BrewedInk.CRT.CRTCameraBehaviour crt = camGO.GetComponent<BrewedInk.CRT.CRTCameraBehaviour>();
        if (crt == null) { Debug.LogError("[WireFilterManager] CRTCameraBehaviour not found on Main Camera."); return; }

        SerializedObject so = new SerializedObject(fm);
        SerializedProperty list = so.FindProperty("filterBehaviours");
        list.ClearArray();
        list.InsertArrayElementAtIndex(0);
        list.GetArrayElementAtIndex(0).objectReferenceValue = crt;
        so.ApplyModifiedProperties();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

        Debug.Log("[WireFilterManager] CRTCameraBehaviour assigned to slot 0.");
    }
}
