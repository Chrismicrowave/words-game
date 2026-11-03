using System;

/// <summary>
/// Tracks the UI state for an asynchronous Steam account inspection without depending on
/// Steamworks, EditorWindow, or UnityEditor. The invoker is supplied by the Editor UI.
/// </summary>
public sealed class SteamInspectCoordinator
{
    public const string CheckingMessage = "Checking the logged-in Steam account…";
    public const string NotCheckedMessage = "Steam status has not been checked. Use the inspect button before the reset button.";

    private int requestVersion;

    public bool IsChecking { get; private set; }
    public string InspectedSteamId { get; private set; }
    public string Message { get; private set; } = NotCheckedMessage;

    public event Action StateChanged;

    public void Inspect(Action<Action<bool, string, string>> invoke)
    {
        int version = ++requestVersion;
        IsChecking = true;
        InspectedSteamId = null;
        Message = CheckingMessage;
        NotifyStateChanged();

        bool completed = false;
        Action<bool, string, string> completion = (success, steamId, description) =>
        {
            if (version != requestVersion || completed)
                return;

            completed = true;
            IsChecking = false;
            InspectedSteamId = success && !string.IsNullOrWhiteSpace(steamId) ? steamId : null;
            Message = string.IsNullOrWhiteSpace(description)
                ? (success ? "Steam account inspection completed." : "Steam account inspection failed.")
                : description;
            NotifyStateChanged();
        };

        if (invoke == null)
        {
            completion(false, null, "Steam maintenance is unavailable. The optional Steamworks integration assembly is not loaded.");
            return;
        }

        try
        {
            invoke(completion);
        }
        catch (Exception exception)
        {
            completion(false, null, "Steam account inspection failed: " + exception.GetType().Name + ": " + exception.Message);
        }
    }

    public void Fail(string message)
    {
        ++requestVersion;
        IsChecking = false;
        InspectedSteamId = null;
        Message = string.IsNullOrWhiteSpace(message) ? "Steam account inspection failed." : message;
        NotifyStateChanged();
    }

    /// <summary>Invalidates callbacks owned by a window that is being closed or rebuilt.</summary>
    public void Cancel()
    {
        ++requestVersion;
        IsChecking = false;
        InspectedSteamId = null;
        Message = NotCheckedMessage;
        NotifyStateChanged();
    }

    /// <summary>Restores an already completed helper diagnostic without authorizing reset.</summary>
    public void RestoreDiagnostic(string message)
    {
        if (IsChecking)
            return;

        ++requestVersion;
        InspectedSteamId = null;
        Message = string.IsNullOrWhiteSpace(message) ? NotCheckedMessage : message;
        NotifyStateChanged();
    }

    /// <summary>Restores a successful cached Inspect result after the window is recreated.</summary>
    public void RestoreSuccessfulInspect(string steamId, string message)
    {
        if (IsChecking)
            return;

        ++requestVersion;
        InspectedSteamId = string.IsNullOrWhiteSpace(steamId) ? null : steamId;
        Message = string.IsNullOrWhiteSpace(message) ? "Steam account inspection completed." : message;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        try
        {
            StateChanged?.Invoke();
        }
        catch
        {
            // A detached Editor UI subscriber must not interrupt the maintenance callback.
        }
    }
}
