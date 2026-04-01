using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class WordEngineTests
{
    private WordEngine engine;

    [SetUp]
    public void SetUp()
    {
        engine = new WordEngine();
    }

    [Test]
    public void LoadWord_SimpleWord_ParsesCorrectSteps()
    {
        engine.LoadWord("Hi");
        Assert.AreEqual(2, engine.TotalSteps);
        Assert.AreEqual(0, engine.CurrentStep);
        Assert.AreEqual(KeyCode.H, engine.Steps[0].Key);
        Assert.AreEqual(StepAction.Hold, engine.Steps[0].Action);
        Assert.AreEqual(KeyCode.I, engine.Steps[1].Key);
        Assert.AreEqual(StepAction.Hold, engine.Steps[1].Action);
    }

    [Test]
    public void LoadWord_RepeatedLetter_AlternatesHoldRelease()
    {
        engine.LoadWord("Noon");
        Assert.AreEqual(4, engine.TotalSteps);
        Assert.AreEqual(StepAction.Hold, engine.Steps[0].Action);    // N
        Assert.AreEqual(StepAction.Hold, engine.Steps[1].Action);    // O
        Assert.AreEqual(StepAction.Release, engine.Steps[2].Action); // O
        Assert.AreEqual(StepAction.Release, engine.Steps[3].Action); // N
    }

    [Test]
    public void LoadWord_SkipsNonAlphanumeric()
    {
        engine.LoadWord("No Food");
        Assert.AreEqual(6, engine.TotalSteps);
    }

    [Test]
    public void LoadWord_TracksTargetTextIndex()
    {
        engine.LoadWord("No Food");
        Assert.AreEqual(0, engine.Steps[0].TargetTextIndex); // N
        Assert.AreEqual(1, engine.Steps[1].TargetTextIndex); // o
        Assert.AreEqual(3, engine.Steps[2].TargetTextIndex); // F
        Assert.AreEqual(4, engine.Steps[3].TargetTextIndex); // o
        Assert.AreEqual(5, engine.Steps[4].TargetTextIndex); // o
        Assert.AreEqual(6, engine.Steps[5].TargetTextIndex); // d
    }

    [Test]
    public void ProcessInput_CorrectHold_ReturnsCorrect()
    {
        engine.LoadWord("Hi");
        StepResult result = engine.ProcessInput(KeyCode.H, isPressed: true);
        Assert.AreEqual(StepResult.Correct, result);
        Assert.AreEqual(1, engine.CurrentStep);
    }

    [Test]
    public void ProcessInput_WrongKey_ReturnsFailed()
    {
        engine.LoadWord("Hi");
        StepResult result = engine.ProcessInput(KeyCode.X, isPressed: true);
        Assert.AreEqual(StepResult.Failed, result);
        Assert.AreEqual(0, engine.CurrentStep);
    }

    [Test]
    public void ProcessInput_CorrectKeyWrongAction_ReturnsFailed()
    {
        engine.LoadWord("Hi");
        StepResult result = engine.ProcessInput(KeyCode.H, isPressed: false);
        Assert.AreEqual(StepResult.Failed, result);
    }

    [Test]
    public void ProcessInput_LastStep_ReturnsPhaseComplete()
    {
        engine.LoadWord("Hi");
        engine.ProcessInput(KeyCode.H, isPressed: true);
        StepResult result = engine.ProcessInput(KeyCode.I, isPressed: true);
        Assert.AreEqual(StepResult.PhaseComplete, result);
    }

    [Test]
    public void ProcessInput_ReleaseStep_RequiresRelease()
    {
        engine.LoadWord("Noon");
        engine.ProcessInput(KeyCode.N, isPressed: true);
        engine.ProcessInput(KeyCode.O, isPressed: true);
        StepResult result = engine.ProcessInput(KeyCode.O, isPressed: false);
        Assert.AreEqual(StepResult.Correct, result);
    }

    [Test]
    public void GetDisplayText_ShowsProgressCorrectly()
    {
        engine.LoadWord("Hi");
        string display0 = engine.GetDisplayText(showCursor: true);
        Assert.IsTrue(display0.Contains("\u2588"));
        Assert.IsTrue(display0.Contains("_"));

        engine.ProcessInput(KeyCode.H, isPressed: true);
        string display1 = engine.GetDisplayText(showCursor: true);
        Assert.IsTrue(display1.StartsWith("H"));
    }

    [Test]
    public void GetFailureMessage_ReturnsExpectedAction()
    {
        engine.LoadWord("Hi");
        engine.ProcessInput(KeyCode.X, isPressed: true);
        string msg = engine.LastFailureMessage;
        Assert.IsTrue(msg.Contains("hold"));
        Assert.IsTrue(msg.Contains("H"));
    }

    [Test]
    public void Reset_ClearsProgress()
    {
        engine.LoadWord("Hi");
        engine.ProcessInput(KeyCode.H, isPressed: true);
        engine.Reset();
        Assert.AreEqual(0, engine.CurrentStep);
    }

    [Test]
    public void LoadWord_WithDigits_ParsesCorrectly()
    {
        engine.LoadWord("A1B");
        Assert.AreEqual(3, engine.TotalSteps);
        Assert.AreEqual(KeyCode.A, engine.Steps[0].Key);
        Assert.AreEqual(KeyCode.Alpha1, engine.Steps[1].Key);
        Assert.AreEqual(KeyCode.B, engine.Steps[2].Key);
    }
}
