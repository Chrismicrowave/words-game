using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[TestFixture]
public sealed class DebugAnyKeyInputTests
{
    private GameObject inputObject;
    private InputHandler input;
    private EventSystem eventSystem;
    private EventSystem priorEventSystem;
    private GameObject ownedEventSystemObject;
    private GameObject focusRoot;
    private GameObject focusChild;
    private MethodInfo tryConsumeDebugKeyDown;

    [SetUp]
    public void SetUp()
    {
        Services.Register<InputHandler>(null);
        priorEventSystem = EventSystem.current;
        ownedEventSystemObject = new GameObject("Debug any-key EventSystem test");
        eventSystem = ownedEventSystemObject.AddComponent<EventSystem>();
        MethodInfo eventSystemOnEnable = typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
        eventSystemOnEnable?.Invoke(eventSystem, null);
        EventSystem.current = eventSystem;

        inputObject = new GameObject("Debug any-key InputHandler test");
        input = inputObject.AddComponent<InputHandler>();
        MethodInfo awake = typeof(InputHandler).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        awake?.Invoke(input, null);
        tryConsumeDebugKeyDown = typeof(InputHandler).GetMethod("TryConsumeDebugKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(tryConsumeDebugKeyDown);
    }

    [TearDown]
    public void TearDown()
    {
        if (priorEventSystem != null)
            EventSystem.current = priorEventSystem;
        if (focusRoot != null)
            Object.DestroyImmediate(focusRoot);
        if (ownedEventSystemObject != null)
            Object.DestroyImmediate(ownedEventSystemObject);
        if (inputObject != null)
            Object.DestroyImmediate(inputObject);
        Services.Register<InputHandler>(null);
    }

    [Test]
    public void PressedKeyEdgeIsConsumedOnceAndHeldFrameDoesNotRepeat()
    {
        int consumed = 0;
        input.RegisterDebugKeyDownConsumer(() => { consumed++; return true; });

        Assert.IsTrue(TryConsume(true), "An aggregated keyboard key-down edge should be consumed.");
        Assert.AreEqual(1, consumed, "Several keys in one input frame are represented by one aggregated edge.");

        Assert.IsFalse(TryConsume(false), "A held key without a new press edge must not be consumed again.");
        Assert.AreEqual(1, consumed);
    }

    [Test]
    public void DecliningConsumerDoesNotConsumeTheKeyboardFrame()
    {
        int consumed = 0;
        input.RegisterDebugKeyDownConsumer(() => { consumed++; return false; });

        Assert.IsFalse(TryConsume(true), "A declined debug action must allow the existing input path to continue.");
        Assert.AreEqual(1, consumed);
        Assert.IsFalse(TryConsume(false), "No edge means the callback is not consulted.");
        Assert.AreEqual(1, consumed);
    }

    [Test]
    public void GameplayHubAndTextInputGatesPreventDebugConsumption()
    {
        int consumed = 0;
        input.RegisterDebugKeyDownConsumer(() => { consumed++; return true; });

        input.SetGameplayBlocked(true);
        Assert.IsFalse(TryConsume(true));
        Assert.AreEqual(0, consumed);
        input.SetGameplayBlocked(false);

        input.SetHubGameplayBlocked(true);
        Assert.IsFalse(TryConsume(true));
        Assert.AreEqual(0, consumed);
        input.SetHubGameplayBlocked(false);

        CreateFocusedTextInput();
        Assert.IsFalse(TryConsume(true));
        Assert.AreEqual(0, consumed);
    }

    private bool TryConsume(bool anyKeyPressedThisFrame)
    {
        return (bool)tryConsumeDebugKeyDown.Invoke(input, new object[] { anyKeyPressedThisFrame });
    }

    private void CreateFocusedTextInput()
    {
        focusRoot = new GameObject("Text input parent");
        TMP_InputField field = focusRoot.AddComponent<TMP_InputField>();
        field.enabled = false;
        focusChild = new GameObject("Selected text input child");
        focusChild.transform.SetParent(focusRoot.transform, false);
        eventSystem.SetSelectedGameObject(focusChild);
    }
}
