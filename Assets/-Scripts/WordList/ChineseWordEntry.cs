using System;
using System.Collections.Generic;

[Serializable]
public class ChineseWordEntry
{
    public string display;                     // e.g. "你好"
    public List<ChinesePhaseEntry> entries;    // one entry per character
}
