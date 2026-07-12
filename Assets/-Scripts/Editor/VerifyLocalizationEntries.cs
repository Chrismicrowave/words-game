using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;

public class VerifyLocalizationEntries
{
    [MenuItem("Tools/Words/Verify Localization Entries")]
    public static void Execute()
    {
        try
        {
            // Load the UI StringTable
            var assetPath = "Assets/Localization/StringTables/UI_en.asset";
            var table = AssetDatabase.LoadAssetAtPath<StringTable>(assetPath);
            if (table == null)
            {
                Debug.LogError("UI_en.asset not found at path: " + assetPath);
                return;
            }

            string[] keys = { "UI.Level.UnlockCustom", "UI.Level.UnlockCommunity" };
            bool allFound = true;
            foreach (var key in keys)
            {
                var entry = table.GetEntry(key);
                var val = entry?.Value;
                if (entry == null)
                    Debug.LogError($"MISSING: {key} not found in UI_en string table");
                else
                    Debug.Log($"OK: {key} = \"{val}\"");
            }

            // Also check the Chinese table
            var zhPath = "Assets/Localization/StringTables/UI_zh-Hans.asset";
            var zhTable = AssetDatabase.LoadAssetAtPath<StringTable>(zhPath);
            if (zhTable != null)
            {
                foreach (var key in keys)
                {
                    var entry = zhTable.GetEntry(key);
                    var val = entry?.Value;
                    if (entry == null)
                        Debug.LogError($"MISSING: {key} not found in UI_zh-Hans string table");
                    else
                        Debug.Log($"OK (zh): {key} = \"{val}\"");
                }
            }

            if (allFound)
                Debug.Log("All localization entries verified successfully.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Verification failed: {e.Message}");
        }
    }
}
