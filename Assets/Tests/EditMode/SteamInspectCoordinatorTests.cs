using System;
using NUnit.Framework;

public class SteamInspectCoordinatorTests
{
    [Test]
    public void Inspect_SynchronousSuccess_CompletesAndKeepsTheResult()
    {
        var coordinator = new SteamInspectCoordinator();
        int changes = 0;
        coordinator.StateChanged += () => changes++;

        coordinator.Inspect(complete => complete(true, "test-steam-id", "Account inspected."));

        Assert.That(coordinator.IsChecking, Is.False);
        Assert.That(coordinator.InspectedSteamId, Is.EqualTo("test-steam-id"));
        Assert.That(coordinator.Message, Is.EqualTo("Account inspected."));
        Assert.That(changes, Is.EqualTo(2), "The UI should see checking before a synchronous completion, then the final result.");
    }

    [Test]
    public void Inspect_SynchronousFailure_IsNotOverwrittenByCheckingState()
    {
        var coordinator = new SteamInspectCoordinator();

        coordinator.Inspect(complete => complete(false, null, "Steam initialization failed."));

        Assert.That(coordinator.IsChecking, Is.False);
        Assert.That(coordinator.InspectedSteamId, Is.Null);
        Assert.That(coordinator.Message, Is.EqualTo("Steam initialization failed."));
    }

    [Test]
    public void Inspect_AsynchronousCompletion_ChangesFromCheckingToResult()
    {
        var coordinator = new SteamInspectCoordinator();
        Action<bool, string, string> complete = null;

        coordinator.Inspect(callback => complete = callback);

        Assert.That(coordinator.IsChecking, Is.True);
        Assert.That(coordinator.Message, Is.EqualTo(SteamInspectCoordinator.CheckingMessage));

        complete(true, "test-steam-id", "Account inspected asynchronously.");

        Assert.That(coordinator.IsChecking, Is.False);
        Assert.That(coordinator.InspectedSteamId, Is.EqualTo("test-steam-id"));
        Assert.That(coordinator.Message, Is.EqualTo("Account inspected asynchronously."));
    }

    [Test]
    public void Inspect_InvokerThrows_CompletesWithFailure()
    {
        var coordinator = new SteamInspectCoordinator();

        coordinator.Inspect(_ => throw new InvalidOperationException("reflection failed"));

        Assert.That(coordinator.IsChecking, Is.False);
        Assert.That(coordinator.InspectedSteamId, Is.Null);
        Assert.That(coordinator.Message, Does.Contain("reflection failed"));
    }

    [Test]
    public void Inspect_MissingHelper_CompletesWithUnavailableMessage()
    {
        var coordinator = new SteamInspectCoordinator();

        coordinator.Inspect(null);

        Assert.That(coordinator.IsChecking, Is.False);
        Assert.That(coordinator.InspectedSteamId, Is.Null);
        Assert.That(coordinator.Message, Does.Contain("maintenance is unavailable"));
    }

    [Test]
    public void Cancel_OldWindowIgnoresLateCallbackAfterWindowRecreation()
    {
        var oldWindowCoordinator = new SteamInspectCoordinator();
        Action<bool, string, string> lateCompletion = null;
        oldWindowCoordinator.Inspect(callback => lateCompletion = callback);

        oldWindowCoordinator.Cancel();

        var newWindowCoordinator = new SteamInspectCoordinator();
        newWindowCoordinator.RestoreDiagnostic("Previous inspection ended after the window closed.");
        lateCompletion(true, "late-id", "Late callback from old window.");

        Assert.That(oldWindowCoordinator.IsChecking, Is.False);
        Assert.That(oldWindowCoordinator.InspectedSteamId, Is.Null);
        Assert.That(newWindowCoordinator.InspectedSteamId, Is.Null, "A stale callback must not authorize a reset in the recreated window.");
        Assert.That(newWindowCoordinator.Message, Is.EqualTo("Previous inspection ended after the window closed."));
    }
}
