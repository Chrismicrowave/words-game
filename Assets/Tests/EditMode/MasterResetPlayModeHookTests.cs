using System;
using System.Reflection;
using NUnit.Framework;

public class MasterResetPlayModeHookTests
{
    private static MethodInfo GetPathGuard()
    {
        Type hookType = Type.GetType("MasterResetPlayModeHook, Assembly-CSharp-Editor", true);
        return hookType.GetMethod("ShouldIgnoreScenePath", BindingFlags.Public | BindingFlags.Static);
    }

    [Test]
    public void MockupScenePathIsExcludedFromArmedResetHook()
    {
        Assert.IsTrue((bool)GetPathGuard().Invoke(null, new object[]
        {
            "Assets/Scenes/UI-Mockups/WordListHubMockup.unity"
        }));
    }

    [Test]
    public void NormalAndSimilarButOutOfScopeScenePathsAreNotExcluded()
    {
        MethodInfo guard = GetPathGuard();
        Assert.IsFalse((bool)guard.Invoke(null, new object[] { "Assets/Scenes/mainGame.unity" }));
        Assert.IsFalse((bool)guard.Invoke(null, new object[] { "Assets/Scenes/UI-Mockups-Archive/Old.unity" }));
        Assert.IsFalse((bool)guard.Invoke(null, new object[] { null }));
    }
}
