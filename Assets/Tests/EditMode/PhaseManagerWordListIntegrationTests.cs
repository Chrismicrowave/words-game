using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class PhaseManagerWordListIntegrationTests
{
    private string testRoot;
    private string customDirectory;
    private WordListLibraryService library;
    private GameObject phaseManagerObject;
    private GameObject timerObject;
    private GameObject stateObject;
    private PhaseManager phaseManager;
    private TimerSystem timer;
    private GameStateManager gameState;
    private GameConfig config;
    private GameConfigProfile profile;
    private Action wordListChangedHandler;
    private float timerClock;
    private PhaseManager previousPhaseManager;
    private TimerSystem previousTimer;
    private GameStateManager previousGameState;

    [SetUp]
    public void SetUp()
    {
        testRoot = Path.Combine(Path.GetTempPath(), "StickyWordsPhaseManagerTests-" + Guid.NewGuid().ToString("N"));
        customDirectory = Path.Combine(testRoot, "Levels", "Custom");
        Directory.CreateDirectory(customDirectory);
        library = new WordListLibraryService(customDirectory, Path.Combine(testRoot, "Library.json"));
        previousPhaseManager = Services.Get<PhaseManager>();
        previousTimer = Services.Get<TimerSystem>();
        previousGameState = Services.Get<GameStateManager>();
        Services.Register<PhaseManager>(null);
        Services.Register<TimerSystem>(null);
        Services.Register<GameStateManager>(null);
    }

    [TearDown]
    public void TearDown()
    {
        if (phaseManager != null && wordListChangedHandler != null)
            phaseManager.OnWordListChanged -= wordListChangedHandler;
        if (phaseManagerObject != null) UnityEngine.Object.DestroyImmediate(phaseManagerObject);
        if (timerObject != null) UnityEngine.Object.DestroyImmediate(timerObject);
        if (stateObject != null) UnityEngine.Object.DestroyImmediate(stateObject);
        if (config != null) UnityEngine.Object.DestroyImmediate(config);
        if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
        Services.Register<PhaseManager>(previousPhaseManager);
        Services.Register<TimerSystem>(previousTimer);
        Services.Register<GameStateManager>(previousGameState);
        if (!string.IsNullOrEmpty(testRoot)
            && Path.GetFileName(testRoot).StartsWith("StickyWordsPhaseManagerTests-", StringComparison.Ordinal)
            && Directory.Exists(testRoot))
            Directory.Delete(testRoot, true);
    }

    [Test]
    public void CustomMutationCommitsBeforeSingleChangeEventAndResetListenerRuns()
    {
        profile = ScriptableObject.CreateInstance<GameConfigProfile>();
        profile.unlockAllFeatures = true;
        profile.maxWordsPerList = 0;
        config = ScriptableObject.CreateInstance<GameConfig>();
        config.activeProfile = profile;
        string path = WriteCustom("active.txt", "one", "two");
        var provider = new LevelWordListProvider(path, true);
        CreateTimer();
        CreateStateManager();
        CreatePhaseManager(config);
        Assert.IsTrue(phaseManager.LoadWordList(provider));
        wordListChangedHandler = () => timer.ResetAll();
        phaseManager.OnWordListChanged += wordListChangedHandler;

        int notifications = 0;
        bool fileAlreadyCommittedAtNotification = false;
        phaseManager.OnWordListChanged += () =>
        {
            notifications++;
            var saved = new LevelWordListProvider(path, true);
            fileAlreadyCommittedAtNotification = saved.GetWords().Contains("three");
        };

        Assert.IsTrue(phaseManager.AdvancePhase());
        phaseManager.RecordFailure();
        timer.StartTimer();
        timerClock = 2f;
        typeof(TimerSystem).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(timer, null);
        Assert.IsTrue(timer.IsRunning);
        Assert.IsTrue(phaseManager.AddPhase("three", 0));

        Assert.AreEqual(1, notifications);
        Assert.IsTrue(fileAlreadyCommittedAtNotification);
        Assert.AreEqual(0, phaseManager.CurrentPhaseIndex);
        Assert.AreEqual(0, phaseManager.TotalErrors);
        Assert.IsFalse(timer.IsRunning);
        Assert.AreEqual(0f, timer.CurrentPhaseDuration);
        Assert.AreEqual(0f, timer.TotalElapsedTime);

        WordListLibraryItem afterRescan = library.FindById(provider.GetListKey());
        Assert.IsNotNull(afterRescan);
        Assert.AreEqual(2, afterRescan.ContentRevision);
        Assert.IsTrue(library.TryRecordRun(phaseManager.ActiveProvider as LevelWordListProvider,
            12f, phaseManager.TotalPhases, 0, 3, out string error), error);
        WordListLibraryItem afterRun = library.FindById(provider.GetListKey());
        Assert.AreEqual(2, afterRun.ContentRevision);
        Assert.AreEqual(2, afterRun.CurrentRecord.contentRevision);
        Assert.AreEqual(0, afterRun.ArchivedRecords.Count);
    }

    [Test]
    public void ReadOnlyAndProfileLockedProvidersRejectAllPhaseMutations()
    {
        profile = ScriptableObject.CreateInstance<GameConfigProfile>();
        profile.allowChineseAndMixed = true;
        profile.ignoreProgressionLocks = false;
        profile.customUnlockLevels = int.MaxValue;
        config = ScriptableObject.CreateInstance<GameConfig>();
        config.activeProfile = profile;
        CreatePhaseManager(config);

        string customPath = WriteCustom("locked.txt", "first", "second");
        var customProvider = new LevelWordListProvider(customPath, true);
        Assert.IsTrue(phaseManager.LoadWordList(customProvider));
        byte[] customBefore = File.ReadAllBytes(customPath);
        Assert.IsFalse(phaseManager.AddPhase("blocked"));
        Assert.IsFalse(phaseManager.RemovePhase(0));
        Assert.IsFalse(phaseManager.MovePhase(0, 1));
        CollectionAssert.AreEqual(customBefore, File.ReadAllBytes(customPath));

        string readonlyPath = Path.Combine(testRoot, "readonly-source.txt");
        File.WriteAllText(readonlyPath, "// u:" + Guid.NewGuid().ToString("N") + "\nfirst\nsecond");
        byte[] readonlyBefore = File.ReadAllBytes(readonlyPath);
        var readonlyProvider = new LevelWordListProvider(readonlyPath, false);
        Assert.IsTrue(phaseManager.LoadWordList(readonlyProvider));
        Assert.IsFalse(phaseManager.AddPhase("blocked"));
        Assert.IsFalse(phaseManager.RemovePhase(0));
        Assert.IsFalse(phaseManager.MovePhase(0, 1));
        Assert.IsFalse(phaseManager.TrySaveCurrentList(out _));
        CollectionAssert.AreEqual(readonlyBefore, File.ReadAllBytes(readonlyPath));
    }

    [Test]
    public void WordLimitUsesConfiguredCapacityEvenWithProgressionBypass()
    {
        profile = ScriptableObject.CreateInstance<GameConfigProfile>();
        profile.unlockAllFeatures = true;
        profile.maxWordsPerList = 2;
        config = ScriptableObject.CreateInstance<GameConfig>();
        config.activeProfile = profile;
        CreatePhaseManager(config);

        string path = WriteCustom("word-cap.txt", "one", "two");
        Assert.IsTrue(phaseManager.LoadWordList(new LevelWordListProvider(path, true)));
        Assert.IsTrue(phaseManager.HasReachedWordLimit);
        Assert.IsFalse(phaseManager.AddPhase("three"),
            "Progression bypass must not bypass the configured word cap.");
        Assert.IsTrue(phaseManager.RemovePhase(1),
            "The word cap must not prevent reducing an existing list.");
        Assert.IsFalse(phaseManager.HasReachedWordLimit);

        profile.maxWordsPerList = 0;
        Assert.IsFalse(phaseManager.HasReachedWordLimit);
    }

    [Test]
    public void AutoModeNormalizationRetainsChinesePinyinAcrossMixedMutations()
    {
        profile = ScriptableObject.CreateInstance<GameConfigProfile>();
        profile.allowChineseAndMixed = true;
        profile.unlockAllFeatures = true;
        profile.maxWordsPerList = 0;
        config = ScriptableObject.CreateInstance<GameConfig>();
        config.activeProfile = profile;
        string display = "你1，好!";
        string path = WriteChineseCustom("neutral-display.txt", display,
            new ChinesePhaseEntry { character = "你", pinyin = "ni" },
            new ChinesePhaseEntry { character = "好", pinyin = "hao" });
        CreatePhaseManager(config);
        Assert.IsTrue(phaseManager.LoadWordList(new LevelWordListProvider(path, true)));

        int notifications = 0;
        phaseManager.OnWordListChanged += () => notifications++;
        Assert.IsTrue(phaseManager.AddPhase("hello", 0));
        Assert.AreEqual(1, notifications, "One successful mutation emits one content-change event.");
        Assert.AreEqual(LanguageMode.Mixed, phaseManager.CurrentLanguageMode);
        CollectionAssert.AreEqual(new[] { "hello", display }, phaseManager.Words);
        AssertMixedChineseEntry(phaseManager.ActiveProvider.GetMixedWords()[1], display, "ni", "hao");

        Assert.IsTrue(phaseManager.RemovePhase(0));
        Assert.AreEqual(2, notifications, "A second successful mutation emits one additional event.");
        Assert.AreEqual(LanguageMode.Chinese, phaseManager.CurrentLanguageMode);
        CollectionAssert.AreEqual(new[] { display }, phaseManager.Words);
        ChineseWordEntry restored = phaseManager.ActiveProvider.GetChineseWords()[0];
        Assert.AreEqual(display, restored.display);
        CollectionAssert.AreEqual(new[] { "你", "好" }, restored.entries.ConvertAll(entry => entry.character));
        CollectionAssert.AreEqual(new[] { "ni", "hao" }, restored.entries.ConvertAll(entry => entry.pinyin));

        var insertedMixed = new MixedWordEntry
        {
            segments = new List<MixedSegmentData>
            {
                new MixedSegmentData { type = "english", text = "say " },
                new MixedSegmentData
                {
                    type = "chinese",
                    entries = new List<ChinesePhaseEntry>
                    {
                        new ChinesePhaseEntry { character = "好", pinyin = "hao" }
                    }
                }
            }
        };
        Assert.IsTrue(phaseManager.AddMixedPhase(insertedMixed));
        Assert.AreEqual(3, notifications, "AddMixedPhase also publishes one successful content change.");
        Assert.AreEqual(LanguageMode.Mixed, phaseManager.CurrentLanguageMode);
        Assert.AreEqual("say 好", phaseManager.Words[0]);
        AssertMixedChineseEntry(phaseManager.ActiveProvider.GetMixedWords()[1], display, "ni", "hao");
    }

    [Test]
    public void NewHanWithoutPinyinFailsAndRestoresProviderLibraryAndEventState()
    {
        profile = ScriptableObject.CreateInstance<GameConfigProfile>();
        profile.allowChineseAndMixed = true;
        profile.unlockAllFeatures = true;
        profile.maxWordsPerList = 0;
        config = ScriptableObject.CreateInstance<GameConfig>();
        config.activeProfile = profile;
        string path = WriteCustom("missing-pinyin.txt", "one", "two");
        var provider = new LevelWordListProvider(path, true);
        CreatePhaseManager(config);
        Assert.IsTrue(phaseManager.LoadWordList(provider));
        WordListLibraryItem before = library.FindById(provider.GetListKey());
        Assert.IsNotNull(before);
        byte[] fileBefore = File.ReadAllBytes(path);
        int notifications = 0;
        phaseManager.OnWordListChanged += () => notifications++;

        Assert.IsFalse(phaseManager.AddPhase("苹果", 0));

        Assert.AreEqual(0, notifications);
        Assert.AreEqual(LanguageMode.English, phaseManager.CurrentLanguageMode);
        CollectionAssert.AreEqual(new[] { "one", "two" }, phaseManager.Words);
        CollectionAssert.AreEqual(new[] { "one", "two" }, provider.GetWords());
        CollectionAssert.AreEqual(fileBefore, File.ReadAllBytes(path));
        WordListLibraryItem after = library.FindById(provider.GetListKey());
        Assert.IsNotNull(after);
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.ContentHash, after.ContentHash);
        Assert.IsFalse(after.CurrentRecord != null && after.CurrentRecord.HasRecord);
    }

    [Test]
    public void FailedLibraryCommitRestoresPhaseArraysAndDoesNotPublishChange()
    {
        profile = ScriptableObject.CreateInstance<GameConfigProfile>();
        profile.unlockAllFeatures = true;
        profile.maxWordsPerList = 0;
        config = ScriptableObject.CreateInstance<GameConfig>();
        config.activeProfile = profile;
        File.WriteAllText(Path.Combine(testRoot, "Library.json"), "{invalid sidecar");
        library = new WordListLibraryService(customDirectory, Path.Combine(testRoot, "Library.json"));
        string path = WriteCustom("unwritable-sidecar.txt", "one", "two");
        var provider = new LevelWordListProvider(path, true);
        CreatePhaseManager(config);
        Assert.IsTrue(phaseManager.LoadWordList(provider));
        int notifications = 0;
        phaseManager.OnWordListChanged += () => notifications++;
        Assert.IsTrue(phaseManager.AdvancePhase());
        phaseManager.RecordFailure();
        byte[] fileBefore = File.ReadAllBytes(path);

        Assert.IsFalse(phaseManager.AddPhase("not committed", 0));
        Assert.AreEqual(1, phaseManager.CurrentPhaseIndex);
        Assert.AreEqual(1, phaseManager.TotalErrors);
        CollectionAssert.AreEqual(new[] { "one", "two" }, phaseManager.Words);
        CollectionAssert.AreEqual(fileBefore, File.ReadAllBytes(path));
        Assert.AreEqual(0, notifications);
    }

    private void CreatePhaseManager(GameConfig optionalConfig = null)
    {
        phaseManagerObject = new GameObject("PhaseManager isolated test");
        phaseManagerObject.SetActive(false);
        phaseManager = phaseManagerObject.AddComponent<PhaseManager>();
        phaseManager.SetWordListLibraryForTests(library);
        Services.Register<PhaseManager>(phaseManager);
        if (optionalConfig != null)
        {
            FieldInfo field = typeof(PhaseManager).GetField("config", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(phaseManager, optionalConfig);
        }
        phaseManagerObject.SetActive(true);
        Assert.AreSame(phaseManager, Services.Get<PhaseManager>());
    }

    private void CreateTimer()
    {
        timerClock = 0f;
        timerObject = new GameObject("TimerSystem isolated test");
        timer = timerObject.AddComponent<TimerSystem>();
        Services.Register<TimerSystem>(timer);
        timer.SetTimeSourceForTests(() => timerClock);
    }

    private void CreateStateManager()
    {
        stateObject = new GameObject("GameStateManager isolated test");
        gameState = stateObject.AddComponent<GameStateManager>();
        Services.Register<GameStateManager>(gameState);
        Assert.AreSame(gameState, Services.Get<GameStateManager>());
    }

    private string WriteCustom(string fileName, params string[] words)
    {
        string path = Path.Combine(customDirectory, fileName);
        File.WriteAllText(path, "// u:" + Guid.NewGuid().ToString("N") + "\n" + string.Join("\n", words));
        return path;
    }

    private string WriteChineseCustom(string fileName, string display,
        params ChinesePhaseEntry[] entries)
    {
        string path = Path.Combine(customDirectory, fileName);
        var provider = new LevelWordListProvider(path, true);
        provider.SetDisplayName(Path.GetFileNameWithoutExtension(fileName));
        provider.AssignNewUUID();
        provider.SetContent(LanguageMode.Chinese, new List<string> { display },
            new List<ChineseWordEntry>
            {
                new ChineseWordEntry { display = display, entries = new List<ChinesePhaseEntry>(entries) }
            }, null);
        Assert.IsTrue(provider.TrySave(out string error), error);
        return path;
    }

    private static void AssertMixedChineseEntry(MixedWordEntry entry, string expectedDisplay,
        params string[] expectedPinyin)
    {
        Assert.IsNotNull(entry);
        var display = new System.Text.StringBuilder();
        var pinyin = new System.Collections.Generic.List<string>();
        foreach (MixedSegmentData segment in entry.segments)
        {
            if (segment.type == "english")
                display.Append(segment.text);
            else
            {
                foreach (ChinesePhaseEntry phase in segment.entries)
                {
                    display.Append(phase.character);
                    pinyin.Add(phase.pinyin);
                }
            }
        }
        Assert.AreEqual(expectedDisplay, display.ToString());
        CollectionAssert.AreEqual(expectedPinyin, pinyin);
    }
}
