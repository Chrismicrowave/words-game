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

    public static MixedPhaseResult Parse(MixedWordEntry mixedWord)
    {
        var segments = new List<MixedSegment>();
        var typeBuilder = new System.Text.StringBuilder();

        foreach (var seg in mixedWord.segments)
        {
            int start = typeBuilder.Length;

            if (seg.type == "english")
            {
                typeBuilder.Append(seg.text);
                segments.Add(new MixedSegment
                {
                    type = SegmentType.English,
                    text = seg.text,
                    typeStart = start,
                    typeEnd = typeBuilder.Length
                });
            }
            else if (seg.type == "chinese" && seg.entries != null)
            {
                int cumulative = 0;
                var boundaries = new int[seg.entries.Count];
                var characters = new string[seg.entries.Count];
                var entryArr = new ChinesePhaseEntry[seg.entries.Count];

                for (int i = 0; i < seg.entries.Count; i++)
                {
                    typeBuilder.Append(seg.entries[i].pinyin);
                    cumulative += seg.entries[i].pinyin.Length;
                    boundaries[i] = start + cumulative;
                    characters[i] = seg.entries[i].character;
                    entryArr[i] = seg.entries[i];
                }

                segments.Add(new MixedSegment
                {
                    type = SegmentType.Chinese,
                    text = typeBuilder.ToString().Substring(start),
                    typeStart = start,
                    typeEnd = typeBuilder.Length,
                    boundaries = boundaries,
                    characters = characters,
                    entries = entryArr
                });
            }
        }

        return new MixedPhaseResult
        {
            typeTarget = typeBuilder.ToString(),
            segments = segments.ToArray()
        };
    }
}
