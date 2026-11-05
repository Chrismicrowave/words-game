using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public sealed class TimerOverlayTests
{
    private GameObject timerObject;
    private GameObject inputObject;

    [SetUp]
    public void SetUp()
    {
        Services.Register<TimerSystem>(null);
        Services.Register<InputHandler>(null);
    }

    [TearDown]
    public void TearDown()
    {
        if (timerObject != null) Object.DestroyImmediate(timerObject);
        if (inputObject != null) Object.DestroyImmediate(inputObject);
        Services.Register<TimerSystem>(null);
        Services.Register<InputHandler>(null);
    }

    [Test]
    public void OverlaySuspendAndResumeExcludesOverlayDuration()
    {
        float now = 0f;
        TimerSystem timer = CreateTimer(() => now);
        timer.StartTimer();
        now = 2f;
        Assert.IsTrue(timer.SuspendForOverlay());
        Assert.IsFalse(timer.IsRunning);
        Assert.AreEqual(2f, timer.CurrentPhaseDuration, 0.001f);

        now = 10f;
        timer.ResumeFromOverlay(true);
        Assert.IsTrue(timer.IsRunning);
        now = 11f;
        MethodInfo update = typeof(TimerSystem).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(update);
        update.Invoke(timer, null);
        Assert.AreEqual(3f, timer.CurrentPhaseDuration, 0.001f);
    }

    [Test]
    public void ResetDuringOverlayCancelsPendingTimerResume()
    {
        float now = 0f;
        TimerSystem timer = CreateTimer(() => now);
        timer.StartTimer();
        now = 2f;
        Assert.IsTrue(timer.SuspendForOverlay());
        timer.ResetAll();
        now = 20f;
        timer.ResumeFromOverlay(true);

        Assert.IsFalse(timer.IsRunning);
        Assert.IsFalse(timer.IsOverlaySuspended);
        Assert.AreEqual(0f, timer.CurrentPhaseDuration);
        Assert.AreEqual(0f, timer.TotalElapsedTime);
    }

    [Test]
    public void HubInputGateDoesNotClearLegacyModalBlock()
    {
        inputObject = new GameObject("InputHandler test");
        InputHandler input = inputObject.AddComponent<InputHandler>();
        input.SetGameplayBlocked(true);
        input.SetHubGameplayBlocked(true);
        Assert.IsTrue(input.IsHubGameplayBlocked);

        input.SetHubGameplayBlocked(false);
        Assert.IsFalse(input.IsHubGameplayBlocked);
        FieldInfo legacyGate = typeof(InputHandler).GetField("_gameplayBlocked", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(legacyGate);
        Assert.IsTrue((bool)legacyGate.GetValue(input));
    }

    private TimerSystem CreateTimer(System.Func<float> clock)
    {
        timerObject = new GameObject("TimerSystem test");
        TimerSystem timer = timerObject.AddComponent<TimerSystem>();
        timer.SetTimeSourceForTests(clock);
        return timer;
    }
}
