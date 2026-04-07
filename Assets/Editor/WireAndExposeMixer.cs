using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using System.Reflection;

public class WireAndExpose {
    public static void Run() {
        string mixerPath = "Assets/Audio/GameMixer.mixer";

        // --- 1. Wire mixer to SettingsManager ---
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(mixerPath);
        if (mixer == null) { Debug.LogError("WireAndExpose: Mixer not found at " + mixerPath); return; }

        var go = GameObject.Find("GameSystems");
        if (go == null) { Debug.LogError("WireAndExpose: GameSystems not found"); return; }

        var settingsManager = go.GetComponent("SettingsManager") as MonoBehaviour;
        if (settingsManager == null) { Debug.LogError("WireAndExpose: SettingsManager component not found"); return; }

        var mainMixerField = settingsManager.GetType().GetField("mainMixer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (mainMixerField == null) { Debug.LogError("WireAndExpose: mainMixer field not found"); return; }

        mainMixerField.SetValue(settingsManager, mixer);
        EditorUtility.SetDirty(settingsManager);
        Debug.Log("WireAndExpose: mainMixer assigned on SettingsManager");

        // --- 2. Expose parameters ---
        var editorAsm = typeof(AudioImporter).Assembly;
        var controllerType = editorAsm.GetType("UnityEditor.Audio.AudioMixerController");
        var effectControllerType = editorAsm.GetType("UnityEditor.Audio.AudioMixerEffectController");
        var paramPathType = editorAsm.GetType("UnityEditor.Audio.AudioGroupParameterPath");

        if (controllerType == null || paramPathType == null) {
            Debug.LogError("Required types not found");
            goto SaveAndDone;
        }

        {
            var controller = AssetDatabase.LoadMainAssetAtPath(mixerPath);

            // Get master group
            var masterGroupProp = controllerType.GetProperty("masterGroup",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var masterGroup = masterGroupProp?.GetValue(controller);
            if (masterGroup == null) { Debug.LogError("masterGroup not found"); goto SaveAndDone; }

            // Get Attenuation effect
            var effectsProp = masterGroup.GetType().GetProperty("effects",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var effects = effectsProp?.GetValue(masterGroup) as System.Array;
            if (effects == null || effects.Length == 0) { Debug.LogError("No effects on master group"); goto SaveAndDone; }

            var attenuationEffect = effects.GetValue(0);

            // Get volume GUID
            var getGUIDForMixLevel = effectControllerType?.GetMethod("GetGUIDForMixLevel",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getGUIDForMixLevel == null) { Debug.LogError("GetGUIDForMixLevel not found"); goto SaveAndDone; }

            var volumeGuid = getGUIDForMixLevel.Invoke(attenuationEffect, null);

            // Create AudioGroupParameterPath(group, guid)
            var paramPathCtor = paramPathType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];

            var addExposed = controllerType.GetMethod("AddExposedParameter",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (addExposed == null) { Debug.LogError("AddExposedParameter not found"); goto SaveAndDone; }

            // Check if MasterVolume already exposed
            var numExpProp = controllerType.GetProperty("numExposedParameters",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            int numBefore = (int)(numExpProp?.GetValue(controller) ?? 0);

            if (numBefore == 0) {
                // Expose MasterVolume
                var masterParamPath = paramPathCtor.Invoke(new object[] { masterGroup, volumeGuid });
                addExposed.Invoke(controller, new object[] { masterParamPath });
                SetLastExposedParamName(controller, controllerType, "MasterVolume");
                Debug.Log("MasterVolume exposed");
            } else {
                Debug.Log("Already has " + numBefore + " exposed parameter(s) — skipping MasterVolume");
            }

            // --- 3. Create SFX group ---
            var sfxGroups = mixer.FindMatchingGroups("SFX");
            object sfxGroup = null;

            if (sfxGroups != null && sfxGroups.Length > 0) {
                sfxGroup = sfxGroups[0];
                Debug.Log("SFX group already exists: " + sfxGroups[0].name);
            } else {
                try {
                    var createGroup = controllerType.GetMethod("CreateNewGroup",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var addChild = controllerType.GetMethod("AddChildToParent",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var addToView = controllerType.GetMethod("AddGroupToCurrentView",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    Undo.RegisterCompleteObjectUndo(new Object[] { controller as Object }, "Create SFX group");

                    sfxGroup = createGroup?.Invoke(controller, new object[] { "SFX", false });
                    if (sfxGroup != null) {
                        addChild?.Invoke(controller, new object[] { sfxGroup, masterGroup });
                        addToView?.Invoke(controller, new object[] { sfxGroup });
                        Debug.Log("Created SFX group");
                    } else {
                        Debug.LogWarning("CreateNewGroup returned null");
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning("SFX group creation failed: " + e.Message + " — continuing without SFXVolume");
                    sfxGroup = null;
                }
            }

            // Expose SFXVolume if SFX group exists
            if (sfxGroup != null) {
                try {
                    var sfxEffectsProp = sfxGroup.GetType().GetProperty("effects",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var sfxEffects = sfxEffectsProp?.GetValue(sfxGroup) as System.Array;
                    if (sfxEffects != null && sfxEffects.Length > 0) {
                        var sfxAttenuation = sfxEffects.GetValue(0);
                        var sfxVolumeGuid = getGUIDForMixLevel.Invoke(sfxAttenuation, null);
                        var sfxParamPath = paramPathCtor.Invoke(new object[] { sfxGroup, sfxVolumeGuid });
                        addExposed.Invoke(controller, new object[] { sfxParamPath });
                        SetLastExposedParamName(controller, controllerType, "SFXVolume");
                        Debug.Log("SFXVolume exposed");
                    } else {
                        Debug.LogWarning("SFX group has no effects yet");
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning("SFXVolume expose failed: " + e.Message);
                }
            }

            EditorUtility.SetDirty(controller as Object);

            int numAfter = (int)(numExpProp?.GetValue(controller) ?? 0);
            Debug.Log("Exposed parameters after: " + numAfter);
        }

SaveAndDone:
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("WireAndExpose: DONE — assets and scene saved");
    }

    static void SetLastExposedParamName(object controller, System.Type controllerType, string name) {
        var prop = controllerType.GetProperty("exposedParameters",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop == null) { Debug.LogWarning("exposedParameters property not found"); return; }

        var epsRaw = prop.GetValue(controller);
        if (epsRaw == null) { Debug.LogWarning("exposedParameters is null"); return; }

        var epsArray = epsRaw as System.Array;
        if (epsArray == null) { Debug.LogWarning("exposedParameters type=" + epsRaw.GetType().FullName); return; }
        if (epsArray.Length == 0) { Debug.LogWarning("exposedParameters empty"); return; }

        var last = epsArray.GetValue(epsArray.Length - 1);
        var lastType = last.GetType();
        var nameField = lastType.GetField("name", BindingFlags.Public | BindingFlags.Instance);
        if (nameField != null) {
            object boxed = last;
            nameField.SetValue(boxed, name);
            epsArray.SetValue(boxed, epsArray.Length - 1);
            prop.SetValue(controller, epsArray);
            Debug.Log("Set exposed param name: " + name);
        } else {
            Debug.LogWarning("name field not found on " + lastType.FullName + " — available: " +
                string.Join(", ", System.Array.ConvertAll(lastType.GetFields(BindingFlags.Public | BindingFlags.Instance), f => f.Name)));
        }
    }
}
