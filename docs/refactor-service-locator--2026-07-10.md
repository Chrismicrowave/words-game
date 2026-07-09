# Service Locator Refactor — Words Typing Game

**Branch:** `feat/service-locator` | **Started:** 2026-07-10

---

## Phase 1: Create Infrastructure ✅

- [x] 1.1 Create `Services.cs` registry (`Assets/-Scripts/Core/Services.cs`)
- [x] 1.2 Update `SingletonBehaviour<T>` to auto-register with Services

## Phase 2: Migrate Consumers ✅

- [x] 2.1 KeyboardShake (1 caller)
- [x] 2.2 CameraShakeAndZoom (1 caller)
- [x] 2.3 FilterManager (1 caller)
- [x] 2.4 TimerSystem (3 callers)
- [x] 2.5 SettingsManager (5 callers)
- [x] 2.6 InputHandler (8 callers)
- [x] 2.7 GameStateManager (10+ callers)
- [x] 2.8 PhaseManager (15+ callers)

All ~100 `.Instance` call sites replaced with `Services.Get<T>()`.

## Phase 3: Cleanup ✅

- [x] 3.1 Remove `Instance` property from SingletonBehaviour
- [x] 3.2 SingletonBehaviour uses Services directly
- [x] 3.3 Remove `Instance != this` guards from InputHandler, SettingsManager

## Phase 4: Architecture Smells

- [ ] 4.1 Remove `using Unity.VisualScripting` from CameraShakeAndZoom.cs
- [ ] 4.2 Update stale `architecture.md`
- [ ] 4.3 Audit `OnGameStartManager` vs `BuildDefaultsApplier` duplication
- [ ] 4.4 Clean WordListTabManager dead code path
- [ ] 4.5 Document FixedWordListProvider as fallback only
- [ ] 4.6 Remove `com.unity.visualscripting` package
- [ ] 4.7 Remove `com.unity.multiplayer.center` package

## Phase 5: Final Verification

- [ ] Full regression playtest
