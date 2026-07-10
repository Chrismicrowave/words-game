#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;

/// <summary>
/// Bulk-wires LocalizeText components to all TMP scene objects,
/// and adds LocalizationBootstrapper to GameSystems.
/// Run via Tools > Words > Wire Localization To Scene.
/// Safe to re-run — removes and re-adds LocalizeText on each object.
/// </summary>
public class WireLocalizationToScene
{
    [MenuItem("Tools/Words/Wire Localization To Scene")]
    public static void Execute()
    {
        int wired = 0;

        // ── Static TMP labels ─────────────────────────────────────────────────

        var uiMappings = new (string path, string table, string key)[]
        {
            // Instructions
            ("--- UI ---/instruction", "UI", "UI.Instructions.Body"),

            // HUD
            ("--- UI ---/HUD-Btns/WordsBtn/Text (TMP)",    "UI", "UI.HUD.WordsBtn"),
            ("--- UI ---/HUD-Btns/LevelBtn/Text (TMP)",    "UI", "UI.HUD.PlayBtn"),
            ("--- UI ---/HUD-Btns/TimerBtn/Text (TMP)",    "UI", "UI.HUD.TimerBtn"),
            ("--- UI ---/HUD-Btns/InfoBtn/Text",           "UI", "UI.HUD.InfoBtn"),
            ("--- UI ---/HUD-Btns/ResetBtn/Text (TMP)",    "UI", "UI.HUD.ResetBtn"),
            ("--- UI ---/HUD-Btns/SettingsBtn/Text (TMP)", "UI", "UI.HUD.SettingsBtn"),
            ("--- UI ---/HUD-Btns/CloseBtn/Text (TMP)",    "UI", "UI.HUD.CloseBtn"),

            // Settings panel
            ("--- UI ---/Menus/SettingsPanel/Card/Title",                                      "UI", "UI.Settings.Title"),
            ("--- UI ---/Menus/SettingsPanel/Card/Sidebar/AudioTabBtn/Text",                   "UI", "UI.Settings.TabAudio"),
            ("--- UI ---/Menus/SettingsPanel/Card/Sidebar/DisplayTabBtn/Text",                 "UI", "UI.Settings.TabDisplay"),
            ("--- UI ---/Menus/SettingsPanel/Card/Sidebar/GameplayTabBtn/Text",                "UI", "UI.Settings.TabGameplay"),
            ("--- UI ---/Menus/SettingsPanel/Card/Sidebar/CustomTabBtn/Text",                  "UI", "UI.Settings.TabCustom"),
            ("--- UI ---/Menus/SettingsPanel/Card/Header/ResetDefaultsBtn/Text",               "UI", "UI.Settings.ResetDefaultsBtn"),
            ("--- UI ---/Menus/SettingsPanel/Card/Header/CloseBtn/Text",                       "UI", "UI.Settings.CloseBtn"),
            ("--- UI ---/Menus/SettingsPanel/Card/ContentArea/-AudioPanel/MasterRow/Label",    "UI", "UI.Settings.Audio.Master"),
            ("--- UI ---/Menus/SettingsPanel/Card/ContentArea/-AudioPanel/SFXRow/Label",       "UI", "UI.Settings.Audio.SFX"),
            ("--- UI ---/Menus/SettingsPanel/Card/ContentArea/-AudioPanel/BGMRow/Label",       "UI", "UI.Settings.Audio.BGM"),
            ("--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel/FullscreenRow/Label",  "UI", "UI.Settings.Display.Fullscreen"),
            ("--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel/CRTRow/Label",         "UI", "UI.Settings.Display.CRTFilter"),
            ("--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel/ResolutionRow/Label",  "UI", "UI.Settings.Display.Resolution"),
            ("--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel/ScreenShakeRow/Label", "UI", "UI.Settings.Display.ScreenShake"),
            ("--- UI ---/Menus/SettingsPanel/Card/ContentArea/-DisplayPanel/ShowPinyinRow/Label",  "UI", "UI.Settings.Display.ShowPinyin"),
            ("--- UI ---/Menus/SettingsPanel/Card/ContentArea/-GameplayPanel/FullscreenRow/UsernameLabel", "UI", "UI.Settings.Gameplay.Username"),

            // Word list panel
            ("--- UI ---/Menus/WordListPanel/MyListPanelBtns/SwapBtn/Text (TMP)", "UI", "UI.WordList.SwapBtn"),
            ("--- UI ---/Menus/WordListPanel/MyListPanelBtns/UpBtn/Text (TMP)",   "UI", "UI.WordList.UpBtn"),
            ("--- UI ---/Menus/WordListPanel/MyListPanelBtns/DownBtn/Text (TMP)", "UI", "UI.WordList.DownBtn"),
            ("--- UI ---/Menus/WordListPanel/MyListPanelBtns/DelBtn/Text (TMP)",  "UI", "UI.WordList.DeleteBtn"),
            ("--- UI ---/Menus/WordListPanel/MyListPanelBtns/AddBtn/Text (TMP)",  "UI", "UI.WordList.AddBtn"),

            // Daily picker
            ("--- UI ---/Menus/DailyPickerPanel/Card/Title",       "UI", "UI.DailyPicker.Title"),
            ("--- UI ---/Menus/DailyPickerPanel/Card/LabelDates",  "UI", "UI.DailyPicker.LabelDates"),
            ("--- UI ---/Menus/DailyPickerPanel/Card/LabelWords",  "UI", "UI.DailyPicker.LabelWords"),
            ("--- UI ---/Menus/DailyPickerPanel/Card/LoadBtn/Text","UI", "UI.DailyPicker.LoadBtn"),
            ("--- UI ---/Menus/DailyPickerPanel/Card/CloseBtn/Text","UI", "UI.DailyPicker.CloseBtn"),

            // Pinyin popup
            ("--- UI ---/Menus/ChinesePinyinPopup/Card/Title",                   "UI", "UI.PinyinPopup.Title"),
            ("--- UI ---/Menus/ChinesePinyinPopup/Card/ButtonRow/CancelBtn/Text","UI", "UI.PinyinPopup.CancelBtn"),
            ("--- UI ---/Menus/ChinesePinyinPopup/Card/ButtonRow/OKBtn/Text",    "UI", "UI.PinyinPopup.OKBtn"),

            // Level panel
            ("--- UI ---/Menus/LevelPanel/Card/Title",                                    "UI", "UI.Level.Title"),
            ("--- UI ---/Menus/LevelPanel/Card/TabRows/TabBtn1/TabNameTMP",               "UI", "UI.Level.TabChallenges"),
            ("--- UI ---/Menus/LevelPanel/Card/TabRows/TabBtn1 (1)/TabNameTMP",           "UI", "UI.Level.TabCustom"),
            ("--- UI ---/Menus/LevelPanel/Card/TabRows/TabBtn1 - Community/TabNameTMP",   "UI", "UI.Level.TabCommunity"),
            ("--- UI ---/Menus/LevelPanel/Card/ButtonRow/CancelBtn/Text",                 "UI", "UI.Level.CancelBtn"),
            ("--- UI ---/Menus/LevelPanel/Card/ButtonRow/OKBtn/Text",                     "UI", "UI.Level.OKBtn"),
            ("--- UI ---/Menus/LevelPanel/Card/ButtonRow/ImportBtn/Text",                 "UI", "UI.Level.ImportBtn"),
            ("--- UI ---/Menus/LevelPanel/Card/ButtonRow/ExportBtn/Text",                 "UI", "UI.Level.ExportBtn"),
            ("--- UI ---/Menus/LevelPanel/Card/ButtonRow/CreateListBtn/Text",             "UI", "UI.Level.CreateListBtn"),
            ("--- UI ---/Menus/LevelPanel/Card/ButtonRow/DeleteListBtn/Text",             "UI", "UI.Level.DeleteListBtn"),
        };

        foreach (var (path, table, key) in uiMappings)
        {
            var go = FindInScene(path);
            if (go == null) { Debug.LogWarning($"[LocalizeWire] Not found: {path}"); continue; }
            WireLocalizeText(go, table, key);
            wired++;
        }

        // ── InputField placeholders ───────────────────────────────────────────

        var placeholderMappings = new (string path, string table, string key)[]
        {
            ("--- UI ---/Menus/WordListPanel/MyListPanelBtns/InputField (TMP)", "UI", "UI.WordList.PhasePlaceholder"),
            ("--- UI ---/Menus/DailyPickerPanel/Card/SearchField",              "UI", "UI.DailyPicker.SearchPlaceholder"),
        };

        foreach (var (path, table, key) in placeholderMappings)
        {
            var go = FindInScene(path);
            if (go == null) { Debug.LogWarning($"[LocalizeWire] InputField not found: {path}"); continue; }
            WireLocalizePlaceholder(go, table, key);
            wired++;
        }

        // ── LocalizationBootstrapper on GameSystems ───────────────────────────

        var gameSystems = FindInScene("GameSystems");
        if (gameSystems != null)
        {
            if (gameSystems.GetComponent<LocalizationBootstrapper>() == null)
                gameSystems.AddComponent<LocalizationBootstrapper>();
            EditorUtility.SetDirty(gameSystems);
            Debug.Log("[LocalizeWire] Added LocalizationBootstrapper to GameSystems.");
        }
        else Debug.LogWarning("[LocalizeWire] GameSystems not found.");

        // ── Save scene ────────────────────────────────────────────────────────

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[LocalizeWire] DONE. Wired {wired} objects.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static void WireLocalizeText(GameObject go, string table, string key)
    {
        // Remove all old instances to avoid duplicates on re-run
        var oldList = go.GetComponents<LocalizeText>();
        foreach (var old in oldList) Object.DestroyImmediate(old);

        var lt = go.AddComponent<LocalizeText>();
        lt.localizedString = new LocalizedString(table, key);
        EditorUtility.SetDirty(go);
    }

    static void WireLocalizePlaceholder(GameObject go, string table, string key)
    {
        var old = go.GetComponent<LocalizePlaceholder>();
        if (old != null) Object.DestroyImmediate(old);

        var lp = go.AddComponent<LocalizePlaceholder>();
        // Access serialized field via reflection to set the LocalizedString
        var field = typeof(LocalizePlaceholder).GetField("localizedString",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null) field.SetValue(lp, new LocalizedString(table, key));
        EditorUtility.SetDirty(go);
    }

    /// <summary>Finds a GameObject by path, including inactive objects.</summary>
    static GameObject FindInScene(string path)
    {
        var parts = path.Split('/');
        var scene = SceneManager.GetActiveScene();

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            if (parts.Length == 1) return root;

            Transform t = root.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                Transform found = null;
                for (int c = 0; c < t.childCount; c++)
                {
                    if (t.GetChild(c).name == parts[i]) { found = t.GetChild(c); break; }
                }
                if (found == null) return null;
                t = found;
            }
            return t.gameObject;
        }
        return null;
    }
}
#endif
