using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

[TestFixture]
public sealed class WordListLibraryServiceTests
{
    private string testRoot;
    private string customDirectory;
    private string sidecarPath;
    private WordListLibraryService service;

    [SetUp]
    public void SetUp()
    {
        testRoot = Path.Combine(Path.GetTempPath(), "StickyWordsLibraryTests-" + Guid.NewGuid().ToString("N"));
        customDirectory = Path.Combine(testRoot, "Levels", "Custom");
        sidecarPath = Path.Combine(testRoot, "WordListLibrary.json");
        Directory.CreateDirectory(customDirectory);
        service = NewService();
    }

    [TearDown]
    public void TearDown()
    {
        if (!string.IsNullOrEmpty(testRoot)
            && Path.GetFileName(testRoot).StartsWith("StickyWordsLibraryTests-", StringComparison.Ordinal)
            && Directory.Exists(testRoot))
            Directory.Delete(testRoot, true);
    }

    [Test]
    public void CreateMetadataEditAndContentEditKeepStableIdentityAndRevisionBoundaries()
    {
        Assert.IsTrue(service.TryCreateList("First", English("one", "two"), out WordListLibraryItem created, out string error), error);
        string originalId = created.LocalListId;
        string originalPath = created.FilePath;
        string originalHash = created.ContentHash;

        Assert.IsTrue(service.TryRecordRun(created.Provider, 24f, 2, 0, 3, out error), error);
        Assert.IsTrue(service.TryUpdateMetadata(originalId, "Renamed", "note", new[] { "Study", "study" }, true,
            out WordListLibraryItem metadataOnly, out error), error);

        Assert.AreEqual(originalId, metadataOnly.LocalListId);
        Assert.AreEqual(originalPath, metadataOnly.FilePath);
        Assert.AreEqual(1, metadataOnly.ContentRevision);
        Assert.AreEqual(originalHash, metadataOnly.ContentHash);
        Assert.AreEqual(1, metadataOnly.Tags.Count);
        Assert.IsTrue(metadataOnly.IsFavorite);
        Assert.IsTrue(metadataOnly.CurrentRecord.HasRecord);

        WordListLibraryContent edited = English("one", "three");
        int changeEvents = 0;
        service.ContentChanged += _ => changeEvents++;
        Assert.IsTrue(service.TrySaveListEdit(originalId, "Renamed", "note", new[] { "Study" }, true,
            edited, out WordListLibraryItem revised, out error), error);
        Assert.AreEqual(2, revised.ContentRevision);
        Assert.AreEqual(1, revised.ArchivedRecords.Count);
        Assert.IsFalse(revised.CurrentRecord.HasRecord);
        Assert.AreEqual(1, changeEvents);

        Assert.IsTrue(service.TrySaveListEdit(originalId, "Renamed", "note", new[] { "Study" }, true,
            edited, out WordListLibraryItem repeatedSave, out error), error);
        Assert.AreEqual(2, repeatedSave.ContentRevision);
        Assert.AreEqual(1, repeatedSave.ArchivedRecords.Count);
        Assert.AreEqual(1, changeEvents);
    }

    [Test]
    public void ChineseAndMixedContentRoundTripWithPinyinAndDeepCopies()
    {
        var chinese = new WordListLibraryContent
        {
            LanguageMode = LanguageMode.Chinese,
            Words = new List<string> { "你好" },
            ChineseWords = new List<ChineseWordEntry>
            {
                new ChineseWordEntry
                {
                    display = "你好",
                    entries = new List<ChinesePhaseEntry>
                    {
                        new ChinesePhaseEntry { character = "你", pinyin = "ni" },
                        new ChinesePhaseEntry { character = "好", pinyin = "hao" }
                    }
                }
            }
        };
        Assert.IsTrue(service.TryCreateList("Chinese", chinese, out WordListLibraryItem chineseItem, out string error), error);
        var reloadedChinese = new LevelWordListProvider(chineseItem.FilePath, true);
        Assert.AreEqual(LanguageMode.Chinese, reloadedChinese.LanguageMode);
        Assert.AreEqual("hao", reloadedChinese.GetChineseWords()[0].entries[1].pinyin);

        var mixed = new WordListLibraryContent
        {
            LanguageMode = LanguageMode.Mixed,
            Words = new List<string> { "go你好!" },
            MixedWords = new List<MixedWordEntry>
            {
                new MixedWordEntry
                {
                    segments = new List<MixedSegmentData>
                    {
                        new MixedSegmentData { type = "english", text = "go" },
                        new MixedSegmentData
                        {
                            type = "chinese",
                            entries = new List<ChinesePhaseEntry>
                            {
                                new ChinesePhaseEntry { character = "你", pinyin = "ni" },
                                new ChinesePhaseEntry { character = "好", pinyin = "hao" }
                            }
                        },
                        new MixedSegmentData { type = "english", text = "!" }
                    }
                }
            }
        };
        Assert.IsTrue(service.TryCreateList("Mixed", mixed, out WordListLibraryItem mixedItem, out error), error);
        var reloadedMixed = new LevelWordListProvider(mixedItem.FilePath, true);
        Assert.AreEqual(LanguageMode.Mixed, reloadedMixed.LanguageMode);
        Assert.AreEqual("ni", reloadedMixed.GetMixedWords()[0].segments[1].entries[0].pinyin);

        mixedItem.Content.MixedWords[0].segments[1].entries[0].pinyin = "bad";
        var detachedReload = new LevelWordListProvider(mixedItem.FilePath, true);
        Assert.AreEqual("ni", detachedReload.GetMixedWords()[0].segments[1].entries[0].pinyin);
    }

    [Test]
    public void DuplicateProviderPreservesLegacyStructuredContentWithoutChangingSource()
    {
        string sourcePath = Path.Combine(testRoot, "readonly-source.txt");
        string sourceJson = "{\"name\":\"Old\",\"languageMode\":\"Mixed\",\"words\":[\"中国\"],"
            + "\"mixedWords\":[{\"segments\":[{\"type\":\"chinese\",\"entries\":["
            + "{\"character\":\"中\",\"pinyin\":\"\"},{\"character\":\"国\",\"pinyin\":\"guo\"}]}]}]}";
        File.WriteAllText(sourcePath, sourceJson);
        byte[] originalBytes = File.ReadAllBytes(sourcePath);
        var readonlySource = new LevelWordListProvider(sourcePath, false);

        Assert.IsTrue(service.TryDuplicateProvider(readonlySource, out WordListLibraryItem copy, out string error), error);
        Assert.AreNotEqual(readonlySource.GetListKey(), copy.LocalListId);
        Assert.AreEqual(string.Empty, copy.Content.MixedWords[0].segments[0].entries[0].pinyin);
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(sourcePath));
        Assert.IsFalse(readonlySource.TrySave(out _));
    }

    [Test]
    public void MissingCustomIdentityIsPersistedAndDuplicateShortIdsDoNotMergeContent()
    {
        string legacy = Path.Combine(customDirectory, "legacy.txt");
        File.WriteAllText(legacy, "alpha\nbeta");
        var migrated = new LevelWordListProvider(legacy, true);
        Assert.AreEqual(32, migrated.GetUUID().Length);
        StringAssert.Contains("// u:" + migrated.GetUUID(), File.ReadAllText(legacy));

        string first = Path.Combine(customDirectory, "a.txt");
        string second = Path.Combine(customDirectory, "b.txt");
        File.WriteAllText(first, "// u:abcdef123456\nfirst");
        File.WriteAllText(second, "// u:abcdef123456\nsecond");
        IReadOnlyList<WordListLibraryItem> all = service.GetAll();
        var collisionEntries = all.Where(item => item.FilePath == first || item.FilePath == second).ToList();
        Assert.AreEqual(2, collisionEntries.Count);
        Assert.AreEqual(2, collisionEntries.Select(item => item.LocalListId).Distinct().Count());
        Assert.AreEqual("first", new LevelWordListProvider(first, true).GetWords()[0]);
        Assert.AreEqual("second", new LevelWordListProvider(second, true).GetWords()[0]);
    }

    [Test]
    public void UnknownExplicitLanguageDoesNotOverwriteSourceFile()
    {
        string path = Path.Combine(customDirectory, "invalid-language.txt");
        string source = "{\"name\":\"bad\",\"languageMode\":\"NotARealMode\",\"words\":[\"safe\"]}";
        File.WriteAllText(path, source);
        byte[] before = File.ReadAllBytes(path);
        var provider = new LevelWordListProvider(path, true);

        Assert.IsFalse(provider.IsValid);
        Assert.IsFalse(provider.TrySave(out _));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
    }

    [Test]
    public void CodecRequiresPinyinForDisplayHanCountAndRebuildsIncompleteLegacyMapping()
    {
        var legacyChinese = new WordListLibraryContent
        {
            LanguageMode = LanguageMode.Chinese,
            Words = new List<string> { "中国" },
            ChineseWords = new List<ChineseWordEntry>
            {
                new ChineseWordEntry
                {
                    display = "中国",
                    entries = new List<ChinesePhaseEntry> { new ChinesePhaseEntry { character = "中", pinyin = "zhong" } }
                }
            }
        };
        Assert.IsFalse(WordListLibraryContentCodec.TryParseEditorLines(new[] { "中国 | zhong" },
            LanguageMode.Chinese, legacyChinese, out _, out _));
        Assert.IsTrue(WordListLibraryContentCodec.TryParseEditorLines(new[] { "中国 | zhong guo" },
            LanguageMode.Chinese, legacyChinese, out WordListLibraryContent correctedChinese, out string error), error);
        Assert.AreEqual(2, correctedChinese.ChineseWords[0].entries.Count);
        Assert.AreEqual("guo", correctedChinese.ChineseWords[0].entries[1].pinyin);

        var legacyMixed = new WordListLibraryContent
        {
            LanguageMode = LanguageMode.Mixed,
            Words = new List<string> { "A中国" },
            MixedWords = new List<MixedWordEntry>
            {
                new MixedWordEntry
                {
                    segments = new List<MixedSegmentData>
                    {
                        new MixedSegmentData { type = "english", text = "A" },
                        new MixedSegmentData
                        {
                            type = "chinese",
                            entries = new List<ChinesePhaseEntry> { new ChinesePhaseEntry { character = "中", pinyin = "zhong" } }
                        }
                    }
                }
            }
        };
        Assert.IsFalse(WordListLibraryContentCodec.TryParseEditorLines(new[] { "A中国 | zhong" },
            LanguageMode.Mixed, legacyMixed, out _, out _));
        Assert.IsTrue(WordListLibraryContentCodec.TryParseEditorLines(new[] { "A中国 | zhong guo" },
            LanguageMode.Mixed, legacyMixed, out WordListLibraryContent correctedMixed, out error), error);
        Assert.AreEqual("A", correctedMixed.MixedWords[0].segments[0].text);
        Assert.AreEqual(2, correctedMixed.MixedWords[0].segments[1].entries.Count);
    }

    [Test]
    public void StrictChineseWritesRequireCompleteMatchingPinyinAndMetadataOnlyKeepsLegacyFile()
    {
        string path = Path.Combine(customDirectory, "incomplete.txt");
        string source = "{\"name\":\"legacy\",\"languageMode\":\"Chinese\",\"words\":[\"中国\"],"
            + "\"chineseWords\":[{\"display\":\"中国\",\"entries\":[{\"character\":\"中\",\"pinyin\":\"zhong\"}]}]}";
        File.WriteAllText(path, source);
        var legacyProvider = new LevelWordListProvider(path, true);
        byte[] legacyBytes = File.ReadAllBytes(path); // identity migration is complete before metadata-only assertion
        string originalId = legacyProvider.GetListKey();
        Assert.IsTrue(service.TryUpdateMetadata(originalId, "legacy updated", "", Array.Empty<string>(), false,
            out _, out string error), error);
        CollectionAssert.AreEqual(legacyBytes, File.ReadAllBytes(path));

        var invalid = new WordListLibraryContent
        {
            LanguageMode = LanguageMode.Chinese,
            Words = new List<string> { "中国" },
            ChineseWords = new List<ChineseWordEntry>
            {
                new ChineseWordEntry { display = "中国", entries = new List<ChinesePhaseEntry> { new ChinesePhaseEntry { character = "中", pinyin = "zhong" } } }
            }
        };
        Assert.IsFalse(service.TrySaveListEdit(originalId, "legacy updated", "", Array.Empty<string>(), false,
            invalid, out _, out error));
        StringAssert.Contains("Chinese word 1", error);
    }

    [Test]
    public void LatestBestAndHighestStarsAreCoherentAndScopedToCurrentRevision()
    {
        Assert.IsTrue(service.TryCreateList("Runs", English("A"), out WordListLibraryItem item, out string error), error);
        Assert.IsTrue(service.TryRecordRun(item.Provider, 24f, 1, 0, 3, out error), error);
        Assert.IsTrue(service.TryRecordRun(item.Provider, 30f, 1, 7, 1, out error), error);
        WordListLibraryItem twoRuns = service.FindById(item.LocalListId);
        Assert.AreEqual(30f, twoRuns.CurrentRecord.latest.totalTimeSeconds);
        Assert.AreEqual(24f, twoRuns.CurrentRecord.best.totalTimeSeconds);
        Assert.AreEqual(0, twoRuns.CurrentRecord.best.errors);
        Assert.AreEqual(3, twoRuns.CurrentRecord.highestStars);

        Assert.IsTrue(service.TryRecordRun(item.Provider, 20f, 1, 9, 0, out error), error);
        WordListLibraryItem fastest = service.FindById(item.LocalListId);
        Assert.AreEqual(20f, fastest.CurrentRecord.best.totalTimeSeconds);
        Assert.AreEqual(9, fastest.CurrentRecord.best.errors);
        Assert.AreEqual(0, fastest.CurrentRecord.best.stars);
        Assert.AreEqual(3, fastest.CurrentRecord.highestStars);
    }

    [Test]
    public void MissingPrimaryRecoversFromBackupAndWritesFreshPrimary()
    {
        Assert.IsTrue(service.TryCreateList("Recovery", English("recover"), out WordListLibraryItem item, out string error), error);
        File.Copy(sidecarPath, sidecarPath + ".bak", true);
        File.Delete(sidecarPath);

        var recovered = NewService();
        Assert.IsTrue(recovered.SidecarLoadError.StartsWith("Recovered Library metadata", StringComparison.Ordinal));
        Assert.IsTrue(recovered.TryUpdateMetadata(item.LocalListId, "Recovered", "", Array.Empty<string>(), false,
            out WordListLibraryItem restored, out error), error);
        Assert.AreEqual("Recovered", restored.Title);
        Assert.IsTrue(File.Exists(sidecarPath));
        Assert.IsTrue(File.Exists(sidecarPath + ".bak"));
    }

    [Test]
    public void FailedRecordPersistenceDoesNotLeakIntoLaterSidecarSave()
    {
        Assert.IsTrue(service.TryCreateList("Failure", English("one"), out WordListLibraryItem item, out string error), error);
        using (var lockFile = new FileStream(sidecarPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.IsFalse(service.TryRecordRun(item.Provider, 10f, 1, 0, 3, out error));

        Assert.IsTrue(service.TryUpdateMetadata(item.LocalListId, "Failure saved", "", Array.Empty<string>(), false,
            out _, out error), error);
        var afterRetry = NewService().FindById(item.LocalListId);
        Assert.IsNotNull(afterRetry);
        Assert.IsFalse(afterRetry.CurrentRecord.HasRecord);
        Assert.AreEqual(0, afterRetry.PlayCount);
    }

    private WordListLibraryService NewService()
    {
        return new WordListLibraryService(customDirectory, sidecarPath, () => new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
    }

    private static WordListLibraryContent English(params string[] words)
    {
        return new WordListLibraryContent
        {
            LanguageMode = LanguageMode.English,
            Words = words.ToList(),
            ChineseWords = new List<ChineseWordEntry>(),
            MixedWords = new List<MixedWordEntry>()
        };
    }
}
