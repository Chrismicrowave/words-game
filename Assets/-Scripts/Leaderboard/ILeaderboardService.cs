using System;
using System.Collections.Generic;

public struct LeaderboardEntry
{
    public string PlayerName;
    public float TotalTime;
    public int PhaseCount;
    public string WordListName;
    public DateTime SubmittedAt;
}

public interface ILeaderboardService
{
    void SubmitScore(string wordListName, float totalTime, int phaseCount);
    void GetLeaderboard(string wordListName, Action<List<LeaderboardEntry>> callback);
}

/// <summary>
/// Default no-op implementation. Replace with Steam or web API integration later.
/// </summary>
public class NullLeaderboardService : ILeaderboardService
{
    public void SubmitScore(string wordListName, float totalTime, int phaseCount)
    {
        UnityEngine.Debug.Log($"[Leaderboard] Score submitted (no backend): {wordListName} - {totalTime:F2}s, {phaseCount} phases");
    }

    public void GetLeaderboard(string wordListName, Action<List<LeaderboardEntry>> callback)
    {
        callback?.Invoke(new List<LeaderboardEntry>());
    }
}
