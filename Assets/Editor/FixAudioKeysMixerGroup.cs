using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public class FixAudioKeysMixerGroup
{
    public static void Execute()
    {
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/GameMixer.mixer");
        if (mixer == null) { Debug.LogError("FixAudioKeys: mixer not found"); return; }
        Debug.Log("mixer ok");

        var allGroups = mixer.FindMatchingGroups(string.Empty);
        if (allGroups == null) { Debug.LogError("FixAudioKeys: allGroups null"); return; }
        Debug.Log("group count: " + allGroups.Length);
        foreach (var g in allGroups)
            Debug.Log("  group: " + (g != null ? g.name : "NULL"));

        AudioMixerGroup sfxGroup = null;
        foreach (var g in allGroups)
            if (g != null && g.name == "SFX") { sfxGroup = g; break; }

        if (sfxGroup == null) { Debug.LogError("FixAudioKeys: SFX group not found"); return; }
        Debug.Log("sfxGroup ok: " + sfxGroup.name);

        var allAMs = Resources.FindObjectsOfTypeAll<AudioManager>();
        Debug.Log("AudioManager count: " + allAMs.Length);
        AudioManager audioKeys = null;
        foreach (var am in allAMs)
        {
            if (am == null) continue;
            Debug.Log("  AM: " + am.gameObject.name);
            if (am.gameObject.name == "AudioKeys") { audioKeys = am; break; }
        }

        if (audioKeys == null) { Debug.LogError("FixAudioKeys: AudioKeys not found"); return; }

        var src = audioKeys.GetComponent<AudioSource>();
        if (src != null)
        {
            var srcSo = new SerializedObject(src);
            var prop = srcSo.FindProperty("m_OutputAudioMixerGroup");
            if (prop != null) { prop.objectReferenceValue = sfxGroup; srcSo.ApplyModifiedProperties(); Debug.Log("AudioSource group set"); }
            else Debug.LogError("m_OutputAudioMixerGroup prop not found");
        }

        var amSo = new SerializedObject(audioKeys);
        var amProp = amSo.FindProperty("mixerGroup");
        if (amProp != null) { amProp.objectReferenceValue = sfxGroup; amSo.ApplyModifiedProperties(); Debug.Log("AM mixerGroup set"); }
        else Debug.LogError("mixerGroup prop not found");

        EditorUtility.SetDirty(audioKeys.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(audioKeys.gameObject.scene);
        Debug.Log("FixAudioKeys: done");
    }
}
