# Production Refactor Design — Words Typing Game

## Overview

Refactor the prototype typing game into a production-quality architecture for Steam release. The game's core mechanic: players hold a key on first letter appearance, release on second, alternating for repeated letters, progressing through word phases with timer and juice effects.

**Goals:**
- Split 850-line GameManager into focused, testable systems
- Migrate from legacy Input to New Input System
- Add settings persistence (audio, display)
- Create clean integration points for future features (daily word lists, leaderboards, Steam SDK)
- Support player-created word lists with file I/O
- Keep single-scene architecture but structure for future separation

**Non-goals (deferred):**
- Daily word list server/API implementation
- Leaderboard backend or Steam SDK integration
- Multiplayer
- Scene splitting

---

## Architecture

### System Diagram

```
                    ┌─────────────────┐
                    │ GameStateManager │  (state machine - central coordinator)
                    └────────┬────────┘
                             │ events
        ┌────────────┬───────┼───────┬──────────────┐
        ▼            ▼       ▼       ▼              ▼
┌─────────────┐ ┌────────┐ ┌─────┐ ┌──────────┐ ┌────────────┐
│ InputHandler│ │WordEngine│ │Timer│ │PhaseManager│ │UIController│
└──────┬──────┘ └───┬─────┘ └──┬──┘ └─────┬────┘ └─────┬──────┘
       │            │          │           │            │
       │     ┌──────┘          │    ┌──────┘            │
       ▼     ▼                 ▼    ▼                   ▼
┌──────────────────┐  ┌──────────────────┐  ┌───────────────────┐
│FeedbackController│  │ WordListProvider │  │KeyboardVisualCtrl │
│(shake,zoom,audio)│  │   (interface)    │  │                   │
└──────────────────┘  └──────────────────┘  └───────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
      ┌──────────────┐ ┌───────────┐ ┌──────────────┐
      │FixedListProvider│ │FileListProvider│ │DailyListProvider│
      │  (demo)      │ │(player custom)│ │  (stub/future) │
      └──────────────┘ └───────────┘ └──────────────┘
```

### Game States

```
enum GameState {
    Idle,           // waiting to start / between sessions
    Playing,        // actively typing a phase
    PhaseFailed,    // wrong key — waiting for backspace
    PhaseComplete,  // phase done — waiting for Enter
    AllComplete     // all phases finished
}
```

Transitions:
- `Idle → Playing`: Phase starts, first key input begins timer
- `Playing → PhaseFailed`: Wrong key or wrong action (hold vs release)
- `Playing → PhaseComplete`: All steps matched
- `PhaseFailed → Playing`: Backspace pressed (restart current phase)
- `PhaseComplete → Playing`: Enter pressed (next phase)
- `PhaseComplete → AllComplete`: No more phases
- `AllComplete → Idle`: Game reset

---

## Systems Detail

### 1. GameStateManager (`Assets/-Scripts/Core/GameStateManager.cs`)

Central coordinator. Owns the state machine and raises C# events on transitions. Does NOT contain game logic — it reacts to events from other systems and transitions state.

```csharp
// Events
public event Action<GameState, GameState> OnStateChanged;  // old, new
public event Action OnPhaseStarted;
public event Action OnPhaseCompleted;
public event Action OnPhaseFailed;
public event Action OnAllPhasesCompleted;
public event Action OnGameReset;
```

Singleton via a simple `Instance` pattern (consistent with existing CameraShakeAndZoom approach). All systems reference GameStateManager to check state or subscribe to events.

### 2. InputHandler (`Assets/-Scripts/Core/InputHandler.cs`)

Wraps the New Input System. Creates an Input Action Asset with a single action map "Gameplay" containing:
- **HoldRelease action** for each letter key (A-Z), digit (0-9) — uses Press interaction with `Press and Release` behavior
- **Backspace** action — restart phase
- **Enter/Return** action — next phase
- **UI navigation** — for menu interaction (handled by Unity's built-in UI input module)

Raises events:
```csharp
public event Action<KeyCode, bool> OnKeyAction;  // key, isPressed (true=down, false=up)
public event Action OnBackspace;
public event Action OnEnter;
```

Disables gameplay input when UI input field is focused (replaces the current `EventSystem.current.currentSelectedGameObject` check).

### 3. WordEngine (`Assets/-Scripts/Core/WordEngine.cs`)

Pure logic, no MonoBehaviour dependency (plain C# class). Extracted from GameManager's `ParseSteps()` and `ProcessStep()`.

```csharp
public class WordEngine {
    public void LoadWord(string word);           // parses steps
    public StepResult ProcessInput(KeyCode key, bool isPressed);  // returns match/fail/complete
    public int CurrentStep { get; }
    public int TotalSteps { get; }
    public ReadOnlyCollection<Step> Steps { get; }
    public string GetDisplayText(bool showCursor);  // builds the matched/unmatched display
}

public enum StepResult { Correct, Failed, PhaseComplete }

public struct Step {
    public KeyCode Key;
    public StepAction Action;  // Hold or Release
    public string Letter;
    public int TargetTextIndex; // position in original text
}
```

This is the most testable extraction — can unit test word parsing and step validation without Unity.

### 4. PhaseManager (`Assets/-Scripts/Core/PhaseManager.cs`)

Manages the list of phases (words) and current phase progression.

```csharp
public class PhaseManager : MonoBehaviour {
    public IWordListProvider ActiveWordList { get; }
    public int CurrentPhaseIndex { get; }
    public string CurrentWord { get; }
    public int TotalPhases { get; }
    
    public void LoadWordList(IWordListProvider provider);
    public bool AdvancePhase();      // returns false if no more phases
    public void RestartPhase();
    public void ResetToBeginning();
    
    // Phase list editing (for UI)
    public void AddPhase(string word, int index = 0);
    public void RemovePhase(int index);
    public void MovePhase(int fromIndex, int toIndex);
}
```

### 5. WordListProvider Interface (`Assets/-Scripts/WordList/IWordListProvider.cs`)

```csharp
public interface IWordListProvider {
    string DisplayName { get; }
    List<string> GetWords();
    bool IsEditable { get; }  // false for daily/fixed lists
}
```

**Implementations:**
- `FixedWordListProvider` — hardcoded demo lists, ships with build
- `FileWordListProvider` — loads/saves from JSON files in `Application.persistentDataPath/WordLists/`
- `DailyWordListProvider` — stub that returns placeholder data, with a `TODO` comment and interface ready for HTTP fetch

File format for custom lists:
```json
{
    "name": "My List",
    "words": ["Hello", "World", "Keyboard"],
    "createdAt": "2026-03-31T00:00:00Z"
}
```

### 6. TimerSystem (`Assets/-Scripts/Core/TimerSystem.cs`)

Extracted from GameManager's timer logic. Listens to GameStateManager events.

```csharp
public class TimerSystem : MonoBehaviour {
    public float CurrentPhaseDuration { get; }
    public float TotalElapsedTime { get; }
    public bool IsRunning { get; }
    
    public event Action<float, float> OnTimerUpdated;  // phase, total
    
    public void StartTimer();
    public void StopAndAccumulate();  // adds phase time to total
    public void ResetAll();
}
```

Subscribes to `OnPhaseStarted`, `OnPhaseCompleted`, `OnGameReset` to auto-manage.

### 7. FeedbackController (`Assets/-Scripts/Feedback/FeedbackController.cs`)

Coordinates all juice effects. Subscribes to WordEngine results via GameStateManager events.

```csharp
public class FeedbackController : MonoBehaviour {
    [SerializeField] private CameraShakeAndZoom cameraShake;
    [SerializeField] private KeyboardShake keyboardShake;
    [SerializeField] private AudioManager audioKeys;
    [SerializeField] private AudioManager audioResult;
    
    public void OnCorrectHold();    // mild shake, zoom, pitch up, keyboard shake up
    public void OnCorrectRelease(); // mild shake, keyboard shake down
    public void OnFailed();         // strong shake, reset pitch, fail sound
    public void OnPhaseComplete();  // complete sound, reset
    public void OnRestart();        // reset all effects
}
```

CameraShakeAndZoom and KeyboardShake remain as separate components (they work fine) — FeedbackController just orchestrates them.

### 8. KeyboardVisualController (`Assets/-Scripts/UI/KeyboardVisualController.cs`)

Replaces the 30+ `[HideInInspector] public GameObject` fields with a serialized list/dictionary approach.

```csharp
[System.Serializable]
public struct KeyMapping {
    public KeyCode keyCode;
    public Image keyImage;
}

public class KeyboardVisualController : MonoBehaviour {
    [SerializeField] private List<KeyMapping> keyMappings;
    [SerializeField] private Color defaultColor;
    [SerializeField] private Color holdColor;
    [SerializeField] private Color releaseColor;
    
    public void SetKeyState(KeyCode key, KeyState state);
    public void ResetAllKeys();
    public void FlashKey(KeyCode key, float duration);  // for backspace/enter feedback
}
```

You still wire up keys in the Inspector, but it's a clean list instead of 30 separate fields. Easy to extend with new keys.

### 9. UIController (`Assets/-Scripts/UI/UIController.cs`)

Handles all text display, cursor blink, scroll view for phase list, and action prompts. Subscribes to events from GameStateManager, WordEngine (via state manager), and PhaseManager.

```csharp
public class UIController : MonoBehaviour {
    // Text displays
    [SerializeField] private TextMeshProUGUI targetTextUI;
    [SerializeField] private TextMeshProUGUI matchedTextUI;
    [SerializeField] private TextMeshProUGUI statusTextUI;  // renamed from notMatchedTextUI
    [SerializeField] private TextMeshProUGUI phaseTimeUI;
    [SerializeField] private TextMeshProUGUI totalTimeUI;
    
    // Phase list UI
    [SerializeField] private ScrollRect phaseScrollRect;
    [SerializeField] private TMP_InputField phaseInputField;
    [SerializeField] private Transform phaseListContent;
    [SerializeField] private GameObject phaseButtonPrefab;
}
```

The delete-text animation (`DeleteTextAnim` coroutine) stays in UIController since it's purely visual.

### 10. AudioManager (improved) (`Assets/-Scripts/Audio/AudioManager.cs`)

Add Unity Audio Mixer integration for settings control:

```csharp
public class AudioManager : MonoBehaviour {
    [SerializeField] private AudioMixerGroup mixerGroup;  // NEW: for volume control via settings
    // ... existing clip fields stay the same
    
    // Existing methods unchanged, just add mixer routing
}
```

No major changes — it works. Just add mixer group reference so SettingsManager can control master/SFX/music volumes.

### 11. SettingsManager (`Assets/-Scripts/Core/SettingsManager.cs`)

Persists player settings via `PlayerPrefs` (simple, works for Steam).

```csharp
public class SettingsManager : MonoBehaviour {
    // Audio
    public float MasterVolume { get; set; }
    public float SFXVolume { get; set; }
    
    // Display
    public int ResolutionIndex { get; set; }
    public bool Fullscreen { get; set; }
    public int QualityLevel { get; set; }
    
    // Gameplay
    public bool ShowActionPrompts { get; set; }
    
    public void Save();
    public void Load();
    public void ResetDefaults();
    
    public event Action OnSettingsChanged;
}
```

Settings UI panel is deferred — SettingsManager just provides the data layer. The existing `MenuAnimOnOff` can animate the settings panel when built.

### 12. LeaderboardService Interface (`Assets/-Scripts/Leaderboard/ILeaderboardService.cs`)

Touch point only — no implementation now.

```csharp
public interface ILeaderboardService {
    void SubmitScore(string wordList, float totalTime, int phaseCount);
    void GetLeaderboard(string wordList, Action<List<LeaderboardEntry>> callback);
}

public struct LeaderboardEntry {
    public string PlayerName;
    public float TotalTime;
    public int PhaseCount;
    public DateTime SubmittedAt;
}
```

A `NullLeaderboardService` implementation that does nothing ships by default.

---

## Folder Structure

```
Assets/-Scripts/
├── Core/
│   ├── GameStateManager.cs
│   ├── InputHandler.cs
│   ├── WordEngine.cs
│   ├── PhaseManager.cs
│   ├── TimerSystem.cs
│   └── SettingsManager.cs
├── Audio/
│   └── AudioManager.cs
├── Feedback/
│   ├── FeedbackController.cs
│   ├── CameraShakeAndZoom.cs    (moved, minor cleanup)
│   └── KeyboardShake.cs         (moved, minor cleanup)
├── UI/
│   ├── UIController.cs
│   ├── KeyboardVisualController.cs
│   ├── CurTextTMPanim.cs        (moved, unchanged)
│   ├── CircularScrollingText.cs  (moved, unchanged)
│   └── MenuAnimOnOff.cs          (moved, unchanged)
├── WordList/
│   ├── IWordListProvider.cs
│   ├── FixedWordListProvider.cs
│   ├── FileWordListProvider.cs
│   └── DailyWordListProvider.cs  (stub)
├── Leaderboard/
│   ├── ILeaderboardService.cs
│   └── NullLeaderboardService.cs
└── Utility/
    └── Screenshot.cs             (moved, unchanged)
```

---

## Migration Strategy

The refactor happens incrementally — each system is extracted and wired up one at a time, keeping the game playable after each step:

1. **Create folder structure** and move existing files (no logic changes)
2. **GameStateManager + state enum** — replace bool soup, existing code checks state instead of bools
3. **WordEngine extraction** — pull ParseSteps/ProcessStep into pure C# class
4. **InputHandler** — migrate to New Input System, create action asset
5. **PhaseManager + WordListProvider** — extract phase logic, add file I/O
6. **TimerSystem** — extract timer
7. **FeedbackController** — extract effect orchestration
8. **KeyboardVisualController** — replace 30 fields with list
9. **UIController** — extract remaining UI logic from GameManager
10. **SettingsManager + AudioMixer** — add settings persistence
11. **Leaderboard stubs** — interfaces only
12. **Delete old GameManager** — should be empty by this point

Each step: extract → wire up → test in editor → commit.

---

## What Stays Unchanged

- **CurTextTMPanim** — works well, just moves to UI folder
- **CircularScrollingText** — works, just moves
- **MenuAnimOnOff** — works, just moves
- **Screenshot** — works, just moves to Utility
- **CameraShakeAndZoom** — works well as-is, FeedbackController just calls it
- **KeyboardShake** — works well as-is, same deal
- **All prefabs, scenes, animations, materials** — untouched
- **StandaloneFileBrowser plugin** — untouched
- **CRT-Free shader** — untouched

---

## Open Questions (deferred, not blocking)

- **Daily list format/API**: Will be defined when server work begins. Interface is ready.
- **Steam SDK integration**: Steamworks.NET package, wired into ILeaderboardService. Added when Steam app ID exists.
- **Settings UI layout**: Build when needed, SettingsManager data layer is ready.
- **Additional key support (symbols)**: KeyboardVisualController's list approach makes adding keys trivial — just add entries.
