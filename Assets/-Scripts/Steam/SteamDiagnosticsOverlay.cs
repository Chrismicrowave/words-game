#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
using Words.Community;

/// <summary>Transient read-only status for local Steam integration testing.</summary>
public sealed class SteamDiagnosticsSnapshot
{
    public string ExpectedAppId { get; }
    public string SessionStatus { get; }
    public string CurrentAppId { get; }
    public string CurrentAccountId { get; }
    public string SessionError { get; }
    public string CommunityAvailability { get; }
    public string CommunityAppId { get; }
    public string CommunityAccountId { get; }
    public string CommunityAuthenticated { get; }
    public string CommunityError { get; }

    internal SteamDiagnosticsSnapshot(string expectedAppId, string sessionStatus,
        string currentAppId, string currentAccountId, string sessionError,
        string communityAvailability, string communityAppId, string communityAccountId,
        string communityAuthenticated, string communityError)
    {
        ExpectedAppId = expectedAppId ?? string.Empty;
        SessionStatus = sessionStatus ?? string.Empty;
        CurrentAppId = currentAppId ?? string.Empty;
        CurrentAccountId = currentAccountId ?? string.Empty;
        SessionError = sessionError ?? string.Empty;
        CommunityAvailability = communityAvailability ?? string.Empty;
        CommunityAppId = communityAppId ?? string.Empty;
        CommunityAccountId = communityAccountId ?? string.Empty;
        CommunityAuthenticated = communityAuthenticated ?? string.Empty;
        CommunityError = communityError ?? string.Empty;
    }
}

/// <summary>Captures existing in-memory Steam state without initializing Steam or making requests.</summary>
public static class SteamDiagnosticsSnapshotProvider
{
    public const uint ExpectedAppId = 4966870;

    public static SteamDiagnosticsSnapshot CaptureCurrent()
    {
        ISteamClientSession session = null;
        ICommunityService communityService = null;
        CommunityServiceState communityState = null;
        bool remoteAuthenticated = false;
        string communityReadError = string.Empty;

        try
        {
            session = SteamClientSessionRegistry.Current;
        }
        catch (Exception exception)
        {
            session = null;
            communityReadError = "Steam session state unavailable (" + exception.GetType().Name + ").";
        }

        try
        {
            if (CommunityServiceRegistry.TryGetExisting(out communityService))
            {
                communityState = communityService.State;
                remoteAuthenticated = communityService.IsRemoteAuthenticated;
            }
            else
            {
                communityReadError = "Community service has not been initialized by the game.";
            }
        }
        catch (Exception exception)
        {
            communityReadError = AppendError(communityReadError,
                "Community state unavailable (" + exception.GetType().Name + ").");
        }

        return Capture(session, communityState, remoteAuthenticated, communityReadError);
    }

    /// <summary>Pure projection for tests and callers that already have state snapshots.</summary>
    public static SteamDiagnosticsSnapshot Capture(ISteamClientSession session,
        CommunityServiceState communityState, bool remoteAuthenticated, string communityReadError = null)
    {
        string sessionStatus = "Unknown";
        string sessionAppId = "0";
        string sessionAccountId = "Not available";
        string sessionError = string.Empty;

        if (session == null)
        {
            sessionStatus = "Unavailable";
            sessionError = "No Steam session is registered.";
        }
        else
        {
            try { sessionStatus = session.Status.ToString(); }
            catch (Exception exception) { sessionError = "Status read failed (" + exception.GetType().Name + ")."; }

            try { sessionAppId = session.CurrentAppId.ToString(); }
            catch (Exception exception) { sessionError = AppendError(sessionError, "AppID read failed (" + exception.GetType().Name + ")."); }

            try
            {
                ulong accountId = session.IsInitialized ? session.CurrentAccountId : 0UL;
                sessionAccountId = accountId != 0 ? accountId.ToString() : "Not available";
            }
            catch (Exception exception)
            {
                sessionError = AppendError(sessionError, "Current account read failed (" + exception.GetType().Name + ").");
            }

            try { sessionError = AppendError(sessionError, session.LastError); }
            catch (Exception exception) { sessionError = AppendError(sessionError, "Error details unavailable (" + exception.GetType().Name + ")."); }
        }

        string communityAvailability = communityState != null
            ? communityState.Availability.ToString()
            : "Unknown";
        string communityAppId = communityState != null ? communityState.CurrentAppId.ToString() : "0";
        string communityAccountId = communityState != null && communityState.CurrentAccountId != 0
            ? communityState.CurrentAccountId.ToString()
            : "Not available";
        string communityError = communityState != null ? communityState.LastError : string.Empty;
        communityError = AppendError(communityError, communityReadError);

        return new SteamDiagnosticsSnapshot(ExpectedAppId.ToString(), sessionStatus,
            sessionAppId, sessionAccountId, sessionError,
            communityAvailability, communityAppId, communityAccountId,
            remoteAuthenticated ? "Yes" : "No", communityError);
    }

    private static string AppendError(string current, string next)
    {
        if (string.IsNullOrWhiteSpace(next)) return current ?? string.Empty;
        if (string.IsNullOrWhiteSpace(current)) return next.Trim();
        return current.Trim() + " " + next.Trim();
    }
}

/// <summary>Small, non-interactive diagnostics panel; only the close glyph consumes a mouse click.</summary>
public sealed class SteamDiagnosticsOverlay : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 1f;
    private const float PanelWidth = 420f;
    private const float PanelHeight = 252f;

    private SteamDiagnosticsSnapshot snapshot;
    private float refreshAt;
    private bool dismissed;
    private GUIStyle titleStyle;
    private GUIStyle rowStyle;
    private GUIStyle closeStyle;

    public static SteamDiagnosticsOverlay Attach(GameObject owner, GameConfigProfile profile)
    {
        if (owner == null || profile == null
            || profile.buildPurpose != BuildPurpose.LocalTesting
            || !profile.enableSteamDiagnostics)
            return null;

        SteamDiagnosticsOverlay overlay = owner.GetComponent<SteamDiagnosticsOverlay>();
        if (overlay == null)
            overlay = owner.AddComponent<SteamDiagnosticsOverlay>();

        overlay.Initialize();
        return overlay;
    }

    private void Initialize()
    {
        dismissed = false;
        RefreshSnapshot();
        refreshAt = Time.unscaledTime + RefreshIntervalSeconds;
    }

    private void Update()
    {
        if (!dismissed && Time.unscaledTime >= refreshAt)
        {
            RefreshSnapshot();
            refreshAt = Time.unscaledTime + RefreshIntervalSeconds;
        }
    }

    private void RefreshSnapshot()
    {
        try
        {
            snapshot = SteamDiagnosticsSnapshotProvider.CaptureCurrent();
        }
        catch (Exception exception)
        {
            snapshot = SteamDiagnosticsSnapshotProvider.Capture(null, null, false,
                "Diagnostics read failed (" + exception.GetType().Name + ").");
        }
    }

    private void OnGUI()
    {
        if (dismissed || snapshot == null)
            return;

        EnsureStyles();
        Rect panel = new Rect(Mathf.Max(8f, Screen.width - PanelWidth - 16f),
            Mathf.Max(8f, Screen.height - PanelHeight - 16f), PanelWidth, PanelHeight);
        GUI.Box(panel, GUIContent.none);

        Rect titleRect = new Rect(panel.x + 12f, panel.y + 8f, PanelWidth - 56f, 22f);
        Rect closeRect = new Rect(panel.xMax - 34f, panel.y + 7f, 24f, 22f);
        GUI.Label(titleRect, "Steam Diagnostics · Local Testing", titleStyle);
        GUI.Label(closeRect, "×", closeStyle);

        Event currentEvent = Event.current;
        if (currentEvent != null && currentEvent.type == EventType.MouseDown
            && currentEvent.button == 0 && closeRect.Contains(currentEvent.mousePosition))
        {
            dismissed = true;
            currentEvent.Use();
            return;
        }

        float y = panel.y + 34f;
        float lineHeight = 20f;
        float x = panel.x + 12f;
        float width = panel.width - 24f;
        DrawRow(ref y, x, width, lineHeight, "Expected AppID", snapshot.ExpectedAppId);
        DrawRow(ref y, x, width, lineHeight, "Steam", snapshot.SessionStatus
            + " · AppID " + snapshot.CurrentAppId + " · Account " + snapshot.CurrentAccountId);
        DrawRow(ref y, x, width + 8f, lineHeight + 4f, "Steam error", EmptyAsNone(snapshot.SessionError));
        DrawRow(ref y, x, width, lineHeight, "Community", snapshot.CommunityAvailability
            + " · Authenticated " + snapshot.CommunityAuthenticated
            + " · AppID " + snapshot.CommunityAppId);
        DrawRow(ref y, x, width + 8f, lineHeight + 4f, "Community account", snapshot.CommunityAccountId);
        DrawRow(ref y, x, width + 8f, lineHeight + 4f, "Community error", EmptyAsNone(snapshot.CommunityError));
        DrawRow(ref y, x, width + 8f, lineHeight + 7f, "Achievements",
            "Use the Steam profile or Editor Inspect after leaving Play; this panel does not query or reset them.");
    }

    private void DrawRow(ref float y, float x, float width, float height, string label, string value)
    {
        GUI.Label(new Rect(x, y, 118f, height), label + ":", rowStyle);
        GUI.Label(new Rect(x + 118f, y, width - 118f, height), value ?? string.Empty, rowStyle);
        y += height;
    }

    private void EnsureStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            rowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.94f, 0.94f, 0.94f, 1f) }
            };
            closeStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
        }
    }

    private static string EmptyAsNone(string value) =>
        string.IsNullOrWhiteSpace(value) ? "None" : value;
}
#endif
