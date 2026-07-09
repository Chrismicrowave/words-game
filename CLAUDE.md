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
- After 2 failed fixes: search online (include "Unity 6" in queries) and audit Editor thoroughly for missing references, duplicate components, and stale statics.

## UI Element Creation
- Use built-in Unity UI elements (Slider, InputField, etc.) via DefaultControls/ExecuteMenuItem only — never build from primitives via MCP without asking first.

## Editor Workflow
- Use Unity MCP tools for all Editor tasks. Never ask the user to do manual Editor actions unless MCP truly can't. All scene text must be TextMeshProUGUI.

## Prefab / Scene Safety
- **Rebuild scripts are one-time only.** Once tweaked in Editor, never re-run — they wipe children.
- **Targeted edits only on existing prefabs.** Use `set_property`, never child-destruction.
- **Commit after every MCP prefab/scene edit** — keeps my changes separate from user's.
- **Revert by specific commit hash, never HEAD.** Warn before any destructive git op if `.prefab`/`.unity` files are affected.
- **Prompt to commit editor changes first** before touching the same prefab/scene file.

## Asset Deletion Safety
- **GUID-check before deleting any asset.** Grep target `.meta` GUIDs against the scene and prefabs.
- **TMP `Examples & Extras/` fonts are in use** — verify GUIDs before deleting demo-content-looking folders.
- **Git checkout restore may still fail** — Unity Library/ cache can hold missing state. Force reimport or delete Library/.

## Conventions
- `-Scripts/` sorts at top of Assets. Core.asmdef for shared types; UI/Feedback/Audio in Assembly-CSharp.
- GameCoordinator lives at `-Scripts/` root (references Assembly-CSharp types).
- Singletons: `Instance` pattern with `Destroy(gameObject)` guard in Awake.
- Systems communicate via C# events on GameStateManager — no direct cross-references.
- Subscribe in OnEnable/Start, unsubscribe in OnDisable.
- InputHandler clears EventSystem selection on Enter/Backspace to prevent button double-triggers.
- TimerSystem pauses on failed input, resumes on restart — paused time excluded from phase duration.


[UCC-START — do not edit]
## Universal Unity Rules (from ucc-gateway v0.1.1)

- **Never `??` with Unity Objects** — use `if (x == null)` only
- **Always use the new Input System** — never `Input.GetKey`
- **Editor values beat script defaults** — check inspector first
- **Stop Play Mode before structural edits** — changes don't persist
- **Script controls initial active state** — Awake()/Start(), not GO toggle
- **Wire references via MCP** — never tell user to do it
- **No name-based lookups** — use stableId or inspector references
- **RefHub for shared systems** — single source for cameras, UI, managers
- **Use ucc-gateway for all scene/asset/code queries** — faster, no round-trips
[UCC-END]