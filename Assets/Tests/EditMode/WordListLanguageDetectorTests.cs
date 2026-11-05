using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public sealed class WordListLanguageDetectorTests
{
    [Test]
    public void DisplayDetectionClassifiesHanLatinAndIgnoresNeutralCharacters()
    {
        Assert.IsTrue(WordListLanguageDetector.TryDetectDisplayLines(
            new[] { "猫！？１２３" }, LanguageMode.English,
            out LanguageMode chinese, out bool usedFallback, out string error), error);
        Assert.AreEqual(LanguageMode.Chinese, chinese);
        Assert.IsFalse(usedFallback);

        Assert.IsTrue(WordListLanguageDetector.TryDetectDisplayLines(
            new[] { "école—Ａ" }, LanguageMode.Chinese,
            out LanguageMode english, out usedFallback, out error), error);
        Assert.AreEqual(LanguageMode.English, english);
        Assert.IsFalse(usedFallback);

        Assert.IsTrue(WordListLanguageDetector.TryDetectDisplayLines(
            new[] { "猫 e\u0301" }, LanguageMode.English,
            out LanguageMode mixed, out usedFallback, out error), error);
        Assert.AreEqual(LanguageMode.Mixed, mixed);

        Assert.IsTrue(WordListLanguageDetector.TryDetectDisplayLines(
            new[] { "\U00020000" }, LanguageMode.English,
            out chinese, out usedFallback, out error), error);
        Assert.AreEqual(LanguageMode.Chinese, chinese);
    }

    [Test]
    public void EditorDetectionStripsPinyinAndFallsBackWhenThereAreNoLetters()
    {
        Assert.IsTrue(WordListLanguageDetector.TryDetectEditorLines(
            new[] { "猫 | ni" }, LanguageMode.English,
            out LanguageMode detected, out bool usedFallback, out string error), error);
        Assert.AreEqual(LanguageMode.Chinese, detected);
        Assert.IsFalse(usedFallback);

        Assert.IsTrue(WordListLanguageDetector.TryDetectEditorLines(
            new[] { " １２３ — ! " }, LanguageMode.Mixed,
            out detected, out usedFallback, out error), error);
        Assert.AreEqual(LanguageMode.Mixed, detected);
        Assert.IsTrue(usedFallback);
    }

    [Test]
    public void UnsupportedLetterScriptsReturnAnActionableError()
    {
        Assert.IsFalse(WordListLanguageDetector.TryDetectDisplayLines(
            new[] { "かな" }, LanguageMode.English,
            out _, out _, out string error));
        StringAssert.Contains("Only English (Latin) and Chinese (Han)", error);
    }

    [Test]
    public void AutoCodecPreservesPinyinAcrossChineseMixedChineseModeChanges()
    {
        var original = new WordListLibraryContent
        {
            LanguageMode = LanguageMode.Chinese,
            Words = new List<string> { "你好，" },
            ChineseWords = new List<ChineseWordEntry>
            {
                new ChineseWordEntry
                {
                    display = "你好，",
                    entries = new List<ChinesePhaseEntry>
                    {
                        new ChinesePhaseEntry { character = "你", pinyin = "ni" },
                        new ChinesePhaseEntry { character = "好", pinyin = "hao" }
                    }
                }
            }
        };

        Assert.IsTrue(WordListLibraryContentCodec.TryParseAutoEditorLines(
            new[] { "你好，", "hello!" }, LanguageMode.Chinese, original,
            out LanguageMode mixedMode, out bool usedFallback, out WordListLibraryContent mixed, out string error), error);
        Assert.AreEqual(LanguageMode.Mixed, mixedMode);
        Assert.IsFalse(usedFallback);
        CollectionAssert.AreEqual(new[] { "ni", "hao" }, ExtractChinesePinyin(mixed.MixedWords[0]));

        Assert.IsTrue(WordListLibraryContentCodec.TryParseAutoEditorLines(
            new[] { "你好，" }, LanguageMode.English, mixed,
            out LanguageMode chineseMode, out usedFallback, out WordListLibraryContent chinese, out error), error);
        Assert.AreEqual(LanguageMode.Chinese, chineseMode);
        CollectionAssert.AreEqual(new[] { "ni", "hao" }, ExtractChinesePinyin(chinese.ChineseWords[0]));
        Assert.AreEqual("你好，", chinese.Words[0]);
    }

    [Test]
    public void AutoCodecKeepsStandaloneNeutralRowsInMixedStorageWhileDetectingChinese()
    {
        string[] editorRows = { "你 | ni", "123", "!" };
        Assert.IsTrue(WordListLibraryContentCodec.TryParseAutoEditorLines(
            editorRows, LanguageMode.English, null,
            out LanguageMode detectedMode, out bool usedFallback,
            out WordListLibraryContent parsed, out string error), error);
        Assert.AreEqual(LanguageMode.Chinese, detectedMode);
        Assert.IsFalse(usedFallback);
        Assert.AreEqual(LanguageMode.Mixed, parsed.LanguageMode);
        CollectionAssert.AreEqual(new[] { "你", "123", "!" }, parsed.Words);
        Assert.AreEqual("123", parsed.MixedWords[1].segments[0].text);
        Assert.AreEqual("!", parsed.MixedWords[2].segments[0].text);

        Assert.IsTrue(WordListLibraryContentCodec.TryParseAutoEditorLines(
            editorRows, LanguageMode.Mixed, parsed,
            out detectedMode, out usedFallback, out WordListLibraryContent roundTripped, out error), error);
        Assert.AreEqual(LanguageMode.Chinese, detectedMode);
        Assert.IsFalse(usedFallback);
        Assert.AreEqual(LanguageMode.Mixed, roundTripped.LanguageMode);
        CollectionAssert.AreEqual(new[] { "你", "123", "!" }, roundTripped.Words);
        CollectionAssert.AreEqual(new[] { "ni" }, ExtractChinesePinyin(roundTripped.MixedWords[0]));

        var rawCandidate = new WordListLibraryContent
        {
            LanguageMode = LanguageMode.Chinese,
            Words = new List<string> { "你", "123", "!" },
            ChineseWords = new List<ChineseWordEntry>
            {
                new ChineseWordEntry
                {
                    display = "你",
                    entries = new List<ChinesePhaseEntry>
                    {
                        new ChinesePhaseEntry { character = "你", pinyin = "ni" }
                    }
                },
                null,
                null
            },
            MixedWords = new List<MixedWordEntry> { null, null, null }
        };
        Assert.IsTrue(WordListLibraryContentCodec.TryNormalizeAutoContent(
            rawCandidate, LanguageMode.Chinese,
            out detectedMode, out usedFallback, out WordListLibraryContent normalized, out error), error);
        Assert.AreEqual(LanguageMode.Chinese, detectedMode);
        Assert.IsFalse(usedFallback);
        Assert.AreEqual(LanguageMode.Mixed, normalized.LanguageMode);
        CollectionAssert.AreEqual(new[] { "你", "123", "!" }, normalized.Words);
        CollectionAssert.AreEqual(new[] { "ni" }, ExtractChinesePinyin(normalized.MixedWords[0]));
    }

    [Test]
    public void AutoCodecRequiresPinyinForNewHanTextInsteadOfInventingIt()
    {
        Assert.IsFalse(WordListLibraryContentCodec.TryParseAutoEditorLines(
            new[] { "新词", "hello" }, LanguageMode.English, null,
            out LanguageMode detected, out _, out WordListLibraryContent content, out string error));
        Assert.AreEqual(LanguageMode.Mixed, detected);
        Assert.IsNull(content);
        StringAssert.Contains("pinyin", error);
    }

    [Test]
    public void RuntimeNormalizationPreservesRawDisplayTextInsteadOfReadingEditorAnnotations()
    {
        var candidate = new WordListLibraryContent
        {
            LanguageMode = LanguageMode.English,
            Words = new List<string> { " A|b " }
        };
        Assert.IsTrue(WordListLibraryContentCodec.TryNormalizeAutoContent(candidate, LanguageMode.English,
            out LanguageMode mode, out bool usedFallback, out WordListLibraryContent normalized, out string error), error);
        Assert.AreEqual(LanguageMode.English, mode);
        Assert.IsFalse(usedFallback);
        Assert.AreEqual(" A|b ", normalized.Words[0]);
    }

    private static List<string> ExtractChinesePinyin(MixedWordEntry word)
    {
        var result = new List<string>();
        foreach (MixedSegmentData segment in word.segments)
            if (segment != null && segment.type == "chinese" && segment.entries != null)
                foreach (ChinesePhaseEntry entry in segment.entries)
                    result.Add(entry.pinyin);
        return result;
    }

    private static List<string> ExtractChinesePinyin(ChineseWordEntry word)
    {
        var result = new List<string>();
        foreach (ChinesePhaseEntry entry in word.entries)
            result.Add(entry.pinyin);
        return result;
    }
}
