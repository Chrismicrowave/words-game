using System;
using System.Collections.Generic;

/// <summary>
/// Parses a MixedWordEntry into a flat step sequence for the WordEngine,
/// plus segment metadata for the display layer.
/// </summary>
public static class MixedPhaseParser
{
    public enum SegmentType { English, Chinese }

    public struct MixedSegment
    {
        public SegmentType type;
        public string text;             // English: literal text; Chinese: pinyin (typeTarget substring)
        public int typeStart;           // index into the combined typeTarget
        public int typeEnd;             // exclusive end index
        // Chinese only:
        public int[] boundaries;        // cumulative letter counts within this segment's pinyin
        public string[] characters;     // Chinese characters at each boundary
        public ChinesePhaseEntry[] entries;
    }

    public struct MixedPhaseResult
    {
        public string typeTarget;           // full string to type (e.g. "hellonihao")
        public MixedSegment[] segments;
    }

    /// <summary>Wraps a plain English word as a single-segment MixedPhaseResult.</summary>
    public static MixedPhaseResult FromEnglish(string word)
    {
        return new MixedPhaseResult
        {
            typeTarget = word,
            segments = new[]
            {
                new MixedSegment
                {
                    type      = SegmentType.English,
                    text      = word,
                    typeStart = 0,
                    typeEnd   = word.Length
                }
            }
        };
    }

    /// <summary>Wraps a ChineseWordEntry as a single-segment MixedPhaseResult.</summary>
    public static MixedPhaseResult FromChinese(ChineseWordEntry chineseWord)
    {
        var entries = chineseWord.entries;
        int cumulative = 0;
        var boundaries = new int[entries.Count];
        var characters = new string[entries.Count];
        var entryArr   = new ChinesePhaseEntry[entries.Count];
        var sb         = new System.Text.StringBuilder();

        for (int i = 0; i < entries.Count; i++)
        {
            sb.Append(entries[i].pinyin);
            cumulative    += entries[i].pinyin.Length;
            boundaries[i]  = cumulative;
            characters[i]  = entries[i].character;
            entryArr[i]    = entries[i];
        }

        string typeTarget = sb.ToString();
        return new MixedPhaseResult
        {
            typeTarget = typeTarget,
            segments = new[]
            {
                new MixedSegment
                {
                    type       = SegmentType.Chinese,
                    text       = typeTarget,
                    typeStart  = 0,
                    typeEnd    = typeTarget.Length,
                    boundaries = boundaries,
                    characters = characters,
                    entries    = entryArr
                }
            }
        };
    }

    public static MixedPhaseResult Parse(MixedWordEntry mixedWord)
    {
        var segments = new List<MixedSegment>();
        // typeBuilder contains only letter/digit characters (spaces stripped from English).
        // This keeps typeTarget indices == WordEngine.CurrentStep (letter-count-based).
        var typeBuilder = new System.Text.StringBuilder();
        int stepCount = 0;

        foreach (var seg in mixedWord.segments)
        {
            if (seg.type == "english")
            {
                int startStep = stepCount;
                foreach (char c in seg.text)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        typeBuilder.Append(c);
                        stepCount++;
                    }
                }
                segments.Add(new MixedSegment
                {
                    type      = SegmentType.English,
                    text      = seg.text,   // original text (may contain spaces) for display
                    typeStart = startStep,
                    typeEnd   = stepCount
                });
            }
            else if (seg.type == "chinese" && seg.entries != null)
            {
                int startStep   = stepCount;
                int localCumul  = 0;
                var boundaries  = new int[seg.entries.Count];
                var characters  = new string[seg.entries.Count];
                var entryArr    = new ChinesePhaseEntry[seg.entries.Count];

                for (int i = 0; i < seg.entries.Count; i++)
                {
                    typeBuilder.Append(seg.entries[i].pinyin);
                    localCumul    += seg.entries[i].pinyin.Length;
                    stepCount     += seg.entries[i].pinyin.Length;
                    boundaries[i]  = startStep + localCumul;
                    characters[i]  = seg.entries[i].character;
                    entryArr[i]    = seg.entries[i];
                }

                segments.Add(new MixedSegment
                {
                    type       = SegmentType.Chinese,
                    text       = typeBuilder.ToString().Substring(startStep),
                    typeStart  = startStep,
                    typeEnd    = stepCount,
                    boundaries = boundaries,
                    characters = characters,
                    entries    = entryArr
                });
            }
        }

        return new MixedPhaseResult
        {
            typeTarget = typeBuilder.ToString(),
            segments   = segments.ToArray()
        };
    }
}
