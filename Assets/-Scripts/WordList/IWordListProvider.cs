using System.Collections.Generic;

public interface IWordListProvider
{
    string DisplayName { get; }
    LanguageMode LanguageMode { get; }
    List<string> GetWords();
    List<ChineseWordEntry> GetChineseWords();
    bool IsEditable { get; }
}
