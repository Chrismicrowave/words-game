# words

Unity typing game targeting Steam release. Players hold a key on first letter appearance, release on second, alternating for repeated letters, progressing through word phases.

## Tech Stack
- Unity 6 (6000.3.11f1), C#
- New Input System (1.19.0) via InputHandler
- TextMesh Pro for text rendering
- Unity Audio Mixer for volume control
- StandaloneFileBrowser plugin (screenshot save dialogs)
- CRT-Free shader package (post-processing)

## Project Structure
- `Assets/-Scripts/Core/` — GameStateManager, GameCoordinator, WordEngine, InputHandler, PhaseManager, TimerSystem, SettingsManager
- `Assets/-Scripts/Feedback/` — FeedbackController, CameraShakeAndZoom, KeyboardShake
- `Assets/-Scripts/UI/` — UIController, KeyboardVisualController, CurTextTMPanim, CircularScrollingText, MenuAnimOnOff
- `Assets/-Scripts/Audio/` — AudioManager (with AudioMixer support)
- `Assets/-Scripts/WordList/` — IWordListProvider, FixedWordListProvider, FileWordListProvider, DailyWordListProvider (stub)
- `Assets/-Scripts/Leaderboard/` — ILeaderboardService, NullLeaderboardService (stub)
- `Assets/-Scripts/Utility/` — Screenshot
- `Assets/-Data/` — ScriptableObject assets (DemoWordList)
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
- IWordListProvider interface: FixedWordListProvider (demo), FileWordListProvider (player custom JSON), DailyWordListProvider (future API)
- ILeaderboardService interface ready for Steam/web leaderboard integration
- SettingsManager persists player preferences via PlayerPrefs

## Conventions
- Script folder uses dash prefix (`-Scripts`) for sorting at top of Assets
- Core assembly (`Core.asmdef`) for shared types and systems; UI/Feedback/Audio in Assembly-CSharp
- Singletons use `Instance` pattern with `Destroy(gameObject)` guard in Awake
- Systems subscribe to events in OnEnable/Start, unsubscribe in OnDisable
