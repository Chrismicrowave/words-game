using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Applies build-purpose safeguards and stages Steam's local AppID sidecar.</summary>
public sealed class SteamBuildProfileProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    private const string GameConfigAssetPath = "Assets/-Data/GameConfig.asset";
    private const string AppIdFileName = "steam_appid.txt";
    private const string ExpectedSteamAppId = "4966870";

    public int callbackOrder => int.MinValue;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report == null)
            throw new BuildFailedException("Steam build validation received no build report.");

        GameConfig config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigAssetPath);
        string error = ValidateBuild(config != null ? config.activeProfile : null,
            report.summary.platform, report.summary.options);
        if (!string.IsNullOrEmpty(error))
            throw new BuildFailedException(error);
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report == null || report.summary.result != BuildResult.Succeeded)
            return;

        GameConfig config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigAssetPath);
        GameConfigProfile profile = config != null ? config.activeProfile : null;
        if (profile == null)
            throw new BuildFailedException("Steam build finalization failed: no active GameConfigProfile is assigned.");

        string outputDirectory = GetPlayerOutputDirectory(report.summary.platform, report.summary.outputPath);
        string outputAppIdPath = Path.Combine(outputDirectory, AppIdFileName);
        string projectAppIdPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, AppIdFileName);
        if (PathsEqual(outputAppIdPath, projectAppIdPath))
            throw new BuildFailedException("Steam build finalization refused to touch steam_appid.txt in the Unity project root. Choose a player output outside the project root.");
        try
        {
            if (profile.buildPurpose == BuildPurpose.LocalTesting)
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                    throw new InvalidOperationException("Unity project root could not be resolved.");
                EnsureLocalTestingAppId(Path.Combine(projectRoot, AppIdFileName), outputDirectory);
            }
            else
            {
                RemoveReleaseAppId(outputAppIdPath);
            }
        }
        catch (Exception exception)
        {
            throw new BuildFailedException("Steam build finalization failed: " + exception.Message);
        }
    }

    /// <summary>Returns a validation message, or null when the profile and build options agree.</summary>
    public static string ValidateBuild(GameConfigProfile profile, BuildTarget target, BuildOptions options)
    {
        if (profile == null)
            return "Build blocked: the canonical GameConfig has no active profile.";

        if (!IsSupportedStandaloneTarget(target))
            return "Build blocked: Steam builds are supported only for Windows, Linux, and macOS Standalone targets.";

        bool isDevelopmentBuild = (options & BuildOptions.Development) != 0;
        if (profile.buildPurpose == BuildPurpose.LocalTesting)
        {
            return isDevelopmentBuild
                ? null
                : "Build blocked: LocalTesting profiles require a Development build. Use the Master Config wrapper or enable Development in Build Settings.";
        }

        if (isDevelopmentBuild)
            return "Build blocked: PlayerRelease profiles cannot be built with Development options.";

        if (profile.enableSteamDiagnostics)
            return "Build blocked: PlayerRelease must disable Steam diagnostics.";
        if (profile.allowEditorSteamTestTools)
            return "Build blocked: PlayerRelease must disable Editor Steam test tools.";
        if (profile.debugAnyKeyCompletesWord)
            return "Build blocked: PlayerRelease must disable the any-key word-completion debug feature.";
        if (profile.resetLocalDataOnFirstRun)
            return "Build blocked: PlayerRelease cannot arm a first-run local-data reset.";
        if (profile.resetAllLocalDataOnNextEditorPlay)
            return "Build blocked: PlayerRelease cannot carry an armed next-Editor-Play reset.";
        if (profile.resetAllLocalDataOnFirstBuildRun)
            return "Build blocked: PlayerRelease cannot arm a first-build-run local-data reset.";
        if (profile.unlockAllFeatures)
            return "Build blocked: PlayerRelease cannot bypass progression or Community access controls.";

        return null;
    }

    public static bool IsSupportedStandaloneTarget(BuildTarget target) =>
        target == BuildTarget.StandaloneWindows64
        || target == BuildTarget.StandaloneWindows
        || target == BuildTarget.StandaloneLinux64
        || target == BuildTarget.StandaloneOSX;

    public static string GetPlayerOutputDirectory(BuildTarget target, string outputPath)
    {
        if (!IsSupportedStandaloneTarget(target))
            throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported Steam Standalone target.");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Build output path is empty.", nameof(outputPath));

        if (target == BuildTarget.StandaloneOSX)
            return Path.Combine(outputPath, "Contents", "MacOS");

        string directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(directory))
            throw new ArgumentException("Standalone executable must have a parent directory.", nameof(outputPath));
        return directory;
    }

    public static string EnsureLocalTestingAppId(string sourceAppIdPath, string outputDirectory)
    {
        ValidateAppIdFile(sourceAppIdPath, "Local Testing source");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Player output directory is empty.", nameof(outputDirectory));
        Directory.CreateDirectory(outputDirectory);

        string destination = Path.Combine(outputDirectory, AppIdFileName);
        if (File.Exists(destination))
        {
            ValidateAppIdFile(destination, "Existing player sidecar");
            return destination;
        }

        File.Copy(sourceAppIdPath, destination, false);
        return destination;
    }

    public static void RemoveReleaseAppId(string outputAppIdPath)
    {
        if (string.IsNullOrWhiteSpace(outputAppIdPath))
            throw new ArgumentException("Player sidecar path is empty.", nameof(outputAppIdPath));
        if (!File.Exists(outputAppIdPath))
            return;

        ValidateAppIdFile(outputAppIdPath, "Release player sidecar");
        File.Delete(outputAppIdPath);
    }

    private static void ValidateAppIdFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(description + " is missing.", path);

        string appId = File.ReadAllText(path).Trim();
        if (!string.Equals(appId, ExpectedSteamAppId, StringComparison.Ordinal))
            throw new InvalidDataException(description + " does not match the project's Steam AppID; the file was left untouched.");
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
