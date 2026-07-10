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
    private class TimeRecordData
    {
        public Dictionary<string, TimeRecord> records = new Dictionary<string, TimeRecord>();
    }

    private static string FilePath => Path.Combine(Application.persistentDataPath, "ListTimeRecords.json");

    /// <summary>Save a completion time for a list key.</summary>
    public static void SaveTime(string listKey, float totalTime, int phaseCount, int errors)
    {
        var data = LoadAll();
        data.records[listKey] = new TimeRecord
        {
            TotalTime = totalTime,
            PhaseCount = phaseCount,
            Errors = errors
        };
        SaveAll(data);
    }

    /// <summary>Get the saved time record, or null if none.</summary>
    public static TimeRecord? GetTime(string listKey)
    {
        var data = LoadAll();
        if (data.records.TryGetValue(listKey, out var record))
            return record;
        return null;
    }

    /// <summary>Delete the time record for a list key.</summary>
    public static void DeleteTime(string listKey)
    {
        var data = LoadAll();
        if (data.records.Remove(listKey))
            SaveAll(data);
    }

    /// <summary>Check if a time record exists for a list key.</summary>
    public static bool HasTime(string listKey)
    {
        var data = LoadAll();
        return data.records.ContainsKey(listKey);
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
            return JsonUtility.FromJson<TimeRecordData>(json) ?? new TimeRecordData();
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
