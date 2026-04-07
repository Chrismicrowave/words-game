using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public class VerifyMixerGroups
{
    public static void Execute()
    {
        var allAMs = Resources.FindObjectsOfTypeAll<AudioManager>();
        foreach (var am in allAMs)
        {
            var so = new SerializedObject(am);
            var prop = so.FindProperty("mixerGroup");
            var group = prop?.objectReferenceValue as AudioMixerGroup;
            Debug.Log($"{am.gameObject.name} mixerGroup = {(group != null ? group.name : "NULL")}");
        }
    }
}
