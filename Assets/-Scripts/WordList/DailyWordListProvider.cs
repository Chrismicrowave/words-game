using System.Collections.Generic;

/// <summary>
/// Stub for future daily word list integration.
/// Will fetch curated word lists from a server API.
/// </summary>
public class DailyWordListProvider : IWordListProvider
{
    public string DisplayName => "Daily Challenge";
    public bool IsEditable => false;

    // TODO: Replace with HTTP fetch from daily list API
    // Expected endpoint: GET /api/daily-words?date=YYYY-MM-DD
    // Response: { "name": "Daily - March 31", "words": [...] }
    public List<string> GetWords()
    {
        return new List<string>
        {
            "Daily",
            "Challenge",
            "Coming Soon"
        };
    }
}
