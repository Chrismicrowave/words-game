#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

/// <summary>
/// One-time setup: creates UI and Gameplay string tables with EN + ZH-Hans entries.
/// Run via Tools > Words > Setup Localization Tables.
/// Safe to re-run — existing entries are overwritten with the values here.
/// </summary>
public class SetupLocalizationTables
{
    [MenuItem("Tools/Words/Setup Localization Tables")]
    public static void Execute()
    {
        // ── Folder structure ──────────────────────────────────────────────────
        if (!AssetDatabase.IsValidFolder("Assets/Localization"))
            AssetDatabase.CreateFolder("Assets", "Localization");
        if (!AssetDatabase.IsValidFolder("Assets/Localization/StringTables"))
            AssetDatabase.CreateFolder("Assets/Localization", "StringTables");

        // ── LocalizationSettings ──────────────────────────────────────────────
        var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings, "Assets/Localization/LocalizationSettings.asset");
            LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            Debug.Log("[Localization Setup] Created LocalizationSettings.asset");
        }

        // ── Locales ───────────────────────────────────────────────────────────
        EnsureLocale("en",      "English");
        EnsureLocale("zh-Hans", "Chinese (Simplified)");

        // ── String tables ─────────────────────────────────────────────────────
        SetupUITable();
        SetupGameplayTable();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Localization Setup] DONE. Check Console for any errors.");
    }

    // ── Locale helpers ────────────────────────────────────────────────────────

    static void EnsureLocale(string code, string displayName)
    {
        foreach (var l in LocalizationEditorSettings.GetLocales())
            if (l.Identifier.Code == code) return;

        var locale = Locale.CreateLocale(new LocaleIdentifier(code));
        locale.name = displayName;
        AssetDatabase.CreateAsset(locale, $"Assets/Localization/{code}.asset");
        LocalizationEditorSettings.AddLocale(locale);
        Debug.Log($"[Localization Setup] Added locale: {code}");
    }

    // ── Table helpers ─────────────────────────────────────────────────────────

    static StringTableCollection GetOrCreateCollection(string name)
    {
        foreach (var c in LocalizationEditorSettings.GetStringTableCollections())
            if (c.TableCollectionName == name) return c;

        var col = LocalizationEditorSettings.CreateStringTableCollection(
            name, "Assets/Localization/StringTables");
        Debug.Log($"[Localization Setup] Created collection: {name}");
        return col;
    }

    static void E(StringTableCollection col, string key, string en, string zh, bool smart = false)
    {
        var enId = new LocaleIdentifier("en");
        var zhId = new LocaleIdentifier("zh-Hans");

        var enTable = col.GetTable(enId) as StringTable;
        var zhTable = col.GetTable(zhId) as StringTable;

        if (enTable != null) { var entry = enTable.AddEntry(key, en); if (smart) entry.IsSmart = true; EditorUtility.SetDirty(enTable); }
        if (zhTable != null) { var entry = zhTable.AddEntry(key, zh); if (smart) entry.IsSmart = true; EditorUtility.SetDirty(zhTable); }
    }

    // ── UI string table ───────────────────────────────────────────────────────

    static void SetupUITable()
    {
        var col = GetOrCreateCollection("UI");

        // HUD
        E(col, "UI.HUD.WordsBtn",    "Words",    "词语");
        E(col, "UI.HUD.TimerBtn",    "Timer",    "计时器");
        E(col, "UI.HUD.InfoBtn",     "Info",     "说明");
        E(col, "UI.HUD.ResetBtn",    "Reset",    "重置");
        E(col, "UI.HUD.SettingsBtn", "Settings", "设置");
        E(col, "UI.HUD.CloseBtn",    "Close",    "关闭");

        // Settings panel
        E(col, "UI.Settings.Title",           "Settings",       "设置");
        E(col, "UI.Settings.TabAudio",        "Audio",          "音频");
        E(col, "UI.Settings.TabDisplay",      "Display",        "显示");
        E(col, "UI.Settings.TabGameplay",     "Gameplay",       "玩法");
        E(col, "UI.Settings.TabCustom",       "Custom",         "自定义");
        E(col, "UI.Settings.ResetDefaultsBtn","Reset Defaults", "恢复默认");
        E(col, "UI.Settings.CloseBtn",        "Close",          "关闭");

        // Audio settings
        E(col, "UI.Settings.Audio.Master", "Master", "总音量");
        E(col, "UI.Settings.Audio.SFX",    "SFX",    "音效");
        E(col, "UI.Settings.Audio.BGM",    "BGM",    "背景音乐");

        // Display settings
        E(col, "UI.Settings.Display.Fullscreen",  "Fullscreen",   "全屏");
        E(col, "UI.Settings.Display.CRTFilter",   "CRT Filter",   "CRT滤镜");
        E(col, "UI.Settings.Display.Resolution",  "Resolution",   "分辨率");
        E(col, "UI.Settings.Display.ScreenShake", "Screen Shake", "屏幕震动");
        E(col, "UI.Settings.Display.ShowPinyin",  "Show Pinyin",  "显示拼音");
        E(col, "UI.Settings.Display.Language",    "Language",     "语言");

        // Word list panel
        E(col, "UI.WordList.TabMyList",        "My List",         "我的列表");
        E(col, "UI.WordList.TabDaily",         "Daily",           "每日");
        E(col, "UI.WordList.SwapBtn",          "Swap",            "切换");
        E(col, "UI.WordList.UpBtn",            "Up",              "上移");
        E(col, "UI.WordList.DownBtn",          "Down",            "下移");
        E(col, "UI.WordList.DeleteBtn",        "Delete",          "删除");
        E(col, "UI.WordList.AddBtn",           "Add",             "添加");
        E(col, "UI.WordList.ImportBtn",        "Import",          "导入");
        E(col, "UI.WordList.ExportBtn",        "Export",          "导出");
        E(col, "UI.WordList.FetchDailyBtn",    "Fetch Daily",     "获取每日单词");
        E(col, "UI.WordList.PhasePlaceholder", "Enter a phase...", "输入词语...");

        // Daily picker panel
        E(col, "UI.DailyPicker.Title",             "Daily Word List", "每日单词列表");
        E(col, "UI.DailyPicker.LabelDates",        "Dates",           "日期");
        E(col, "UI.DailyPicker.LabelWords",        "Words",           "单词");
        E(col, "UI.DailyPicker.LoadBtn",           "Load",            "加载");
        E(col, "UI.DailyPicker.CloseBtn",          "Close",           "关闭");
        E(col, "UI.DailyPicker.SearchPlaceholder", "Search...",       "搜索...");

        // Pinyin popup
        E(col, "UI.PinyinPopup.Title",     "Confirm Pinyin", "确认拼音");
        E(col, "UI.PinyinPopup.CancelBtn", "Cancel",         "取消");
        E(col, "UI.PinyinPopup.OKBtn",     "OK",             "确认");

        // Instructions
        E(col, "UI.Instructions.Body",
            "Rules:\n1. Hold the letter one by one and don't let go\n2. If you see the same letter when holding, release it\n\n(Ignore cases, space and punctuations)\n(use props, extra hands, whatever you need:) )",
            "规则：\n1. 依次按住每个字母，不要松开\n2. 如果在按住时看到相同的字母，则松开它\n\n（忽略大小写、空格和标点符号）\n（可以使用工具、多余的手，一切你需要的东西 :) ）");

        EditorUtility.SetDirty(col);
        Debug.Log("[Localization Setup] UI table populated.");
    }

    // ── Gameplay string table ─────────────────────────────────────────────────

    static void SetupGameplayTable()
    {
        var col = GetOrCreateCollection("Gameplay");

        E(col, "Gameplay.PhaseComplete",
            "Phase complete! Hit Return to continue...",
            "阶段完成！按回车键继续……");

        E(col, "Gameplay.AllPhasesComplete",
            "Congratulations! You completed all phases!",
            "恭喜！您已完成所有阶段！");

        E(col, "Gameplay.FailureSuffix",
            ". Press Backspace to start again",
            "。按退格键重新开始");

        // Smart strings — {action}, {key}, {got} are Smartformat.NET placeholders
        E(col, "Gameplay.ExpectedTo",
            "Expected to {action} '{key}', but got '{got}'",
            "期望{action}\u201c{key}\u201d，但你按了\u201c{got}\u201d",
            smart: true);

        E(col, "Gameplay.ActionPrompt",
            "{action} '{key}'",
            "{action}\u201c{key}\u201d",
            smart: true);

        // Verb forms
        E(col, "Gameplay.VerbHold",        "Hold",    "按住");
        E(col, "Gameplay.VerbRelease",     "Release", "松开");
        E(col, "Gameplay.VerbHoldLower",   "hold",    "按住");
        E(col, "Gameplay.VerbReleaseLower","release", "松开");

        EditorUtility.SetDirty(col);
        Debug.Log("[Localization Setup] Gameplay table populated.");
    }
}
#endif
