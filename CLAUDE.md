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

## Debugging Rules
- After 2 failed fix attempts on the same bug: search online (WebSearch) before trying again — do not rely solely on internal reasoning
- Always search for Unity-version-specific solutions (include "Unity 6" in queries)
- After 2 failed fix attempts: also audit the Unity Editor thoroughly for missing references (e.g. unassigned slider OnClick callbacks), duplicate components (e.g. two SettingsManager instances), and stale static declarations — code bugs and Editor wiring bugs are equally likely

## UI Element Creation
- Always use Unity's built-in UI elements (Slider, InputField, Toggle, Dropdown, ScrollView, etc.) via DefaultControls or ExecuteMenuItem — never build them manually from scratch via MCP create_game_object
- Only build custom UI from primitives if the built-in element genuinely cannot do the job, and ask the user first before doing so

## Editor Workflow
- Always use Unity MCP (coplay-mcp) tools for all Unity Editor tasks — creating assets, wiring components, building scene hierarchy, etc.
- Never ask the user to perform Unity Editor actions manually unless the MCP tool truly cannot do it
- All scene text must use TextMeshProUGUI (TMP) — never legacy Unity Text component

## Prefab / Scene Safety Rules (STRICT — never violate)
- **Rebuild scripts are one-time only.** Once a prefab has been manually tweaked in the Editor, NEVER re-run its rebuild script. Rebuild scripts wipe all children and recreate from scratch, destroying Inspector changes.
- **Targeted edits only on existing prefabs.** Use MCP `set_property` for individual field changes. Never use `EditPrefabContentsScope` with child destruction on a prefab that has user editor changes.
- **Check before reverting.** Before any `git revert`, `git reset`, or destructive git operation, run `git diff --stat -- Assets/` and warn the user if `.prefab` or `.unity` files are affected. Get explicit confirmation before proceeding.
- **Prompt to commit editor changes first.** If the user has made Inspector tweaks and I need to touch the same prefab/scene file, ask them to commit their changes before I proceed.

## Conventions
- Script folder uses dash prefix (`-Scripts`) for sorting at top of Assets
- Core assembly (`Core.asmdef`) for shared types and systems; UI/Feedback/Audio in Assembly-CSharp
- GameCoordinator lives at `-Scripts/` root (not in Core asmdef) because it references Assembly-CSharp types
- Singletons use `Instance` pattern with `Destroy(gameObject)` guard in Awake
- Systems communicate via C# events on GameStateManager — no direct cross-references
- Systems subscribe to events in OnEnable/Start, unsubscribe in OnDisable
- InputHandler clears EventSystem selection on Enter/Backspace to prevent UI button double-triggers
- TimerSystem pauses on failed input, resumes on restart — paused time excluded from phase duration
