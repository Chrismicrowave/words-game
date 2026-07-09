# Service Locator Refactor — Words Typing Game

**Branch:** `feat/service-locator` | **Completed:** 2026-07-10

---

## Phase 1: Create Infrastructure ✅
- [x] 1.1 Create `Services.cs` registry
- [x] 1.2 Update `SingletonBehaviour<T>` to auto-register

## Phase 2: Migrate Consumers ✅
- [x] 2.1 KeyboardShake (1 caller)
- [x] 2.2 CameraShakeAndZoom (1 caller)
- [x] 2.3 FilterManager (1 caller)
- [x] 2.4 TimerSystem (3 callers)
- [x] 2.5 SettingsManager (5 callers)
- [x] 2.6 InputHandler (8 callers)
- [x] 2.7 GameStateManager (10+ callers)
- [x] 2.8 PhaseManager (15+ callers)

**~100 `.Instance` → `Services.Get<T>()` replacements across 20 files.**

## Phase 3: Cleanup ✅
- [x] 3.1 `Instance` property removed from SingletonBehaviour
- [x] 3.2 SingletonBehaviour uses Services directly
- [x] 3.3 Old `Instance != this` guards removed

## Phase 4: Architecture Smells ✅
- [x] 4.1 `using Unity.VisualScripting` removed
- [x] 4.2 architecture.md — deferred (docs need full rewrite)
- [x] 4.3 OnGameStartManager audited — NOT a duplicate (different purpose from BuildDefaultsApplier)
- [x] 4.4 WordListTabManager — deferred (minor, no behavioral impact)
- [x] 4.5 FixedWordListProvider — deferred (documentation only)
- [x] 4.6 `com.unity.visualscripting` package removed
- [x] 4.7 `com.unity.multiplayer.center` package removed

## Phase 5: Final Verification ✅
- [x] Game compiles, zero new errors
- [x] Game starts, loads challenge_01
- [x] ModeHUD shows Challenge tag
- [x] All 8 services accessible via Services.Get<T>()
- [x] No .Instance calls remain in -Scripts/

---

## Result

**8 singletons** migrated from `SingletonBehaviour<T>.Instance` to `Services.Get<T>()`:
`GameStateManager`, `InputHandler`, `PhaseManager`, `TimerSystem`, `SettingsManager`,
`FilterManager`, `CameraShakeAndZoom`, `KeyboardShake`

**2 unused packages** removed: `visualscripting`, `multiplayer.center`

**New file:** `Assets/-Scripts/Core/Services.cs` — the Service Locator registry.
