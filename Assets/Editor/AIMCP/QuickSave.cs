using UnityEditor;
using UnityEngine;

/// <summary>
/// Fast scene save via Editor API (no MCP overhead or timeout).
/// Run: execute_script -> QuickSave.Execute
/// </summary>
public static class QuickSave
{
    public static void Execute()
    {
        if (EditorApplication.isCompiling)
        {
            Debug.LogWarning("[QuickSave] Unity is compiling — skipping save");
            return;
        }
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (!scene.isDirty)
        {
            Debug.Log("[QuickSave] Scene not dirty, skipping");
            return;
        }
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        System.IO.File.WriteAllText("Library/AgentMirror/.generate-snapshots-signal", "save");
        Debug.Log($"[QuickSave] Saved '{scene.path}' + snapshots triggered");
    }
}
