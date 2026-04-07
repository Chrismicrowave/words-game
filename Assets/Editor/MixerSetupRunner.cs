#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class MixerSetupRunner
{
    public static void Execute()
    {
        const string path = "Assets/Audio/GameMixer.mixer";

        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
        if (mixer == null)
        {
            Debug.LogError("MixerSetupRunner: GameMixer.mixer not found at " + path);
            return;
        }
        Debug.Log("MixerSetupRunner: mixer found OK");

        var asm       = typeof(AudioImporter).Assembly;
        var ctrlType  = asm.GetType("UnityEditor.Audio.AudioMixerController");
        var groupType = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");

        if (ctrlType == null) { Debug.LogError("MixerSetupRunner: AudioMixerController type not found"); return; }

        // Load controller from asset
        UnityEngine.Object ctrl = null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
            if (a != null && a.GetType() == ctrlType) { ctrl = a; break; }

        if (ctrl == null) { Debug.LogError("MixerSetupRunner: controller asset not found in " + path); return; }
        Debug.Log("MixerSetupRunner: controller loaded: " + ctrl.GetType().Name);

        // ── Get master group ──────────────────────────────────────────────
        var masterGroupProp = ctrlType.GetProperty("masterGroup",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var masterGroup = masterGroupProp?.GetValue(ctrl);
        if (masterGroup == null) { Debug.LogError("MixerSetupRunner: masterGroup is null"); return; }
        Debug.Log("MixerSetupRunner: masterGroup = " + (masterGroup as UnityEngine.Object)?.name);

        // ── Check / create SFX child group ────────────────────────────────
        UnityEngine.Object sfxGroup = FindOrCreateSFXGroup(ctrl, ctrlType, groupType, masterGroup);

        // ── Expose volume params ──────────────────────────────────────────
        ExposeVolume(ctrl, ctrlType, groupType, masterGroup, "MasterVolume");
        if (sfxGroup != null)
            ExposeVolume(ctrl, ctrlType, groupType, sfxGroup, "SFXVolume");
        else
            Debug.LogWarning("MixerSetupRunner: SFX group missing — SFXVolume NOT exposed. Add SFX group manually and expose its volume.");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("MixerSetupRunner: COMPLETE");
        Selection.activeObject = mixer;
    }

    static UnityEngine.Object FindOrCreateSFXGroup(UnityEngine.Object ctrl, Type ctrlType, Type groupType, object masterGroup)
    {
        // Check existing children
        var childrenProp = groupType.GetProperty("children",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (childrenProp != null)
        {
            var children = childrenProp.GetValue(masterGroup) as System.Array;
            if (children != null)
                foreach (var c in children)
                {
                    var obj = c as UnityEngine.Object;
                    if (obj != null && obj.name == "SFX") { Debug.Log("MixerSetupRunner: SFX group already exists"); return obj; }
                }
        }

        // Try to create it — enumerate overloads
        foreach (var m in ctrlType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.Name != "CreateNewGroup") continue;
            var p = m.GetParameters();
            Debug.Log("MixerSetupRunner: CreateNewGroup overload params=" + p.Length);
            try
            {
                UnityEngine.Object result = null;
                if (p.Length == 2)
                    result = m.Invoke(ctrl, new object[] { masterGroup, "SFX" }) as UnityEngine.Object;
                else if (p.Length == 3)
                    result = m.Invoke(ctrl, new object[] { masterGroup, "SFX", true }) as UnityEngine.Object;
                else if (p.Length >= 4)
                    result = m.Invoke(ctrl, new object[] { masterGroup, "SFX", true, false }) as UnityEngine.Object;

                if (result != null) { Debug.Log("MixerSetupRunner: SFX group created"); return result; }
            }
            catch (Exception ex) { Debug.LogWarning("MixerSetupRunner: CreateNewGroup(" + p.Length + ") threw: " + ex.Message); }
        }
        Debug.LogWarning("MixerSetupRunner: could not create SFX group — no working overload found");
        return null;
    }

    static void ExposeVolume(UnityEngine.Object ctrl, Type ctrlType, Type groupType, object group, string paramName)
    {
        // Get the GUID of the volume parameter for this group
        var volumeField = groupType.GetField("m_Volume",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (volumeField == null)
        {
            Debug.LogWarning("MixerSetupRunner: m_Volume field not found on group — cannot expose " + paramName);
            // Log available fields
            foreach (var f in groupType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Debug.Log("  group field: " + f.Name + " : " + f.FieldType);
            return;
        }

        var volumeGuid = volumeField.GetValue(group);
        Debug.Log("MixerSetupRunner: " + paramName + " volume GUID = " + volumeGuid);

        // Get m_ExposedParameters on controller
        var expField = ctrlType.GetField("m_ExposedParameters",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (expField == null)
        {
            Debug.LogWarning("MixerSetupRunner: m_ExposedParameters field not found — cannot expose " + paramName);
            foreach (var f in ctrlType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Debug.Log("  ctrl field: " + f.Name + " : " + f.FieldType);
            return;
        }

        var currentArr = expField.GetValue(ctrl) as System.Array;
        var elemType = expField.FieldType.IsArray
            ? expField.FieldType.GetElementType()
            : expField.FieldType.GetGenericArguments()[0];
        Debug.Log("MixerSetupRunner: exposed param elem type = " + elemType);

        // Check already exposed
        if (currentArr != null)
            foreach (var elem in currentArr)
            {
                var nf = elemType.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (nf != null && (string)nf.GetValue(elem) == paramName)
                {
                    Debug.Log("MixerSetupRunner: " + paramName + " already exposed — skipping");
                    return;
                }
            }

        // Build new entry
        var entry = Activator.CreateInstance(elemType);
        var entryFields = elemType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var f in entryFields) Debug.Log("  exposed param entry field: " + f.Name + " : " + f.FieldType);

        var nameField = elemType.GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var guidField = elemType.GetField("guid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (nameField == null || guidField == null)
        {
            Debug.LogWarning("MixerSetupRunner: name/guid fields not found on exposed param type — cannot expose " + paramName);
            return;
        }

        nameField.SetValue(entry, paramName);
        guidField.SetValue(entry, volumeGuid);

        int oldLen = currentArr?.Length ?? 0;
        var newArr = Array.CreateInstance(elemType, oldLen + 1);
        if (currentArr != null) Array.Copy(currentArr, newArr, oldLen);
        newArr.SetValue(entry, oldLen);
        expField.SetValue(ctrl, newArr);

        Debug.Log("MixerSetupRunner: " + paramName + " exposed successfully");
    }
}
#endif
