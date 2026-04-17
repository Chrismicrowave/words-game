public struct ChinesePhaseData
{
    public string typeTarget;        // e.g. "nihao"
    public int[] boundaries;         // cumulative letter counts where each character completes, e.g. [2, 5]
    public string[] characters;      // the character at each boundary, e.g. ["你", "好"]
    public ChinesePhaseEntry[] entries;
}
