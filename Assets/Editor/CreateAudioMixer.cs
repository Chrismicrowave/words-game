using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using System.Reflection;

public class Execute {
    public static void Run() {
        string folderPath = "Assets/Audio";
        string assetPath = folderPath + "/GameMixer.mixer";

        if (!AssetDatabase.IsValidFolder(folderPath))
            AssetDatabase.CreateFolder("Assets", "Audio");

        var editorAsm = typeof(AudioImporter).Assembly;
        var controllerType = editorAsm.GetType("UnityEditor.Audio.AudioMixerController");
        if (controllerType == null) { Debug.LogError("AudioMixerController type not found"); return; }

        // CreateMixerControllerAtPath is a static method that fully handles allocation + serialization
        var createAtPath = controllerType.GetMethod(
            "CreateMixerControllerAtPath",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(string) }, null);

        if (createAtPath == null) { Debug.LogError("CreateMixerControllerAtPath not found"); return; }

        var controller = createAtPath.Invoke(null, new object[] { assetPath });
        Debug.Log("CreateMixerControllerAtPath result: " + (controller ?? "null"));

        AssetDatabase.Refresh();
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(assetPath);
        if (mixer != null)
            Debug.Log("SUCCESS: AudioMixer created at " + assetPath);
        else
            Debug.LogError("Mixer not found at " + assetPath + " after CreateMixerControllerAtPath");
    }
}
