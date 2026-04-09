# Architecture — Words Typing Game

## Project Structure

```
Assets/-Scripts/
├── Core/                  GameStateManager, WordEngine, InputHandler,
│                          PhaseManager, TimerSystem, SettingsManager,
│                          FilterManager, BuildDefaultsApplier,
│                          SingletonBehaviour<T>
├── GameCoordinator.cs     Central wiring (Assembly-CSharp root, not in Core asmdef)
├── Feedback/              FeedbackController, CameraShakeAndZoom, KeyboardShake
├── UI/                    UIController, KeyboardVisualController,
│                          CurTextTMPanim, CircularScrollingText, MenuAnimOnOff,
│                          TimerDisplayManager, PhaseListUIManager, WordListTabManager,
│                          DisplaySettingsController, AudioSettingsController,
│                          DailyPickerPanelController, SettingsPanelController
├── Audio/                 AudioManager (with AudioMixer support)
├── WordList/              IWordListProvider, FixedWordListProvider,
│                          FileWordListProvider, DailyWordListProvider,
│                          TxtWordListImporter
├── Leaderboard/           ILeaderboardService, NullLeaderboardService (stub)
└── Utility/               Screenshot

Assets/-Data/              ScriptableObject assets (GameConfig, DemoWordList, Readme)
Assets/-Recovery/          Old/backup scene files
Assets/-pkg/               Third-party plugins (StandaloneFileBrowser)
Assets/Tests/EditMode/     WordEngine unit tests
Assets/-Anim/              Animations and animator controllers
Assets/-Audio/             Sound effects
  └── Feedback/            Keyboard/UI feedback SFX (formerly "ss/")
Assets/-Images/            Sprites and textures
Assets/-Material/          Materials
Assets/-Prefabs/           Prefabs
  ├── AnimatedBackgroundText.prefab  (reference prefab from BG TextA GO)
  ├── PhaseBtnInScrollView.prefab    (phase list button)
  └── UI/
      └── KeyButton.prefab           (reference prefab from keyboard key GO)
Assets/Scenes/             Unity scenes (single-scene architecture)
Assets/CRT-Free/           CRT post-processing shader
```

## System Diagram

```
                    ┌─────────────────┐
                    │ GameStateManager │  (state machine — central coordinator)
                    └────────┬────────┘
                             │ events
        ┌────────────┬───────┼───────┬──────────────┐
        ▼            ▼       ▼       ▼              ▼
┌─────────────┐ ┌────────┐ ┌─────┐ ┌──────────┐ ┌────────────────────────────────┐
│ InputHandler│ │WordEngine│ │Timer│ │PhaseManager│ │ UI Layer                      │
└──────┬──────┘ └───┬─────┘ └──┬──┘ └─────┬────┘ │ UIController (orchestrator)   │
       │            │          │           │      │ TimerDisplayManager            │
       │     ┌──────┘          │    ┌──────┘      │ PhaseListUIManager             │
       ▼     ▼                 ▼    ▼             │ WordListTabManager             │
┌──────────────────┐  ┌──────────────────┐        └────────────────────────────────┘
│FeedbackController│  │ WordListProvider │
│(shake,zoom,audio)│  │   (interface)    │
└──────────────────┘  └──────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
      ┌──────────────┐ ┌───────────┐ ┌──────────────┐
      │FixedListProv │ │FileListProv│ │DailyListProv │
      │  (demo)      │ │(player JSON)│ │             │
      └──────────────┘ └───────────┘ └──────────────┘
```

## Game States

```
enum GameState { Idle, Playing, PhaseFailed, PhaseComplete, AllComplete }
```

| Transition | Trigger |
|---|---|
| Idle → Playing | Phase starts, first key begins timer |
| Playing → PhaseFailed | Wrong key or wrong action (hold vs release) |
| Playing → PhaseComplete | All steps matched |
| PhaseFailed → Playing | Backspace (restart current phase) |
| PhaseComplete → Playing | Enter (next phase) |
| PhaseComplete → AllComplete | No more phases |
| AllComplete → Idle | Game reset |

## Key Design Decisions

- **Event-driven**: Systems communicate via C# events on GameStateManager. No direct references between systems except through GameCoordinator.
- **GameCoordinator**: The only class that knows about all systems. Wires input events to game logic and state transitions. Lives outside Core asmdef because it references Assembly-CSharp types (UI, Feedback).
- **WordEngine**: Pure C# class (no MonoBehaviour) for unit testability. Handles word parsing and step validation.
- **Assembly definitions**: Core.asmdef for shared types/systems, WordList.asmdef for word list providers. UI/Feedback/Audio stay in Assembly-CSharp.
- **TimerSystem**: Pauses on failed input, excludes paused time from phase duration. Resumes on restart.
- **InputHandler**: Clears EventSystem selection on Enter/Backspace to prevent UI button double-triggers.

## Singleton Pattern

All singletons extend `SingletonBehaviour<T>` (Assets/-Scripts/Core/SingletonBehaviour.cs):

```csharp
public abstract class SingletonBehaviour<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance { get; private set; }
    protected virtual void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this as T;
    }
}
```

**Singletons using this base:** `GameStateManager`, `InputHandler`, `PhaseManager`, `TimerSystem`, `SettingsManager`, `FilterManager`, `CameraShakeAndZoom`, `KeyboardShake`

## UI Architecture

`UIController` is the gameplay-display orchestrator — it handles the cursor blink loop, matched text display, delete animation, and panel toggles. Three sub-managers handle focused concerns:

| Class | Responsibility |
|---|---|
| `TimerDisplayManager` | Subscribes to `TimerSystem.OnTimerUpdated`, updates phase/total timer TMP labels |
| `PhaseListUIManager` | Builds and refreshes the phase scroll list; tracks selection index |
| `WordListTabManager` | Tab switching (My List / Daily), word list provider initialization, DailyPicker integration |

## Settings Architecture

Settings flow on startup:

1. **`BuildDefaultsApplier`** (Editor-only): writes PlayerPrefs defaults for new builds
2. **`SettingsManager`**: loads PlayerPrefs on Awake, exposes key constants and typed getters/setters, applies audio mixer volumes via `AudioMixer.SetFloat`
3. **`DisplaySettingsController`** / **`AudioSettingsController`**: Settings panel UI — reads from SettingsManager, writes back on change, calls `FilterManager` for shader toggles

**PlayerPrefs key registry** — all keys are constants on `SettingsManager`:

| Constant | Key string | Type |
|---|---|---|
| `KeyVolumeMaster` | `"VolumeMaster"` | float |
| `KeyVolumeMusic` | `"VolumeMusic"` | float |
| `KeyVolumeSFX` | `"VolumeSFX"` | float |
| `KeyCRTFilter` | `"CRTFilter"` | bool (0/1) |
| `KeyScreenShake` | `"ScreenShake"` | bool (0/1) |
| `KeyWordsPanelOn` | `"WordsPanelOn"` | bool (0/1) |
| `KeyTimerPanelOn` | `"TimerPanelOn"` | bool (0/1) |
| `KeyInfoPanelOn` | `"InfoPanelOn"` | bool (0/1) |
| `KeyActiveTab` | `"ActiveTab"` | string ("mylist"/"daily") |

## Event Subscription Lifecycle

All MonoBehaviours that listen to events subscribe in `OnEnable` and unsubscribe in `OnDisable`. This pattern applies to: `UIController`, `KeyboardVisualController`, `FeedbackController`, `TimerDisplayManager`, `PhaseListUIManager`.

## Word List Pipeline

```
DailyLists/          ← drop JSON files here (StreamingAssets/DailyLists/)
     │
     ▼
DailyPickerPanelController  ← scans folder, shows picker UI
     │
     ▼
DailyWordListProvider       ← IWordListProvider backed by JSON file
     │
     ▼
PhaseManager.LoadWordList() ← replaces active provider, fires OnWordListChanged
     │
     ▼
PhaseListUIManager          ← rebuilds scroll list
WordListTabManager          ← persists chosen provider path in PlayerPrefs
```

**To add a new daily list:** drop a `.json` file matching the `WordListData` schema into `StreamingAssets/DailyLists/`. The picker UI will discover it automatically.

## Integration Touch Points

- **IWordListProvider**: DailyWordListProvider ready for daily JSON lists
- **ILeaderboardService**: NullLeaderboardService stub ready for Steam/web leaderboard
- **AudioMixer**: Routed through SettingsManager for volume control
- **KeyButton prefab** (`Assets/-Prefabs/UI/KeyButton.prefab`): reference prefab only — existing 50+ key GOs are not yet prefab instances (relinking KeyboardVisualController mappings deferred)
