# Architecture — Words Typing Game

## Project Structure

```
Assets/-Scripts/
├── Core/                  GameStateManager, WordEngine, InputHandler,
│                          PhaseManager, TimerSystem, SettingsManager
├── GameCoordinator.cs     Central wiring (Assembly-CSharp root, not in Core asmdef)
├── Feedback/              FeedbackController, CameraShakeAndZoom, KeyboardShake
├── UI/                    UIController, KeyboardVisualController,
│                          CurTextTMPanim, CircularScrollingText, MenuAnimOnOff
├── Audio/                 AudioManager (with AudioMixer support)
├── WordList/              IWordListProvider, FixedWordListProvider,
│                          FileWordListProvider, DailyWordListProvider (stub)
├── Leaderboard/           ILeaderboardService, NullLeaderboardService (stub)
└── Utility/               Screenshot

Assets/-Data/              ScriptableObject assets (DemoWordList)
Assets/Tests/EditMode/     WordEngine unit tests
Assets/-Anim/              Animations and animator controllers
Assets/-Audio/             Sound effects
Assets/-Images/            Sprites and textures
Assets/-Material/          Materials
Assets/-Prefabs/           Prefabs
Assets/Scenes/             Unity scenes (single-scene architecture)
Assets/CRT-Free/           CRT post-processing shader
Assets/StandaloneFileBrowser/  Native file dialog plugin
```

## System Diagram

```
                    ┌─────────────────┐
                    │ GameStateManager │  (state machine — central coordinator)
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
      │FixedListProv │ │FileListProv│ │DailyListProv │
      │  (demo)      │ │(player JSON)│ │  (stub)     │
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

## Integration Touch Points (future)

- **IWordListProvider**: DailyWordListProvider stub ready for HTTP fetch
- **ILeaderboardService**: NullLeaderboardService stub ready for Steam/web leaderboard
- **SettingsManager**: Data layer ready for settings UI panel
- **AudioMixer**: Routed through SettingsManager for volume control
