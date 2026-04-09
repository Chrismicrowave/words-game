using System;
using System.Collections.Generic;

[Serializable]
public class MixedWordEntry
{
    public List<MixedSegmentData> segments;
}

[Serializable]
public class MixedSegmentData
{
    public string type;                         // "english" or "chinese"
    public string text;                         // populated for english segments
    public List<ChinesePhaseEntry> entries;     // populated for chinese segments
}
