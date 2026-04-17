using System.Collections.Generic;
using UnityEngine.Localization.Settings;

/// <summary>
/// Synchronous localization helper. Safe to call after LocalizationSettings.InitializationOperation completes.
/// String tables used here must have Preload enabled in Localization Settings.
/// </summary>
public static class LocalizationService
{
    public static string Get(string table, string key)
    {
        var op = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(table, key);
        op.WaitForCompletion();
        return op.Result ?? key;
    }

    public static string GetSmart(string table, string key, Dictionary<string, object> args)
    {
        var op = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(
            table, key, arguments: new object[] { args });
        op.WaitForCompletion();
        return op.Result ?? key;
    }
}
