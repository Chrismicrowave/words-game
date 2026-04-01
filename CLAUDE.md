# words

Unity typing game targeting Steam release. Players hold a key on first letter appearance, release on second, alternating for repeated letters, progressing through word phases.

Architecture details: `docs/architecture.md`

## Tech Stack
- Unity 6 (6000.3.11f1), C#
- New Input System (1.19.0) via InputHandler
- TextMesh Pro for text rendering
- Unity Audio Mixer for volume control
- StandaloneFileBrowser plugin (screenshot save dialogs)
- CRT-Free shader package (post-processing)

## Conventions
- Script folder uses dash prefix (`-Scripts`) for sorting at top of Assets
- Core assembly (`Core.asmdef`) for shared types and systems; UI/Feedback/Audio in Assembly-CSharp
- GameCoordinator lives at `-Scripts/` root (not in Core asmdef) because it references Assembly-CSharp types
- Singletons use `Instance` pattern with `Destroy(gameObject)` guard in Awake
- Systems communicate via C# events on GameStateManager — no direct cross-references
- Systems subscribe to events in OnEnable/Start, unsubscribe in OnDisable
- InputHandler clears EventSystem selection on Enter/Backspace to prevent UI button double-triggers
- TimerSystem pauses on failed input, resumes on restart — paused time excluded from phase duration
