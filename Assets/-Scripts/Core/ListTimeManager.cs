using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persistent time records for challenge and custom word lists.
///
/// Storage: single JSON file at Application.persistentDataPath/ListTimeRecords.json.
/// Not stored in .txt files (players can't cheat by editing word files).
///
/// Key format:
///   Challenge: "chg_{filename}"  (e.g. "chg_level_01.txt" — stable across updates)
///   Custom:    "cst_{uuid}"       (UUID generated at list creation, stored in file header)
///
/// Future leaderboard: server-side validation (totalTime >= phaseCount * 4.0)
/// rejects impossible scores regardless of local storage.
/// </summary>
public static class ListTimeManager
{
    [Serializable]
    public struct TimeRecord
    {
        public float TotalTime;
        public int PhaseCount;
        public int Errors;
    }

    [Serializable]
    private class TimeRecordEntry
    {
        public string key;
        public TimeRecord record;
    }

    [Serializable]
    private class TimeRecordData
    {
        public List<TimeRecordEntry> entries = new List<TimeRecordEntry>();
    }

    private static string FilePath => Path.Combine(Application.persistentDataPath, "ListTimeRecords.json");

    /// <summary>Save a completion time for a list key.</summary>
    public static void SaveTime(string listKey, float totalTime, int phaseCount, int errors)
    {
        var data = LoadAll();
        // Replace existing entry or add new one
        int idx = data.entries.FindIndex(e => e.key == listKey);
        if (idx >= 0)
            data.entries[idx] = new TimeRecordEntry { key = listKey, record = new TimeRecord { TotalTime = totalTime, PhaseCount = phaseCount, Errors = errors } };
        else
            data.entries.Add(new TimeRecordEntry { key = listKey, record = new TimeRecord { TotalTime = totalTime, PhaseCount = phaseCount, Errors = errors } });
        SaveAll(data);
    }

    /// <summary>Get the saved time record, or null if none.</summary>
    public static TimeRecord? GetTime(string listKey)
    {
        var data = LoadAll();
        int idx = data.entries.FindIndex(e => e.key == listKey);
        return idx >= 0 ? data.entries[idx].record : (TimeRecord?)null;
    }

    /// <summary>Delete the time record for a list key.</summary>
    public static void DeleteTime(string listKey)
    {
        var data = LoadAll();
        int removed = data.entries.RemoveAll(e => e.key == listKey);
        if (removed > 0)
            SaveAll(data);
    }

    /// <summary>Check if a time record exists for a list key.</summary>
    public static bool HasTime(string listKey)
    {
        var data = LoadAll();
        return data.entries.Exists(e => e.key == listKey);
    }

    /// <summary>Clear ALL time records (used by ChallengeProgression.ResetAll).</summary>
    public static void ClearAll()
    {
        if (File.Exists(FilePath))
        {
            try { File.Delete(FilePath); }
            catch (Exception e) { Debug.LogWarning($"[ListTimeManager] Failed to clear: {e.Message}"); }
        }
    }

    /// <summary>Format a time float as "1:02.3" or "45.2s".</summary>
    public static string FormatTime(float totalSeconds)
    {
        if (totalSeconds >= 60f)
        {
            int min = Mathf.FloorToInt(totalSeconds / 60f);
            float sec = totalSeconds - min * 60f;
            return $"{min}:{sec:F1}";
        }
        return $"{totalSeconds:F1}s";
    }

    // ── I/O ──────────────────────────────────────────────────────────────────────

    private static TimeRecordData LoadAll()
    {
        if (!File.Exists(FilePath))
            return new TimeRecordData();

        try
        {
            string json = File.ReadAllText(FilePath);
            var data = JsonUtility.FromJson<TimeRecordData>(json) ?? new TimeRecordData();
            // JsonUtility doesn't run field initializers on deserialization (via Unity 6)
            if (data.entries == null)
                data.entries = new List<TimeRecordEntry>();
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ListTimeManager] Failed to load: {e.Message}");
            return new TimeRecordData();
        }
    }

    private static void SaveAll(TimeRecordData data)
    {
        try
        {
            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ListTimeManager] Failed to save: {e.Message}");
        }
    }
}
