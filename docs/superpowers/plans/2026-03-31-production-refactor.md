# Production Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the monolithic GameManager into focused systems with New Input System, settings persistence, word list file I/O, and integration touch points for Steam leaderboards and daily word lists.

**Architecture:** Event-driven MonoBehaviour systems connected by C# events. A central GameStateManager owns a state machine and raises transition events. Systems subscribe to relevant events and operate independently. WordEngine is a pure C# class for testability.

**Tech Stack:** Unity 6 (6000.3.11f1), C#, New Input System 1.19.0, TextMesh Pro, Unity Audio Mixer, NUnit (Unity Test Framework 1.6.0)

---

## File Map

### New files to create:
| File | Responsibility |
|------|---------------|
| `Assets/-Scripts/Core/GameState.cs` | State enum and Step/StepResult types |
| `Assets/-Scripts/Core/GameStateManager.cs` | State machine, C# events, central coordinator |
| `Assets/-Scripts/Core/WordEngine.cs` | Pure C# — step parsing, input validation, display text |
| `Assets/-Scripts/Core/InputHandler.cs` | New Input System wrapper, key events |
| `Assets/-Scripts/Core/PhaseManager.cs` | Phase list, progression, word list provider integration |
| `Assets/-Scripts/Core/TimerSystem.cs` | Phase + total timer |
| `Assets/-Scripts/Core/SettingsManager.cs` | PlayerPrefs persistence for audio/display/gameplay |
| `Assets/-Scripts/Feedback/FeedbackController.cs` | Orchestrates shake, zoom, audio effects |
| `Assets/-Scripts/UI/UIController.cs` | Text display, cursor blink, status text, phase list UI |
| `Assets/-Scripts/UI/KeyboardVisualController.cs` | Key color states via serialized list |
| `Assets/-Scripts/WordList/IWordListProvider.cs` | Interface for word list sources |
| `Assets/-Scripts/WordList/FixedWordListProvider.cs` | Hardcoded demo word lists |
| `Assets/-Scripts/WordList/FileWordListProvider.cs` | JSON file load/save from persistentDataPath |
| `Assets/-Scripts/WordList/DailyWordListProvider.cs` | Stub for future server-fetched lists |
| `Assets/-Scripts/Leaderboard/ILeaderboardService.cs` | Interface + null implementation |
| `Assets/-Scripts/Audio/AudioManager.cs` | Existing + AudioMixer group support |
| `Assets/Audio/MainMixer.mixer` | Unity Audio Mixer asset (created in editor) |
| `Assets/Input/WordGameActions.inputactions` | New Input Action Asset for this game |
| `Assets/Tests/EditMode/WordEngineTests.cs` | Unit tests for WordEngine |

### Files to move (no logic changes):
| From | To |
|------|-----|
| `Assets/-Scripts/CamShake.cs` | `Assets/-Scripts/Feedback/CameraShakeAndZoom.cs` |
| `Assets/-Scripts/KeyboardShake.cs` | `Assets/-Scripts/Feedback/KeyboardShake.cs` |
| `Assets/-Scripts/CurTextTMPanim.cs` | `Assets/-Scripts/UI/CurTextTMPanim.cs` |
| `Assets/-Scripts/ScrollText.cs` | `Assets/-Scripts/UI/CircularScrollingText.cs` |
| `Assets/-Scripts/MenuAnimOnOff.cs` | `Assets/-Scripts/UI/MenuAnimOnOff.cs` |
| `Assets/-Scripts/Screenshot.cs` | `Assets/-Scripts/Utility/Screenshot.cs` |
| `Assets/-Scripts/AudioManager.cs` | `Assets/-Scripts/Audio/AudioManager.cs` |

### Files to delete after refactor:
| File | Reason |
|------|--------|
| `Assets/-Scripts/GameManager.cs` | Fully replaced by new systems |
| `Assets/InputSystem_Actions.inputactions` | Replaced by WordGameActions.inputactions |

---

## Task 1: Create Folder Structure and Move Existing Files

**Files:**
- Move: all files listed in "Files to move" table above
- Create: folder structure for Core/, Feedback/, UI/, WordList/, Leaderboard/, Audio/, Utility/

This task reorganizes files without changing any code. Unity will update .meta GUIDs automatically when moved within the editor, but since we're moving via filesystem we need to move .meta files too.

- [ ] **Step 1: Create the folder structure**

```bash
cd "D:\Files\Desktop\Unity\Projects\GDS4 Game-a-Week\02-words\words"
mkdir -p Assets/-Scripts/Core
mkdir -p Assets/-Scripts/Feedback
mkdir -p Assets/-Scripts/UI
mkdir -p Assets/-Scripts/WordList
mkdir -p Assets/-Scripts/Leaderboard
mkdir -p Assets/-Scripts/Audio
mkdir -p Assets/-Scripts/Utility
mkdir -p Assets/Input
mkdir -p Assets/Tests/EditMode
```

- [ ] **Step 2: Move files with their .meta files**

Move each script and its .meta to the new location. Use `git mv` to preserve history:

```bash
cd "D:\Files\Desktop\Unity\Projects\GDS4 Game-a-Week\02-words\words"

# Feedback
git mv "Assets/-Scripts/CamShake.cs" "Assets/-Scripts/Feedback/CameraShakeAndZoom.cs"
git mv "Assets/-Scripts/CamShake.cs.meta" "Assets/-Scripts/Feedback/CameraShakeAndZoom.cs.meta"
git mv "Assets/-Scripts/KeyboardShake.cs" "Assets/-Scripts/Feedback/KeyboardShake.cs"
git mv "Assets/-Scripts/KeyboardShake.cs.meta" "Assets/-Scripts/Feedback/KeyboardShake.cs.meta"

# UI
git mv "Assets/-Scripts/CurTextTMPanim.cs" "Assets/-Scripts/UI/CurTextTMPanim.cs"
git mv "Assets/-Scripts/CurTextTMPanim.cs.meta" "Assets/-Scripts/UI/CurTextTMPanim.cs.meta"
git mv "Assets/-Scripts/ScrollText.cs" "Assets/-Scripts/UI/CircularScrollingText.cs"
git mv "Assets/-Scripts/ScrollText.cs.meta" "Assets/-Scripts/UI/CircularScrollingText.cs.meta"
git mv "Assets/-Scripts/MenuAnimOnOff.cs" "Assets/-Scripts/UI/MenuAnimOnOff.cs"
git mv "Assets/-Scripts/MenuAnimOnOff.cs.meta" "Assets/-Scripts/UI/MenuAnimOnOff.cs.meta"

# Audio
git mv "Assets/-Scripts/AudioManager.cs" "Assets/-Scripts/Audio/AudioManager.cs"
git mv "Assets/-Scripts/AudioManager.cs.meta" "Assets/-Scripts/Audio/AudioManager.cs.meta"

# Utility
git mv "Assets/-Scripts/Screenshot.cs" "Assets/-Scripts/Utility/Screenshot.cs"
git mv "Assets/-Scripts/Screenshot.cs.meta" "Assets/-Scripts/Utility/Screenshot.cs.meta"
```

- [ ] **Step 3: Verify no compile errors**

Open Unity Editor (or check compile via Coplay MCP `check_compile_errors`). All scripts should compile — we only moved files, no code changed. The only script references that matter are in the scene's serialized data, and Unity tracks by GUID in .meta files, not by path.

- [ ] **Step 4: Commit**

```bash
git add -A Assets/-Scripts/
git commit -m "refactor: reorganize scripts into subfolder structure

Move existing scripts to Core/, Feedback/, UI/, Audio/, Utility/ folders.
No logic changes — file moves only."
```

---

## Task 2: GameState Types (Shared Enums and Structs)

**Files:**
- Create: `Assets/-Scripts/Core/GameState.cs`

Define the shared types that multiple systems will reference.

- [ ] **Step 1: Create GameState.cs**

```csharp
// Assets/-Scripts/Core/GameState.cs
using UnityEngine;

public enum GameState
{
    Idle,
    Playing,
    PhaseFailed,
    PhaseComplete,
    AllComplete
}

public enum StepAction
{
    Hold,
    Release
}

public enum StepResult
{
    Correct,
    Failed,
    PhaseComplete
}

[System.Serializable]
public struct Step
{
    public KeyCode Key;
    public StepAction Action;
    public string Letter;
    public int TargetTextIndex;
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/-Scripts/Core/GameState.cs
git commit -m "feat: add shared game state enums and Step struct"
```

---

## Task 3: WordEngine (Pure C# — TDD)

**Files:**
- Create: `Assets/-Scripts/Core/WordEngine.cs`
- Create: `Assets/Tests/EditMode/WordEngineTests.cs`
- Create: `Assets/Tests/EditMode/EditMode.asmdef`
- Create: `Assets/-Scripts/Core/Core.asmdef` (needed for test reference)

WordEngine is a pure C# class with no MonoBehaviour dependency. It handles step parsing, input validation, and display text generation — the core game logic extracted from GameManager.

- [ ] **Step 1: Create assembly definitions for testability**

Unity requires assembly definitions for the test framework to reference game code.

Create `Assets/-Scripts/Core/Core.asmdef`:
```json
{
    "name": "Core",
    "rootNamespace": "",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Create `Assets/Tests/EditMode/EditMode.asmdef`:
```json
{
    "name": "EditMode",
    "rootNamespace": "",
    "references": [
        "GUID:<GUID_OF_CORE_ASMDEF>"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Note: After creating `Core.asmdef`, open it in Unity to get its GUID from the .meta file, then paste that GUID into `EditMode.asmdef`'s references array.

- [ ] **Step 2: Write failing tests for WordEngine**

Create `Assets/Tests/EditMode/WordEngineTests.cs`:
```csharp
using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

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

        // H = first occurrence = Hold
        Assert.AreEqual(KeyCode.H, engine.Steps[0].Key);
        Assert.AreEqual(StepAction.Hold, engine.Steps[0].Action);

        // I = first occurrence = Hold
        Assert.AreEqual(KeyCode.I, engine.Steps[1].Key);
        Assert.AreEqual(StepAction.Hold, engine.Steps[1].Action);
    }

    [Test]
    public void LoadWord_RepeatedLetter_AlternatesHoldRelease()
    {
        engine.LoadWord("Noon");

        Assert.AreEqual(4, engine.TotalSteps);

        // N(1) = Hold, O(1) = Hold, O(2) = Release, N(2) = Release
        Assert.AreEqual(StepAction.Hold, engine.Steps[0].Action);    // N
        Assert.AreEqual(StepAction.Hold, engine.Steps[1].Action);    // O
        Assert.AreEqual(StepAction.Release, engine.Steps[2].Action); // O
        Assert.AreEqual(StepAction.Release, engine.Steps[3].Action); // N
    }

    [Test]
    public void LoadWord_SkipsNonAlphanumeric()
    {
        engine.LoadWord("No Food");

        // N, O, F, O, O, D = 6 steps (space skipped)
        Assert.AreEqual(6, engine.TotalSteps);
    }

    [Test]
    public void LoadWord_TracksTargetTextIndex()
    {
        engine.LoadWord("No Food");

        // 'N' is at index 0, 'o' at 1, 'F' at 3, second 'o' at 4, third 'o' at 5, 'D' at 6
        // (space at index 2 is skipped)
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
        Assert.AreEqual(0, engine.CurrentStep); // no advancement
    }

    [Test]
    public void ProcessInput_CorrectKeyWrongAction_ReturnsFailed()
    {
        engine.LoadWord("Hi");

        // H should be Hold (pressed=true), but we send released
        StepResult result = engine.ProcessInput(KeyCode.H, isPressed: false);

        Assert.AreEqual(StepResult.Failed, result);
    }

    [Test]
    public void ProcessInput_LastStep_ReturnsPhaseComplete()
    {
        engine.LoadWord("Hi");

        engine.ProcessInput(KeyCode.H, isPressed: true);  // step 0
        StepResult result = engine.ProcessInput(KeyCode.I, isPressed: true);  // step 1 (last)

        Assert.AreEqual(StepResult.PhaseComplete, result);
    }

    [Test]
    public void ProcessInput_ReleaseStep_RequiresRelease()
    {
        engine.LoadWord("Noon");

        engine.ProcessInput(KeyCode.N, isPressed: true);   // N Hold
        engine.ProcessInput(KeyCode.O, isPressed: true);   // O Hold
        StepResult result = engine.ProcessInput(KeyCode.O, isPressed: false); // O Release

        Assert.AreEqual(StepResult.Correct, result);
    }

    [Test]
    public void GetDisplayText_ShowsProgressCorrectly()
    {
        engine.LoadWord("Hi");

        // Before any input — cursor on H, underscore on I
        string display0 = engine.GetDisplayText(showCursor: true);
        // Should have cursor char at position 0, underscore at position 1
        Assert.IsTrue(display0.Contains("\u2588")); // block char present
        Assert.IsTrue(display0.Contains("_"));

        // After matching H
        engine.ProcessInput(KeyCode.H, isPressed: true);
        string display1 = engine.GetDisplayText(showCursor: true);
        // H revealed, cursor on I
        Assert.IsTrue(display1.StartsWith("H"));
    }

    [Test]
    public void GetFailureMessage_ReturnsExpectedAction()
    {
        engine.LoadWord("Hi");

        engine.ProcessInput(KeyCode.X, isPressed: true); // fail

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
```

- [ ] **Step 3: Run tests to verify they fail**

Run via Unity Test Runner or command line:
```bash
# From Unity Editor: Window > General > Test Runner > EditMode > Run All
```
Expected: All tests fail with "WordEngine not found" or similar.

- [ ] **Step 4: Implement WordEngine**

Create `Assets/-Scripts/Core/WordEngine.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public class WordEngine
{
    private readonly List<Step> steps = new List<Step>();
    private readonly Dictionary<string, int> letterOccurrences = new Dictionary<string, int>();
    private string targetText = "";
    private static readonly string blockChar = "<size=75%>\u2588</size>";

    public int CurrentStep { get; private set; }
    public int TotalSteps => steps.Count;
    public ReadOnlyCollection<Step> Steps => steps.AsReadOnly();
    public string TargetText => targetText;
    public string LastFailureMessage { get; private set; } = "";
    public bool IsComplete => CurrentStep >= TotalSteps;

    public void LoadWord(string word)
    {
        targetText = word;
        CurrentStep = 0;
        LastFailureMessage = "";
        ParseSteps();
    }

    public void Reset()
    {
        CurrentStep = 0;
        LastFailureMessage = "";
    }

    private void ParseSteps()
    {
        steps.Clear();
        letterOccurrences.Clear();

        for (int i = 0; i < targetText.Length; i++)
        {
            char c = targetText[i];
            if (!char.IsLetterOrDigit(c))
                continue;

            string letter = c.ToString().ToUpper();

            if (!letterOccurrences.ContainsKey(letter))
                letterOccurrences[letter] = 0;
            letterOccurrences[letter]++;

            StepAction action = (letterOccurrences[letter] % 2 == 1)
                ? StepAction.Hold
                : StepAction.Release;

            KeyCode key = ParseKeyCode(letter);
            if (key != KeyCode.None)
            {
                steps.Add(new Step
                {
                    Key = key,
                    Action = action,
                    Letter = letter,
                    TargetTextIndex = i
                });
            }
        }
    }

    private KeyCode ParseKeyCode(string letter)
    {
        // Handle digits: "0"-"9" → KeyCode.Alpha0-Alpha9
        if (letter.Length == 1 && char.IsDigit(letter[0]))
        {
            return KeyCode.Alpha0 + (letter[0] - '0');
        }

        // Handle letters: "A"-"Z" → KeyCode.A-KeyCode.Z
        if (Enum.TryParse(letter, out KeyCode key))
            return key;

        return KeyCode.None;
    }

    public StepResult ProcessInput(KeyCode key, bool isPressed)
    {
        if (CurrentStep >= steps.Count)
            return StepResult.Failed;

        Step step = steps[CurrentStep];
        bool actionMatches = (step.Action == StepAction.Hold && isPressed)
                          || (step.Action == StepAction.Release && !isPressed);

        if (key == step.Key && actionMatches)
        {
            CurrentStep++;
            LastFailureMessage = "";

            if (CurrentStep >= steps.Count)
                return StepResult.PhaseComplete;

            return StepResult.Correct;
        }

        // Build failure message
        string expectedAction = step.Action == StepAction.Hold ? "hold" : "release";
        LastFailureMessage = $"Expected to {expectedAction} '{step.Letter}', but got '{key}'";
        return StepResult.Failed;
    }

    public string GetDisplayText(bool showCursor)
    {
        string display = "";
        int stepIndex = 0;

        foreach (char c in targetText)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (stepIndex < CurrentStep)
                {
                    display += c; // revealed
                }
                else if (stepIndex == CurrentStep)
                {
                    display += showCursor ? blockChar : " ";
                }
                else
                {
                    display += "_";
                }
                stepIndex++;
            }
            else
            {
                display += c; // spaces, punctuation
            }
        }

        return display;
    }

    public string GetActionPrompt()
    {
        if (CurrentStep >= steps.Count)
            return "";

        Step step = steps[CurrentStep];
        string action = step.Action == StepAction.Hold ? "Hold" : "Release";
        char targetChar = targetText[step.TargetTextIndex];
        return $"{action} '{targetChar}'";
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run via Unity Test Runner: Window > General > Test Runner > EditMode > Run All.
Expected: All 12 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/-Scripts/Core/GameState.cs Assets/-Scripts/Core/WordEngine.cs Assets/-Scripts/Core/Core.asmdef Assets/Tests/EditMode/
git commit -m "feat: extract WordEngine with unit tests

Pure C# class handling step parsing, hold/release validation,
and display text generation. 12 unit tests covering parsing,
input processing, display, and edge cases."
```

---

## Task 4: GameStateManager

**Files:**
- Create: `Assets/-Scripts/Core/GameStateManager.cs`

The central state machine that coordinates all other systems via C# events.

- [ ] **Step 1: Create GameStateManager.cs**

```csharp
// Assets/-Scripts/Core/GameStateManager.cs
using System;
using UnityEngine;

public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance { get; private set; }

    public GameState CurrentState { get; private set; } = GameState.Idle;

    // State transition events
    public event Action<GameState, GameState> OnStateChanged;
    public event Action OnPhaseStarted;
    public event Action<StepResult, Step> OnStepProcessed;
    public event Action OnPhaseCompleted;
    public event Action OnPhaseFailed;
    public event Action OnPhaseRestarted;
    public event Action OnAllPhasesCompleted;
    public event Action OnGameReset;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    public void TransitionTo(GameState newState)
    {
        if (newState == CurrentState) return;

        GameState oldState = CurrentState;
        CurrentState = newState;
        OnStateChanged?.Invoke(oldState, newState);

        switch (newState)
        {
            case GameState.Playing:
                OnPhaseStarted?.Invoke();
                break;
            case GameState.PhaseComplete:
                OnPhaseCompleted?.Invoke();
                break;
            case GameState.PhaseFailed:
                OnPhaseFailed?.Invoke();
                break;
            case GameState.AllComplete:
                OnAllPhasesCompleted?.Invoke();
                break;
        }
    }

    public void RaiseStepProcessed(StepResult result, Step step)
    {
        OnStepProcessed?.Invoke(result, step);
    }

    public void RaisePhaseRestarted()
    {
        OnPhaseRestarted?.Invoke();
    }

    public void RaiseGameReset()
    {
        OnGameReset?.Invoke();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/-Scripts/Core/GameStateManager.cs
git commit -m "feat: add GameStateManager with state machine and C# events"
```

---

## Task 5: InputHandler (New Input System)

**Files:**
- Create: `Assets/Input/WordGameActions.inputactions`
- Create: `Assets/-Scripts/Core/InputHandler.cs`

Migrates from legacy `Input.GetKeyDown/GetKeyUp` to the New Input System. The input actions asset defines per-key actions that detect both press and release.

- [ ] **Step 1: Create the Input Action Asset**

Create `Assets/Input/WordGameActions.inputactions` using the Coplay MCP `create_input_action_asset` tool, or manually create the JSON. The asset needs:

**Action Map: "Gameplay"**
- One action per letter key (A-Z) — type: Button, interactions: Press(behavior=2) for PressAndRelease
- One action per digit key (0-9) — same config
- "Backspace" action — type: Button, bound to `<Keyboard>/backspace`
- "Enter" action — type: Button, bound to `<Keyboard>/enter`

Since defining 38 individual actions in JSON is verbose, we'll create this programmatically in InputHandler and use a runtime-built action map instead. This is cleaner for a per-key game.

- [ ] **Step 2: Create InputHandler.cs**

```csharp
// Assets/-Scripts/Core/InputHandler.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.EventSystems;

public class InputHandler : MonoBehaviour
{
    public static InputHandler Instance { get; private set; }

    public event Action<KeyCode, bool> OnKeyAction; // key, isPressed
    public event Action OnBackspacePressed;
    public event Action OnEnterPressed;

    [SerializeField] private TMP_InputField uiInputField; // blocks gameplay input when focused

    private Keyboard keyboard;

    // Map Keyboard keys to KeyCode for compatibility with WordEngine
    private readonly Dictionary<Key, KeyCode> keyToKeyCode = new Dictionary<Key, KeyCode>();

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        BuildKeyMap();
    }

    void BuildKeyMap()
    {
        // Letters
        keyToKeyCode[Key.A] = KeyCode.A; keyToKeyCode[Key.B] = KeyCode.B;
        keyToKeyCode[Key.C] = KeyCode.C; keyToKeyCode[Key.D] = KeyCode.D;
        keyToKeyCode[Key.E] = KeyCode.E; keyToKeyCode[Key.F] = KeyCode.F;
        keyToKeyCode[Key.G] = KeyCode.G; keyToKeyCode[Key.H] = KeyCode.H;
        keyToKeyCode[Key.I] = KeyCode.I; keyToKeyCode[Key.J] = KeyCode.J;
        keyToKeyCode[Key.K] = KeyCode.K; keyToKeyCode[Key.L] = KeyCode.L;
        keyToKeyCode[Key.M] = KeyCode.M; keyToKeyCode[Key.N] = KeyCode.N;
        keyToKeyCode[Key.O] = KeyCode.O; keyToKeyCode[Key.P] = KeyCode.P;
        keyToKeyCode[Key.Q] = KeyCode.Q; keyToKeyCode[Key.R] = KeyCode.R;
        keyToKeyCode[Key.S] = KeyCode.S; keyToKeyCode[Key.T] = KeyCode.T;
        keyToKeyCode[Key.U] = KeyCode.U; keyToKeyCode[Key.V] = KeyCode.V;
        keyToKeyCode[Key.W] = KeyCode.W; keyToKeyCode[Key.X] = KeyCode.X;
        keyToKeyCode[Key.Y] = KeyCode.Y; keyToKeyCode[Key.Z] = KeyCode.Z;

        // Digits
        keyToKeyCode[Key.Digit0] = KeyCode.Alpha0;
        keyToKeyCode[Key.Digit1] = KeyCode.Alpha1;
        keyToKeyCode[Key.Digit2] = KeyCode.Alpha2;
        keyToKeyCode[Key.Digit3] = KeyCode.Alpha3;
        keyToKeyCode[Key.Digit4] = KeyCode.Alpha4;
        keyToKeyCode[Key.Digit5] = KeyCode.Alpha5;
        keyToKeyCode[Key.Digit6] = KeyCode.Alpha6;
        keyToKeyCode[Key.Digit7] = KeyCode.Alpha7;
        keyToKeyCode[Key.Digit8] = KeyCode.Alpha8;
        keyToKeyCode[Key.Digit9] = KeyCode.Alpha9;
    }

    private bool IsUIFocused()
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected == null) return false;
        return selected.GetComponent<TMPro.TMP_InputField>() != null;
    }

    void Update()
    {
        keyboard = Keyboard.current;
        if (keyboard == null || IsUIFocused()) return;

        // Check backspace
        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            OnBackspacePressed?.Invoke();
            return;
        }

        // Check enter
        if (keyboard.enterKey.wasPressedThisFrame)
        {
            OnEnterPressed?.Invoke();
            return;
        }

        // Check all mapped keys for press and release
        foreach (var kvp in keyToKeyCode)
        {
            KeyControl keyControl = keyboard[kvp.Key];

            if (keyControl.wasPressedThisFrame)
            {
                OnKeyAction?.Invoke(kvp.Value, true);
                return; // process one key event per frame
            }

            if (keyControl.wasReleasedThisFrame)
            {
                OnKeyAction?.Invoke(kvp.Value, false);
                return;
            }
        }
    }
}
```

Note: We use `TMPro.TMP_InputField` in the `using` — add `using TMPro;` at the top if the asmdef has the TMP reference. Actually, since we check by component type, we need the TMPro reference. The `uiInputField` field is unused for now (we use `IsUIFocused()` which checks the EventSystem), but kept for explicit assignment if needed later. Remove the `[SerializeField]` field and the `using TMPro` — the `IsUIFocused` approach is cleaner:

Revised `IsUIFocused`:
```csharp
    private bool IsUIFocused()
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        return selected != null && selected.GetComponent<UnityEngine.UI.InputField>() != null
            || selected != null && selected.GetComponent<TMPro.TMP_InputField>() != null;
    }
```

- [ ] **Step 3: Verify compilation**

Check compile via Coplay MCP or open Unity Editor. Expected: compiles with no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/-Scripts/Core/InputHandler.cs
git commit -m "feat: add InputHandler using New Input System

Reads keyboard state per-frame, maps Key enum to KeyCode for
WordEngine compatibility. Blocks gameplay input when UI input
fields are focused."
```

---

## Task 6: PhaseManager + WordListProviders

**Files:**
- Create: `Assets/-Scripts/WordList/IWordListProvider.cs`
- Create: `Assets/-Scripts/WordList/FixedWordListProvider.cs`
- Create: `Assets/-Scripts/WordList/FileWordListProvider.cs`
- Create: `Assets/-Scripts/WordList/DailyWordListProvider.cs`
- Create: `Assets/-Scripts/Core/PhaseManager.cs`

- [ ] **Step 1: Create IWordListProvider.cs**

```csharp
// Assets/-Scripts/WordList/IWordListProvider.cs
using System.Collections.Generic;

public interface IWordListProvider
{
    string DisplayName { get; }
    List<string> GetWords();
    bool IsEditable { get; }
}
```

- [ ] **Step 2: Create FixedWordListProvider.cs**

```csharp
// Assets/-Scripts/WordList/FixedWordListProvider.cs
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FixedWordList", menuName = "Words/Fixed Word List")]
public class FixedWordListProvider : ScriptableObject, IWordListProvider
{
    [SerializeField] private string listName = "Demo List";
    [SerializeField] private List<string> words = new List<string> { "No Food" };

    public string DisplayName => listName;
    public bool IsEditable => false;

    public List<string> GetWords()
    {
        return new List<string>(words);
    }
}
```

- [ ] **Step 3: Create FileWordListProvider.cs**

```csharp
// Assets/-Scripts/WordList/FileWordListProvider.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class FileWordListProvider : IWordListProvider
{
    public string DisplayName { get; private set; }
    public bool IsEditable => true;
    public string FilePath { get; private set; }

    private List<string> words = new List<string>();

    [Serializable]
    private class WordListData
    {
        public string name;
        public List<string> words;
        public string createdAt;
    }

    public FileWordListProvider(string filePath)
    {
        FilePath = filePath;
        Load();
    }

    public List<string> GetWords()
    {
        return new List<string>(words);
    }

    public void SetWords(List<string> newWords)
    {
        words = new List<string>(newWords);
    }

    public void SetName(string name)
    {
        DisplayName = name;
    }

    public void Save()
    {
        var data = new WordListData
        {
            name = DisplayName,
            words = words,
            createdAt = DateTime.UtcNow.ToString("o")
        };

        string dir = Path.GetDirectoryName(FilePath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(FilePath, json);
    }

    public void Load()
    {
        if (!File.Exists(FilePath))
        {
            DisplayName = "New List";
            words = new List<string>();
            return;
        }

        string json = File.ReadAllText(FilePath);
        var data = JsonUtility.FromJson<WordListData>(json);
        DisplayName = data.name ?? "Untitled";
        words = data.words ?? new List<string>();
    }

    public static string GetWordListDirectory()
    {
        return Path.Combine(Application.persistentDataPath, "WordLists");
    }

    public static List<string> GetAllWordListFiles()
    {
        string dir = GetWordListDirectory();
        if (!Directory.Exists(dir))
            return new List<string>();

        var files = new List<string>(Directory.GetFiles(dir, "*.json"));
        return files;
    }
}
```

- [ ] **Step 4: Create DailyWordListProvider.cs (stub)**

```csharp
// Assets/-Scripts/WordList/DailyWordListProvider.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stub for future daily word list integration.
/// Will fetch curated word lists from a server API.
/// For now, returns a placeholder list.
/// </summary>
public class DailyWordListProvider : IWordListProvider
{
    public string DisplayName => "Daily Challenge";
    public bool IsEditable => false;

    // TODO: Replace with HTTP fetch from daily list API
    // Expected endpoint: GET /api/daily-words?date=YYYY-MM-DD
    // Response: { "name": "Daily - March 31", "words": [...] }
    public List<string> GetWords()
    {
        return new List<string>
        {
            "Daily",
            "Challenge",
            "Coming Soon"
        };
    }
}
```

- [ ] **Step 5: Create PhaseManager.cs**

```csharp
// Assets/-Scripts/Core/PhaseManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public class PhaseManager : MonoBehaviour
{
    public static PhaseManager Instance { get; private set; }

    [SerializeField] private FixedWordListProvider defaultWordList;

    private IWordListProvider activeProvider;
    private List<string> words = new List<string>();

    public int CurrentPhaseIndex { get; private set; }
    public string CurrentWord => (CurrentPhaseIndex < words.Count) ? words[CurrentPhaseIndex] : "";
    public int TotalPhases => words.Count;
    public bool HasMorePhases => CurrentPhaseIndex < words.Count - 1;
    public IWordListProvider ActiveProvider => activeProvider;
    public List<string> Words => words;

    public event Action<string> OnPhaseWordChanged;
    public event Action OnWordListChanged;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        if (defaultWordList != null)
            LoadWordList(defaultWordList);
    }

    public void LoadWordList(IWordListProvider provider)
    {
        activeProvider = provider;
        words = provider.GetWords();
        CurrentPhaseIndex = 0;
        OnWordListChanged?.Invoke();
        OnPhaseWordChanged?.Invoke(CurrentWord);
    }

    public bool AdvancePhase()
    {
        if (CurrentPhaseIndex >= words.Count - 1)
            return false;

        CurrentPhaseIndex++;
        OnPhaseWordChanged?.Invoke(CurrentWord);
        return true;
    }

    public void RestartPhase()
    {
        OnPhaseWordChanged?.Invoke(CurrentWord);
    }

    public void ResetToBeginning()
    {
        CurrentPhaseIndex = 0;
        OnPhaseWordChanged?.Invoke(CurrentWord);
    }

    public void JumpToPhase(int index)
    {
        if (index >= 0 && index < words.Count)
        {
            CurrentPhaseIndex = index;
            OnPhaseWordChanged?.Invoke(CurrentWord);
        }
    }

    // Phase list editing (for UI)
    public void AddPhase(string word, int index = 0)
    {
        words.Insert(index, word);
        OnWordListChanged?.Invoke();
    }

    public void RemovePhase(int index)
    {
        if (index < 0 || index >= words.Count) return;

        words.RemoveAt(index);
        if (CurrentPhaseIndex >= words.Count)
            CurrentPhaseIndex = Mathf.Max(0, words.Count - 1);
        OnWordListChanged?.Invoke();
    }

    public void MovePhase(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= words.Count) return;
        if (toIndex < 0 || toIndex >= words.Count) return;

        string word = words[fromIndex];
        words.RemoveAt(fromIndex);
        words.Insert(toIndex, word);
        OnWordListChanged?.Invoke();
    }

    public void SaveCurrentList()
    {
        if (activeProvider is FileWordListProvider fileProvider)
        {
            fileProvider.SetWords(words);
            fileProvider.Save();
        }
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add Assets/-Scripts/WordList/ Assets/-Scripts/Core/PhaseManager.cs
git commit -m "feat: add PhaseManager and word list providers

IWordListProvider interface with three implementations:
- FixedWordListProvider (ScriptableObject, demo lists)
- FileWordListProvider (JSON file I/O, player custom lists)
- DailyWordListProvider (stub for future API integration)"
```

---

## Task 7: TimerSystem

**Files:**
- Create: `Assets/-Scripts/Core/TimerSystem.cs`

- [ ] **Step 1: Create TimerSystem.cs**

```csharp
// Assets/-Scripts/Core/TimerSystem.cs
using System;
using UnityEngine;

public class TimerSystem : MonoBehaviour
{
    public static TimerSystem Instance { get; private set; }

    public float CurrentPhaseDuration { get; private set; }
    public float TotalElapsedTime { get; private set; }
    public bool IsRunning { get; private set; }

    private float phaseStartTime;

    public event Action<float, float> OnTimerUpdated; // phaseDuration, totalElapsed

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Update()
    {
        if (IsRunning)
        {
            CurrentPhaseDuration = Time.time - phaseStartTime;
            OnTimerUpdated?.Invoke(CurrentPhaseDuration, TotalElapsedTime + CurrentPhaseDuration);
        }
    }

    public void StartTimer()
    {
        if (!IsRunning)
        {
            IsRunning = true;
            phaseStartTime = Time.time;
        }
    }

    public void StopAndAccumulate()
    {
        if (IsRunning)
        {
            TotalElapsedTime += CurrentPhaseDuration;
            IsRunning = false;
            CurrentPhaseDuration = 0f;
        }
    }

    public void ResetPhaseTimer()
    {
        IsRunning = false;
        CurrentPhaseDuration = 0f;
        OnTimerUpdated?.Invoke(0f, TotalElapsedTime);
    }

    public void ResetAll()
    {
        IsRunning = false;
        CurrentPhaseDuration = 0f;
        TotalElapsedTime = 0f;
        OnTimerUpdated?.Invoke(0f, 0f);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/-Scripts/Core/TimerSystem.cs
git commit -m "feat: add TimerSystem for phase and total time tracking"
```

---

## Task 8: FeedbackController

**Files:**
- Create: `Assets/-Scripts/Feedback/FeedbackController.cs`

Orchestrates all juice effects by subscribing to GameStateManager events.

- [ ] **Step 1: Create FeedbackController.cs**

```csharp
// Assets/-Scripts/Feedback/FeedbackController.cs
using UnityEngine;

public class FeedbackController : MonoBehaviour
{
    [SerializeField] private CameraShakeAndZoom cameraShake;
    [SerializeField] private KeyboardShake keyboardShake;
    [SerializeField] private AudioManager audioKeys;
    [SerializeField] private AudioManager audioResult;

    void OnEnable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed += HandleStepProcessed;
            GameStateManager.Instance.OnPhaseCompleted += HandlePhaseCompleted;
            GameStateManager.Instance.OnPhaseRestarted += HandleRestart;
            GameStateManager.Instance.OnGameReset += HandleRestart;
        }
    }

    void OnDisable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed -= HandleStepProcessed;
            GameStateManager.Instance.OnPhaseCompleted -= HandlePhaseCompleted;
            GameStateManager.Instance.OnPhaseRestarted -= HandleRestart;
            GameStateManager.Instance.OnGameReset -= HandleRestart;
        }
    }

    // Called after GameStateManager.Awake via script execution order or lazy subscribe
    void Start()
    {
        // Re-subscribe in case OnEnable ran before GameStateManager.Awake
        OnDisable();
        OnEnable();
    }

    private void HandleStepProcessed(StepResult result, Step step)
    {
        switch (result)
        {
            case StepResult.Correct:
                if (step.Action == StepAction.Hold)
                    OnCorrectHold();
                else
                    OnCorrectRelease();
                break;
            case StepResult.PhaseComplete:
                if (step.Action == StepAction.Hold)
                    OnCorrectHold();
                else
                    OnCorrectRelease();
                break;
            case StepResult.Failed:
                OnFailed();
                break;
        }
    }

    private void HandlePhaseCompleted()
    {
        audioKeys.ResetPitch();
        audioResult.PlaySound(audioResult.complete);
    }

    private void OnCorrectHold()
    {
        audioKeys.StopAudio();
        audioKeys.AddPitch(0.2f);
        audioKeys.PlaySound(audioKeys.pressed);

        cameraShake.MildShake();
        cameraShake.OverZoomCam();

        keyboardShake.SetShaking(true);
        keyboardShake.UpMagnitude();
    }

    private void OnCorrectRelease()
    {
        audioKeys.PlaySound(audioKeys.released);

        cameraShake.MildShake();
        keyboardShake.DownMagnitude();
    }

    private void OnFailed()
    {
        audioKeys.StopAudio();
        audioKeys.ResetPitch();
        audioResult.StopAudio();
        audioResult.PlaySound(audioResult.fail);

        cameraShake.StrongShake();
    }

    private void HandleRestart()
    {
        audioKeys.SetVolume(1.0f);
        audioKeys.ResetPitch();
        keyboardShake.SetShaking(false);
        keyboardShake.ResetMagnitude();
        cameraShake.ResetFOV();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/-Scripts/Feedback/FeedbackController.cs
git commit -m "feat: add FeedbackController to orchestrate shake, zoom, and audio effects"
```

---

## Task 9: KeyboardVisualController

**Files:**
- Create: `Assets/-Scripts/UI/KeyboardVisualController.cs`

Replaces the 30+ individual GameObject fields with a serialized list.

- [ ] **Step 1: Create KeyboardVisualController.cs**

```csharp
// Assets/-Scripts/UI/KeyboardVisualController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class KeyboardVisualController : MonoBehaviour
{
    [Serializable]
    public struct KeyMapping
    {
        public KeyCode keyCode;
        public Image keyImage;
    }

    [SerializeField] private List<KeyMapping> keyMappings = new List<KeyMapping>();
    [SerializeField] private Color defaultColor = Color.white;
    [SerializeField] private Color holdColor = Color.yellow;

    private Dictionary<KeyCode, Image> keyLookup = new Dictionary<KeyCode, Image>();

    void Awake()
    {
        foreach (var mapping in keyMappings)
        {
            if (mapping.keyImage != null)
                keyLookup[mapping.keyCode] = mapping.keyImage;
        }
    }

    void OnEnable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed += HandleStepProcessed;
            GameStateManager.Instance.OnPhaseRestarted += ResetAllKeys;
            GameStateManager.Instance.OnGameReset += ResetAllKeys;
        }
    }

    void OnDisable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed -= HandleStepProcessed;
            GameStateManager.Instance.OnPhaseRestarted -= ResetAllKeys;
            GameStateManager.Instance.OnGameReset -= ResetAllKeys;
        }
    }

    void Start()
    {
        // Re-subscribe after all Awakes
        OnDisable();
        OnEnable();
    }

    private void HandleStepProcessed(StepResult result, Step step)
    {
        if (result == StepResult.Failed) return;

        if (step.Action == StepAction.Hold)
            SetKeyColor(step.Key, holdColor);
        else
            SetKeyColor(step.Key, defaultColor);
    }

    public void SetKeyColor(KeyCode key, Color color)
    {
        if (keyLookup.TryGetValue(key, out Image img))
            img.color = color;
    }

    public void ResetAllKeys()
    {
        foreach (var kvp in keyLookup)
            kvp.Value.color = defaultColor;
    }

    public void FlashKey(KeyCode key, Color color, float duration = 0.15f)
    {
        StartCoroutine(FlashKeyCoroutine(key, color, duration));
    }

    private IEnumerator FlashKeyCoroutine(KeyCode key, Color color, float duration)
    {
        SetKeyColor(key, color);
        yield return new WaitForSeconds(duration);
        SetKeyColor(key, defaultColor);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/-Scripts/UI/KeyboardVisualController.cs
git commit -m "feat: add KeyboardVisualController with serialized key mapping list"
```

---

## Task 10: UIController

**Files:**
- Create: `Assets/-Scripts/UI/UIController.cs`

Handles all text display, cursor blink, status messages, phase list scroll view, and delete animation.

- [ ] **Step 1: Create UIController.cs**

```csharp
// Assets/-Scripts/UI/UIController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    [Header("Text Displays")]
    [SerializeField] private TextMeshProUGUI targetTextUI;
    [SerializeField] private TextMeshProUGUI matchedTextUI;
    [SerializeField] private TextMeshProUGUI statusTextUI;

    [Header("Cursor Blink")]
    [SerializeField] private float cursorBlinkInterval = 0.5f;
    private float blinkTimer;
    private bool showCursor = true;

    [Header("Phase List UI")]
    [SerializeField] private TMP_InputField phaseInputField;
    [SerializeField] private Color inputFieldActiveColor = Color.white;
    [SerializeField] private Color inputFieldInactiveColor = Color.gray;
    [SerializeField] private Transform phaseListContent;
    [SerializeField] private GameObject phaseButtonPrefab;
    [SerializeField] private Color phaseSelectedColor = Color.yellow;
    [SerializeField] private Color phaseUnselectedColor = Color.white;

    [Header("Timer UI")]
    [SerializeField] private TextMeshProUGUI phaseTimeUI;
    [SerializeField] private TextMeshProUGUI totalTimeUI;

    [Header("Delete Animation")]
    [SerializeField] private float deleteDelayBetweenLetters = 0.05f;
    [SerializeField] private AudioManager audioKeys;

    [Header("Action Prompt")]
    [SerializeField] private bool showActionPromptOnFirstPhase = true;
    private bool hasCompletedFirstPhase;

    private WordEngine wordEngine;
    private int selectedPhaseIndex = -1;
    private Coroutine deleteAnimCoroutine;

    void OnEnable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed += HandleStepProcessed;
            GameStateManager.Instance.OnPhaseCompleted += HandlePhaseCompleted;
            GameStateManager.Instance.OnPhaseFailed += HandlePhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted += HandleRestart;
            GameStateManager.Instance.OnGameReset += HandleGameReset;
            GameStateManager.Instance.OnAllPhasesCompleted += HandleAllComplete;
        }
        if (PhaseManager.Instance != null)
        {
            PhaseManager.Instance.OnWordListChanged += RefreshPhaseList;
        }
        if (TimerSystem.Instance != null)
        {
            TimerSystem.Instance.OnTimerUpdated += UpdateTimerDisplay;
        }
    }

    void OnDisable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStepProcessed -= HandleStepProcessed;
            GameStateManager.Instance.OnPhaseCompleted -= HandlePhaseCompleted;
            GameStateManager.Instance.OnPhaseFailed -= HandlePhaseFailed;
            GameStateManager.Instance.OnPhaseRestarted -= HandleRestart;
            GameStateManager.Instance.OnGameReset -= HandleGameReset;
            GameStateManager.Instance.OnAllPhasesCompleted -= HandleAllComplete;
        }
        if (PhaseManager.Instance != null)
        {
            PhaseManager.Instance.OnWordListChanged -= RefreshPhaseList;
        }
        if (TimerSystem.Instance != null)
        {
            TimerSystem.Instance.OnTimerUpdated -= UpdateTimerDisplay;
        }
    }

    void Start()
    {
        OnDisable();
        OnEnable();
    }

    public void Initialize(WordEngine engine)
    {
        wordEngine = engine;
        UpdateTextDisplay();
        RefreshPhaseList();
    }

    void Update()
    {
        // Input field visual feedback
        if (phaseInputField != null)
        {
            bool focused = EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == phaseInputField.gameObject;
            phaseInputField.GetComponent<Image>().color = focused
                ? inputFieldActiveColor
                : inputFieldInactiveColor;
        }

        // Cursor blink
        blinkTimer += Time.deltaTime;
        if (blinkTimer >= cursorBlinkInterval)
        {
            blinkTimer = 0f;
            showCursor = !showCursor;
            UpdateTextDisplay();
        }
    }

    public void UpdateTextDisplay()
    {
        if (wordEngine == null) return;

        targetTextUI.text = wordEngine.TargetText;
        matchedTextUI.text = wordEngine.GetDisplayText(showCursor);

        // Action prompt for first phase
        if (showActionPromptOnFirstPhase && !hasCompletedFirstPhase
            && GameStateManager.Instance.CurrentState == GameState.Playing)
        {
            string prompt = wordEngine.GetActionPrompt();
            if (!string.IsNullOrEmpty(prompt))
                statusTextUI.text = prompt;
        }
    }

    private void HandleStepProcessed(StepResult result, Step step)
    {
        UpdateTextDisplay();

        if (result == StepResult.Failed)
        {
            statusTextUI.text = wordEngine.LastFailureMessage
                + ". Press Backspace to start again";
        }
    }

    private void HandlePhaseCompleted()
    {
        hasCompletedFirstPhase = true;
        statusTextUI.text = "Phase complete! Hit Return to continue...";
    }

    private void HandlePhaseFailed()
    {
        statusTextUI.text = wordEngine.LastFailureMessage
            + ". Press Backspace to start again";
    }

    private void HandleRestart()
    {
        if (deleteAnimCoroutine != null)
            StopCoroutine(deleteAnimCoroutine);
        deleteAnimCoroutine = StartCoroutine(DeleteTextAnim());
    }

    private void HandleGameReset()
    {
        hasCompletedFirstPhase = false;
        HandleRestart();
    }

    private void HandleAllComplete()
    {
        targetTextUI.text = "";
        matchedTextUI.text = "";
        statusTextUI.text = "Congratulations! You completed all phases!";
    }

    // --- Timer Display ---

    private void UpdateTimerDisplay(float phaseDuration, float total)
    {
        TimeSpan phaseTime = TimeSpan.FromSeconds(phaseDuration);
        phaseTimeUI.text = FormatTime(phaseTime);

        TimeSpan totalTime = TimeSpan.FromSeconds(total);
        totalTimeUI.text = FormatTime(totalTime);
    }

    private string FormatTime(TimeSpan t)
    {
        return $"{t.Hours:D2}\"{t.Minutes:D2}\'{t.Seconds:D2}.{t.Milliseconds / 10:D2}";
    }

    // --- Phase List UI ---

    public void RefreshPhaseList()
    {
        if (phaseListContent == null || phaseButtonPrefab == null) return;

        foreach (Transform child in phaseListContent)
            Destroy(child.gameObject);

        var words = PhaseManager.Instance.Words;
        for (int i = 0; i < words.Count; i++)
        {
            int index = i;
            GameObject btnObj = Instantiate(phaseButtonPrefab, phaseListContent);
            btnObj.GetComponentInChildren<TextMeshProUGUI>().text = $"{index + 1}. {words[i]}";

            Button btn = btnObj.GetComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                selectedPhaseIndex = index;
                HighlightSelectedButton(btnObj);
            });
        }

        Canvas.ForceUpdateCanvases();
    }

    private void HighlightSelectedButton(GameObject selected)
    {
        foreach (Transform child in phaseListContent)
        {
            Image img = child.GetComponent<Image>();
            if (img != null)
                img.color = (child.gameObject == selected) ? phaseSelectedColor : phaseUnselectedColor;
        }
    }

    // Button callbacks (wire in Inspector)
    public void OnAddPhaseClicked()
    {
        string text = phaseInputField.text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        PhaseManager.Instance.AddPhase(text, 0);
        phaseInputField.text = "";
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void OnDeletePhaseClicked()
    {
        if (selectedPhaseIndex < 0) return;
        PhaseManager.Instance.RemovePhase(selectedPhaseIndex);
        selectedPhaseIndex = -1;
    }

    public void OnSwapPhaseClicked()
    {
        if (selectedPhaseIndex < 0) return;
        PhaseManager.Instance.JumpToPhase(selectedPhaseIndex);
        GameStateManager.Instance.RaisePhaseRestarted();
        GameStateManager.Instance.TransitionTo(GameState.Playing);
    }

    public void OnMovePhaseUpClicked()
    {
        if (selectedPhaseIndex <= 0) return;
        PhaseManager.Instance.MovePhase(selectedPhaseIndex, selectedPhaseIndex - 1);
        selectedPhaseIndex--;
    }

    public void OnMovePhaseDownClicked()
    {
        if (selectedPhaseIndex < 0 || selectedPhaseIndex >= PhaseManager.Instance.TotalPhases - 1) return;
        PhaseManager.Instance.MovePhase(selectedPhaseIndex, selectedPhaseIndex + 1);
        selectedPhaseIndex++;
    }

    // --- Delete Text Animation ---

    private IEnumerator DeleteTextAnim()
    {
        string currentDisplayText = matchedTextUI.text;
        string visibleText = Regex.Replace(currentDisplayText, "<.*?>", string.Empty);

        if (string.IsNullOrEmpty(visibleText))
        {
            UpdateTextDisplay();
            yield break;
        }

        audioKeys.PlaySound(audioKeys.released);

        char[] textChars = visibleText.ToCharArray();

        for (int i = textChars.Length - 1; i >= 0; i--)
        {
            if (textChars[i] == '_' || textChars[i] == ' ')
                continue;

            textChars[i] = '_';

            string newText = RebuildWithOriginalFormatting(currentDisplayText, new string(textChars));
            matchedTextUI.text = newText;
            matchedTextUI.ForceMeshUpdate();

            if (i > 0)
                audioKeys.PlaySound(audioKeys.released);

            yield return new WaitForSeconds(deleteDelayBetweenLetters);
        }

        UpdateTextDisplay();
    }

    private string RebuildWithOriginalFormatting(string originalText, string newContent)
    {
        var tags = new List<(int pos, string tag)>();
        var matches = Regex.Matches(originalText, "<.*?>");
        foreach (Match match in matches)
            tags.Add((match.Index, match.Value));

        StringBuilder result = new StringBuilder();
        int contentIndex = 0;

        for (int i = 0; i < originalText.Length; i++)
        {
            var currentTag = tags.Find(t => t.pos == i);
            if (currentTag.tag != null)
            {
                result.Append(currentTag.tag);
                i += currentTag.tag.Length - 1;
            }
            else if (contentIndex < newContent.Length)
            {
                char c = newContent[contentIndex++];
                result.Append(c == '_' && originalText[i] == ' ' ? ' ' : c);
            }
        }

        return result.ToString();
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/-Scripts/UI/UIController.cs
git commit -m "feat: add UIController for text display, phase list, timer, and delete animation"
```

---

## Task 11: SettingsManager + Audio Mixer

**Files:**
- Create: `Assets/-Scripts/Core/SettingsManager.cs`
- Modify: `Assets/-Scripts/Audio/AudioManager.cs` — add AudioMixerGroup support

- [ ] **Step 1: Create SettingsManager.cs**

```csharp
// Assets/-Scripts/Core/SettingsManager.cs
using System;
using UnityEngine;
using UnityEngine.Audio;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [SerializeField] private AudioMixer mainMixer;

    // Keys for PlayerPrefs
    private const string KEY_MASTER_VOL = "settings_masterVolume";
    private const string KEY_SFX_VOL = "settings_sfxVolume";
    private const string KEY_FULLSCREEN = "settings_fullscreen";
    private const string KEY_RESOLUTION = "settings_resolution";
    private const string KEY_QUALITY = "settings_quality";
    private const string KEY_ACTION_PROMPTS = "settings_actionPrompts";

    public float MasterVolume
    {
        get => PlayerPrefs.GetFloat(KEY_MASTER_VOL, 1f);
        set { PlayerPrefs.SetFloat(KEY_MASTER_VOL, value); ApplyAudio(); }
    }

    public float SFXVolume
    {
        get => PlayerPrefs.GetFloat(KEY_SFX_VOL, 1f);
        set { PlayerPrefs.SetFloat(KEY_SFX_VOL, value); ApplyAudio(); }
    }

    public bool Fullscreen
    {
        get => PlayerPrefs.GetInt(KEY_FULLSCREEN, 1) == 1;
        set { PlayerPrefs.SetInt(KEY_FULLSCREEN, value ? 1 : 0); ApplyDisplay(); }
    }

    public int ResolutionIndex
    {
        get => PlayerPrefs.GetInt(KEY_RESOLUTION, -1);
        set { PlayerPrefs.SetInt(KEY_RESOLUTION, value); ApplyDisplay(); }
    }

    public int QualityLevel
    {
        get => PlayerPrefs.GetInt(KEY_QUALITY, QualitySettings.GetQualityLevel());
        set { PlayerPrefs.SetInt(KEY_QUALITY, value); QualitySettings.SetQualityLevel(value); }
    }

    public bool ShowActionPrompts
    {
        get => PlayerPrefs.GetInt(KEY_ACTION_PROMPTS, 1) == 1;
        set => PlayerPrefs.SetInt(KEY_ACTION_PROMPTS, value ? 1 : 0);
    }

    public event Action OnSettingsChanged;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        Load();
    }

    public void Load()
    {
        ApplyAudio();
        ApplyDisplay();
        QualitySettings.SetQualityLevel(QualityLevel);
    }

    public void Save()
    {
        PlayerPrefs.Save();
        OnSettingsChanged?.Invoke();
    }

    public void ResetDefaults()
    {
        PlayerPrefs.DeleteKey(KEY_MASTER_VOL);
        PlayerPrefs.DeleteKey(KEY_SFX_VOL);
        PlayerPrefs.DeleteKey(KEY_FULLSCREEN);
        PlayerPrefs.DeleteKey(KEY_RESOLUTION);
        PlayerPrefs.DeleteKey(KEY_QUALITY);
        PlayerPrefs.DeleteKey(KEY_ACTION_PROMPTS);
        Load();
        OnSettingsChanged?.Invoke();
    }

    private void ApplyAudio()
    {
        if (mainMixer == null) return;

        // Convert linear 0-1 to dB (-80 to 0)
        float masterDb = MasterVolume > 0.001f ? Mathf.Log10(MasterVolume) * 20f : -80f;
        float sfxDb = SFXVolume > 0.001f ? Mathf.Log10(SFXVolume) * 20f : -80f;

        mainMixer.SetFloat("MasterVolume", masterDb);
        mainMixer.SetFloat("SFXVolume", sfxDb);
    }

    private void ApplyDisplay()
    {
        Screen.fullScreen = Fullscreen;

        int resIdx = ResolutionIndex;
        if (resIdx >= 0 && resIdx < Screen.resolutions.Length)
        {
            Resolution res = Screen.resolutions[resIdx];
            Screen.SetResolution(res.width, res.height, Fullscreen);
        }
    }
}
```

- [ ] **Step 2: Add AudioMixerGroup support to AudioManager**

Modify `Assets/-Scripts/Audio/AudioManager.cs`. Add a serialized `AudioMixerGroup` field and assign it to the AudioSource on Start:

Add after the existing `private AudioSource audioSource;` line:

```csharp
    [Header("Mixer")]
    [SerializeField] private UnityEngine.Audio.AudioMixerGroup mixerGroup;
```

And in the `Start()` method, after `audioSource = GetComponent<AudioSource>();`, add:

```csharp
        if (mixerGroup != null)
            audioSource.outputAudioMixerGroup = mixerGroup;
```

Also remove the empty `Update()` method.

- [ ] **Step 3: Create Audio Mixer asset in Unity Editor**

Use the Unity Editor (or Coplay MCP) to create `Assets/Audio/MainMixer.mixer` with:
- Master group (exposed parameter: "MasterVolume")
- SFX child group (exposed parameter: "SFXVolume")

This must be done in the Unity Editor since .mixer is a binary asset.

- [ ] **Step 4: Commit**

```bash
git add Assets/-Scripts/Core/SettingsManager.cs Assets/-Scripts/Audio/AudioManager.cs
git commit -m "feat: add SettingsManager with PlayerPrefs persistence and AudioMixer support"
```

---

## Task 12: Leaderboard Interfaces

**Files:**
- Create: `Assets/-Scripts/Leaderboard/ILeaderboardService.cs`

- [ ] **Step 1: Create ILeaderboardService.cs**

```csharp
// Assets/-Scripts/Leaderboard/ILeaderboardService.cs
using System;
using System.Collections.Generic;

public struct LeaderboardEntry
{
    public string PlayerName;
    public float TotalTime;
    public int PhaseCount;
    public string WordListName;
    public DateTime SubmittedAt;
}

public interface ILeaderboardService
{
    void SubmitScore(string wordListName, float totalTime, int phaseCount);
    void GetLeaderboard(string wordListName, Action<List<LeaderboardEntry>> callback);
}

/// <summary>
/// Default no-op implementation. Replace with Steam or web API integration later.
/// </summary>
public class NullLeaderboardService : ILeaderboardService
{
    public void SubmitScore(string wordListName, float totalTime, int phaseCount)
    {
        // No-op: leaderboard integration not yet implemented
        UnityEngine.Debug.Log($"[Leaderboard] Score submitted (no backend): {wordListName} - {totalTime:F2}s, {phaseCount} phases");
    }

    public void GetLeaderboard(string wordListName, Action<List<LeaderboardEntry>> callback)
    {
        // Return empty list
        callback?.Invoke(new List<LeaderboardEntry>());
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/-Scripts/Leaderboard/ILeaderboardService.cs
git commit -m "feat: add ILeaderboardService interface and NullLeaderboardService stub"
```

---

## Task 13: GameCoordinator (Wire Everything Together)

**Files:**
- Create: `Assets/-Scripts/Core/GameCoordinator.cs`
- Delete: `Assets/-Scripts/GameManager.cs` (after wiring is verified)

GameCoordinator replaces the old GameManager. It holds references to all systems and wires them together. It's the only script that knows about all other systems — individual systems only communicate through events.

- [ ] **Step 1: Create GameCoordinator.cs**

```csharp
// Assets/-Scripts/Core/GameCoordinator.cs
using UnityEngine;

public class GameCoordinator : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private KeyboardVisualController keyboardVisual;

    [Header("Settings")]
    [SerializeField] private Texture2D customCursor;
    [SerializeField] private Vector2 cursorHotspot = Vector2.zero;

    private WordEngine wordEngine;
    private ILeaderboardService leaderboardService;
    private UIController uiController;

    void Start()
    {
        Cursor.SetCursor(customCursor, cursorHotspot, CursorMode.Auto);

        wordEngine = new WordEngine();
        leaderboardService = new NullLeaderboardService();

        uiController = GetComponentInChildren<UIController>();
        if (uiController == null)
            uiController = FindAnyObjectByType<UIController>();

        // Subscribe to input events
        InputHandler.Instance.OnKeyAction += HandleKeyAction;
        InputHandler.Instance.OnBackspacePressed += HandleBackspace;
        InputHandler.Instance.OnEnterPressed += HandleEnter;

        // Subscribe to phase changes
        PhaseManager.Instance.OnPhaseWordChanged += HandlePhaseWordChanged;

        // Start the first phase
        GameStateManager.Instance.TransitionTo(GameState.Playing);
        wordEngine.LoadWord(PhaseManager.Instance.CurrentWord);
        uiController.Initialize(wordEngine);
    }

    void OnDestroy()
    {
        if (InputHandler.Instance != null)
        {
            InputHandler.Instance.OnKeyAction -= HandleKeyAction;
            InputHandler.Instance.OnBackspacePressed -= HandleBackspace;
            InputHandler.Instance.OnEnterPressed -= HandleEnter;
        }
        if (PhaseManager.Instance != null)
        {
            PhaseManager.Instance.OnPhaseWordChanged -= HandlePhaseWordChanged;
        }
    }

    private void HandleKeyAction(KeyCode key, bool isPressed)
    {
        var state = GameStateManager.Instance.CurrentState;

        if (state != GameState.Playing)
            return;

        // Start timer on first key action
        if (!TimerSystem.Instance.IsRunning)
            TimerSystem.Instance.StartTimer();

        StepResult result = wordEngine.ProcessInput(key, isPressed);

        // Get the step that was just processed for feedback
        int stepIndex = result == StepResult.Failed
            ? wordEngine.CurrentStep
            : wordEngine.CurrentStep - 1;

        Step step = wordEngine.Steps[stepIndex];
        GameStateManager.Instance.RaiseStepProcessed(result, step);

        switch (result)
        {
            case StepResult.Correct:
                // Stay in Playing state
                break;
            case StepResult.PhaseComplete:
                TimerSystem.Instance.StopAndAccumulate();
                GameStateManager.Instance.TransitionTo(GameState.PhaseComplete);
                break;
            case StepResult.Failed:
                GameStateManager.Instance.TransitionTo(GameState.PhaseFailed);
                break;
        }
    }

    private void HandleBackspace()
    {
        var state = GameStateManager.Instance.CurrentState;
        if (state == GameState.Playing || state == GameState.PhaseFailed)
        {
            wordEngine.Reset();
            wordEngine.LoadWord(PhaseManager.Instance.CurrentWord);
            TimerSystem.Instance.ResetPhaseTimer();

            GameStateManager.Instance.RaisePhaseRestarted();
            GameStateManager.Instance.TransitionTo(GameState.Playing);

            // Flash backspace key
            keyboardVisual.FlashKey(KeyCode.Backspace, keyboardVisual.GetComponentInChildren<KeyboardVisualController>() != null
                ? Color.yellow : Color.yellow);

            uiController.UpdateTextDisplay();
        }
    }

    private void HandleEnter()
    {
        if (GameStateManager.Instance.CurrentState != GameState.PhaseComplete)
            return;

        if (PhaseManager.Instance.AdvancePhase())
        {
            wordEngine.LoadWord(PhaseManager.Instance.CurrentWord);
            GameStateManager.Instance.TransitionTo(GameState.Playing);
            keyboardVisual.FlashKey(KeyCode.Return, Color.yellow);
            uiController.UpdateTextDisplay();
        }
        else
        {
            // All phases done — submit score
            leaderboardService.SubmitScore(
                PhaseManager.Instance.ActiveProvider?.DisplayName ?? "Unknown",
                TimerSystem.Instance.TotalElapsedTime,
                PhaseManager.Instance.TotalPhases
            );
            GameStateManager.Instance.TransitionTo(GameState.AllComplete);
        }
    }

    private void HandlePhaseWordChanged(string word)
    {
        wordEngine.LoadWord(word);
        uiController.UpdateTextDisplay();
    }

    // Called from UI button
    public void ResetGame()
    {
        PhaseManager.Instance.ResetToBeginning();
        wordEngine.LoadWord(PhaseManager.Instance.CurrentWord);
        TimerSystem.Instance.ResetAll();
        GameStateManager.Instance.RaiseGameReset();
        GameStateManager.Instance.TransitionTo(GameState.Playing);
        uiController.UpdateTextDisplay();
    }

    public void CloseGame()
    {
        Application.Quit();
    }
}
```

- [ ] **Step 2: Fix the FlashKey call in HandleBackspace**

The `HandleBackspace` method has a redundant null check. Simplify to:

Replace the FlashKey line in `HandleBackspace`:
```csharp
            keyboardVisual.FlashKey(KeyCode.Backspace, Color.yellow);
```

And in `HandleEnter`:
```csharp
            keyboardVisual.FlashKey(KeyCode.Return, Color.yellow);
```

(The `Color.yellow` here should match the holdColor — but since FlashKey resets to defaultColor, using a literal is fine. The actual colors come from the KeyboardVisualController's serialized fields.)

- [ ] **Step 3: Commit**

```bash
git add Assets/-Scripts/Core/GameCoordinator.cs
git commit -m "feat: add GameCoordinator to wire all systems together

Replaces GameManager as the central coordinator. Connects
InputHandler, WordEngine, PhaseManager, TimerSystem,
FeedbackController, UIController, and KeyboardVisualController
through C# events."
```

---

## Task 14: Scene Wiring and Old GameManager Removal

**Files:**
- Delete: `Assets/-Scripts/GameManager.cs`
- Modify: Scene (via Unity Editor / Coplay MCP)

This task wires up all the new MonoBehaviour components in the scene, migrates serialized references from the old GameManager, and removes it.

- [ ] **Step 1: Add new components to the scene**

Using the Unity Editor or Coplay MCP tools:

1. Create a new empty GameObject called "GameSystems" in the scene hierarchy
2. Add components to it:
   - `GameStateManager`
   - `InputHandler`
   - `PhaseManager`
   - `TimerSystem`
   - `SettingsManager`
   - `GameCoordinator`
3. Add `FeedbackController` to the Camera object (or a child of GameSystems)
4. Add `UIController` to the Canvas or a UI parent object
5. Add `KeyboardVisualController` to the keyboard visual parent object

- [ ] **Step 2: Wire serialized references**

For each new component, assign the references in the Inspector:

**GameCoordinator:**
- `keyboardVisual` → KeyboardVisualController component
- `customCursor` → same cursor texture from old GameManager

**FeedbackController:**
- `cameraShake` → CameraShakeAndZoom component on camera
- `keyboardShake` → KeyboardShake component
- `audioKeys` → AudioManager component (keys)
- `audioResult` → AudioManager component (results)

**UIController:**
- `targetTextUI`, `matchedTextUI`, `statusTextUI` → same TMP objects from old GameManager
- `phaseInputField` → same input field
- `phaseListContent` → same scroll content transform
- `phaseButtonPrefab` → same prefab
- `phaseTimeUI`, `totalTimeUI` → same timer TMP objects
- `audioKeys` → AudioManager component (for delete animation sounds)
- Colors → copy from old GameManager

**KeyboardVisualController:**
- `keyMappings` → create entries for each key (A-Z, 0-9, Backspace, Enter) mapping KeyCode to the Image component on each key GameObject
- `defaultColor` → old GameManager's `keyReleaseColor`
- `holdColor` → old GameManager's `keyHoldColor`

**PhaseManager:**
- `defaultWordList` → create a FixedWordListProvider ScriptableObject asset (Right-click > Create > Words > Fixed Word List) with the demo word "No Food"

- [ ] **Step 3: Remove old GameManager from scene**

Remove the GameManager component from whatever GameObject it was on. Do NOT delete the GameObject if it has other components.

- [ ] **Step 4: Delete old GameManager.cs**

```bash
git rm "Assets/-Scripts/GameManager.cs"
git rm "Assets/-Scripts/GameManager.cs.meta"
```

- [ ] **Step 5: Delete old InputSystem_Actions.inputactions**

```bash
git rm "Assets/InputSystem_Actions.inputactions"
git rm "Assets/InputSystem_Actions.inputactions.meta"
```

- [ ] **Step 6: Verify compilation and play test**

1. Open Unity Editor
2. Check for compile errors (Console window)
3. Enter Play Mode
4. Test: type the first letter of "No Food" (hold N) — should see cursor advance, hear sound, see shake
5. Test: wrong key — should see failure message, strong shake
6. Test: backspace — should restart phase with delete animation
7. Test: complete a phase — should see "Phase complete! Hit Return to continue..."
8. Test: add/remove phases from scroll view

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: complete migration from GameManager to modular system architecture

Remove monolithic GameManager (850 lines) in favor of:
- GameStateManager (state machine)
- GameCoordinator (wiring)
- WordEngine (pure C# game logic)
- InputHandler (New Input System)
- PhaseManager + WordListProviders (word list management)
- TimerSystem (timing)
- FeedbackController (juice effects)
- UIController (display)
- KeyboardVisualController (key visuals)
- SettingsManager (PlayerPrefs persistence)
- ILeaderboardService (future integration stub)"
```

---

## Task 15: Update Project Documentation

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update CLAUDE.md with new architecture**

Update the project structure section to reflect the new folder layout and system descriptions:

```markdown
## Project Structure
- `Assets/-Scripts/Core/` — GameStateManager, GameCoordinator, WordEngine, InputHandler, PhaseManager, TimerSystem, SettingsManager
- `Assets/-Scripts/Feedback/` — FeedbackController, CameraShakeAndZoom, KeyboardShake
- `Assets/-Scripts/UI/` — UIController, KeyboardVisualController, CurTextTMPanim, CircularScrollingText, MenuAnimOnOff
- `Assets/-Scripts/Audio/` — AudioManager
- `Assets/-Scripts/WordList/` — IWordListProvider, FixedWordListProvider, FileWordListProvider, DailyWordListProvider
- `Assets/-Scripts/Leaderboard/` — ILeaderboardService, NullLeaderboardService
- `Assets/-Scripts/Utility/` — Screenshot
- `Assets/Tests/EditMode/` — WordEngine unit tests
- `Assets/-Anim/`, `-Audio/`, `-Images/`, `-Material/`, `-Prefabs/` — Game assets
- `Assets/Scenes/` — Unity scenes
- `Assets/CRT-Free/` — CRT post-processing effect
- `Assets/StandaloneFileBrowser/` — Native file dialog plugin

## Architecture
- Event-driven MonoBehaviour systems connected by C# events
- GameStateManager owns the state machine (Idle, Playing, PhaseFailed, PhaseComplete, AllComplete)
- GameCoordinator wires systems together — the only class that knows about all systems
- WordEngine is a pure C# class (no MonoBehaviour) for testability
- IWordListProvider interface enables fixed lists (demo), file-based lists (player custom), and daily lists (future API)
- ILeaderboardService interface ready for Steam/web leaderboard integration
- SettingsManager persists player preferences via PlayerPrefs
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with new modular architecture"
```

---

## Post-Implementation Verification Checklist

After all tasks are complete, verify:

- [ ] All 12+ unit tests pass in Unity Test Runner (EditMode)
- [ ] Game plays identically to before the refactor:
  - Hold/release mechanic works correctly
  - Cursor blinks between letters
  - Camera shake and keyboard shake trigger on correct/wrong input
  - Audio pitch escalates on consecutive correct holds
  - Delete animation plays on restart
  - Phase progression works (Enter advances, Backspace restarts)
  - Timer tracks phase and total time
  - Phase list UI (add, delete, swap, move up/down) works
  - Screenshot function works
  - Custom cursor displays
- [ ] No compile warnings (other than Unity package warnings)
- [ ] Old GameManager.cs is fully deleted
- [ ] All new scripts are in their correct subfolder
