#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using Microsoft.Win32;

/// <summary>
/// Editor tools for challenge progression debugging.
/// Tools > Words > Reset Challenge Progress
///
/// Clears PlayerPrefs (Editor + standalone), ListTimeRecords.json,
/// and unlocks back to level 1.
/// </summary>
public class ResetChallengeProgress
{
    [MenuItem("Tools/Words/Reset Challenge Progress")]
    public static void Execute()
    {
        if (!EditorUtility.DisplayDialog("Reset Challenge Progress",
            "Reset all challenge unlocks, star ratings, and time records back to level 1?\n\n" +
            "Clears:\n" +
            "  • Editor PlayerPrefs (registry)\n" +
            "  • Standalone PlayerPrefs (registry)\n" +
            "  • ListTimeRecords.json (persistent data)",
            "Reset", "Cancel"))
            return;

        // ── 1. Editor PlayerPrefs + ListTimeRecords.json ──────────────────────
        ChallengeProgression.ResetAll();
        Debug.Log("[ResetProgress] Editor PlayerPrefs cleared.");

        // ── 2. Standalone PlayerPrefs registry key ────────────────────────────
        // Editor stores at: HKCU\Software\Unity\UnityEditor\<company>\<product>
        // Standalone stores at: HKCU\Software\<company>\<product>
        const string companyName = "UnihoodStudio";
        const string productName = "StickyWords";
        string standaloneKey = $@"Software\{companyName}\{productName}";

        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(standaloneKey, writable: true))
            {
                if (key != null)
                {
                    // Delete the Unity PlayerPrefs values inside this key
                    // (Unity stores individual prefs as string values here)
                    string[] valueNames = key.GetValueNames();
                    foreach (var v in valueNames)
                        key.DeleteValue(v, throwOnMissingValue: false);

                    // Also try deleting subkeys (older Unity versions nest them)
                    string[] subNames = key.GetSubKeyNames();
                    foreach (var s in subNames)
                        key.DeleteSubKeyTree(s, throwOnMissingSubKey: false);

                    Debug.Log("[ResetProgress] Standalone PlayerPrefs registry key cleared.");
                }
                else
                {
                    Debug.Log("[ResetProgress] No standalone PlayerPrefs registry key found.");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ResetProgress] Could not access standalone registry: {e.Message}");
        }

        // ── 3. Ensure ListTimeRecords.json is gone ───────────────────────────
        string timeRecordsPath = Path.Combine(Application.persistentDataPath, "ListTimeRecords.json");
        if (File.Exists(timeRecordsPath))
        {
            try
            {
                File.Delete(timeRecordsPath);
                Debug.Log($"[ResetProgress] Deleted {timeRecordsPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ResetProgress] Could not delete time records: {e.Message}");
            }
        }
        else
        {
            Debug.Log("[ResetProgress] No ListTimeRecords.json found.");
        }

        Debug.Log("[ResetProgress] Full progress reset complete.");
    }
}
#endif
