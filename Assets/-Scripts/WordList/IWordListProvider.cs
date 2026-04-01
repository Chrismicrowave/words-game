using System.Collections.Generic;

public interface IWordListProvider
{
    string DisplayName { get; }
    List<string> GetWords();
    bool IsEditable { get; }
}
