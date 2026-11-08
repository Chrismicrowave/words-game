using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class SteamBuildProfileProcessorTests
{
    private GameConfigProfile profile;
    private static readonly Type ProcessorType = Type.GetType("SteamBuildProfileProcessor, Assembly-CSharp-Editor", true);

    [SetUp]
    public void SetUp()
    {
        profile = ScriptableObject.CreateInstance<GameConfigProfile>();
        profile.resetTargets = GameResetTargets.None;
        profile.maxChallengeLevels = 0;
        profile.maxCustomLists = 0;
        profile.maxWordsPerList = 0;
        profile.communityEnabled = true;
        profile.customUnlockLevels = 5;
        profile.communityUnlockLevels = 10;
    }

    [TearDown]
    public void TearDown()
    {
        if (profile != null)
            UnityEngine.Object.DestroyImmediate(profile);
    }

    [Test]
    public void LocalTestingRequiresDevelopmentAndPlayerReleaseRejectsUnsafeFlags()
    {
        profile.buildPurpose = BuildPurpose.LocalTesting;
        Assert.That(Validate(BuildTarget.StandaloneWindows64, BuildOptions.None), Does.Contain("require a Development build"));
        Assert.IsNull(Validate(BuildTarget.StandaloneWindows64, BuildOptions.Development));

        profile.buildPurpose = BuildPurpose.PlayerRelease;
        Assert.IsNull(Validate(BuildTarget.StandaloneWindows64, BuildOptions.None));
        Assert.That(Validate(BuildTarget.StandaloneWindows64, BuildOptions.Development), Does.Contain("cannot be built with Development"));

        profile.enableSteamDiagnostics = true;
        Assert.That(Validate(BuildTarget.StandaloneWindows64, BuildOptions.None), Does.Contain("disable Steam diagnostics"));
        profile.enableSteamDiagnostics = false;

        profile.allowEditorSteamTestTools = true;
        Assert.That(Validate(BuildTarget.StandaloneWindows64, BuildOptions.None), Does.Contain("disable Editor Steam test tools"));
        profile.allowEditorSteamTestTools = false;

        profile.debugAnyKeyCompletesWord = true;
        Assert.That(Validate(BuildTarget.StandaloneWindows64, BuildOptions.None), Does.Contain("disable the any-key"));
        profile.debugAnyKeyCompletesWord = false;

        profile.resetLocalDataOnFirstRun = true;
        Assert.That(Validate(BuildTarget.StandaloneWindows64, BuildOptions.None), Does.Contain("first-run local-data reset"));
        profile.resetLocalDataOnFirstRun = false;

        profile.resetAllLocalDataOnNextEditorPlay = true;
        Assert.That(Validate(BuildTarget.StandaloneWindows64, BuildOptions.None), Does.Contain("next-Editor-Play reset"));
        profile.resetAllLocalDataOnNextEditorPlay = false;

        profile.resetAllLocalDataOnFirstBuildRun = true;
        Assert.That(Validate(BuildTarget.StandaloneWindows64, BuildOptions.None), Does.Contain("first-build-run local-data reset"));
        profile.resetAllLocalDataOnFirstBuildRun = false;

        profile.unlockAllFeatures = true;
        Assert.That(Validate(BuildTarget.StandaloneWindows64, BuildOptions.None), Does.Contain("bypass progression"));
        profile.unlockAllFeatures = false;

        profile.ignoreProgressionLocks = true;
        Assert.IsNull(Validate(BuildTarget.StandaloneWindows64, BuildOptions.None),
            "Legacy Ignore Progression Locks data no longer bypasses runtime progression.");
    }

    [Test]
    public void LocalTestingCopiesKnownAppIdAndReleaseOnlyRemovesKnownSidecar()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "SteamBuildProfileProcessorTests-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(tempRoot, "project", "steam_appid.txt");
        string output = Path.Combine(tempRoot, "player");
        Directory.CreateDirectory(Path.GetDirectoryName(source));
        Directory.CreateDirectory(output);

        try
        {
            File.WriteAllText(source, "4966870\n");
            string copiedPath = (string)Invoke("EnsureLocalTestingAppId", source, output);
            Assert.AreEqual(Path.Combine(output, "steam_appid.txt"), copiedPath);
            Assert.AreEqual("4966870", File.ReadAllText(copiedPath).Trim());

            Invoke("RemoveReleaseAppId", copiedPath);
            Assert.IsFalse(File.Exists(copiedPath));

            string unknownSidecar = Path.Combine(output, "steam_appid.txt");
            File.WriteAllText(unknownSidecar, "other-user-data");
            Exception error = Assert.Throws<InvalidDataException>(() => Invoke("RemoveReleaseAppId", unknownSidecar));
            Assert.That(error.Message, Does.Contain("left untouched"));
            Assert.AreEqual("other-user-data", File.ReadAllText(unknownSidecar));

            Exception copyError = Assert.Throws<InvalidDataException>(() => Invoke("EnsureLocalTestingAppId", source, output));
            Assert.That(copyError.Message, Does.Contain("left untouched"));
            Assert.AreEqual("other-user-data", File.ReadAllText(unknownSidecar));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Test]
    public void AppIdOutputDirectoryUsesExecutableFolderAndMacBundleExecutableFolder()
    {
        string windows = (string)Invoke("GetPlayerOutputDirectory", BuildTarget.StandaloneWindows64,
            Path.Combine("root", "Game.exe"));
        Assert.AreEqual("root", windows);

        string mac = (string)Invoke("GetPlayerOutputDirectory", BuildTarget.StandaloneOSX,
            Path.Combine("root", "Game.app"));
        Assert.AreEqual(Path.Combine("root", "Game.app", "Contents", "MacOS"), mac);
    }

    [Test]
    public void OptionalDiagnosticsAssemblyExposesTheExpectedAttachSignature()
    {
        Type diagnosticsType = Type.GetType("SteamDiagnosticsOverlay, SteamAchievementIntegration", false);
        Assert.NotNull(diagnosticsType, "The local Steam diagnostics assembly should be available in Editor tests.");
        MethodInfo attach = diagnosticsType.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(GameObject), typeof(GameConfigProfile) }, null);
        Assert.NotNull(attach, "Coordinator reflection must resolve the exact no-session-creation Attach entry point.");
    }

    private string Validate(BuildTarget target, BuildOptions options) =>
        (string)Invoke("ValidateBuild", profile, target, options);

    private static object Invoke(string methodName, params object[] args)
    {
        MethodInfo method = ProcessorType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method, "Missing build-policy API: " + methodName);
        try
        {
            return method.Invoke(null, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
