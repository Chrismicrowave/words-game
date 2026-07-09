# Service Locator Refactor — Words Typing Game

**Context:** Replace SingletonBehaviour<T>.Instance pattern with a Service Locator. 8 singletons currently use static Instance properties with ~100 call sites across 20 files. Service Locator keeps the same mental model (one registry, one of each service) but adds testability and extensibility without third-party packages.

**Branch:** `feat/service-locator` (new, from `feat/levelsProgression`)

---

## Phase 1: Create Infrastructure (zero breakage)

### Step 1.1: Create `Services.cs` registry
**File:** `Assets/-Scripts/Core/Services.cs`

```csharp
public static class Services
{
    static readonly Dictionary<Type, object> registry = new();

    public static void Register<T>(T instance) where T : class
        => registry[typeof(T)] = instance;

    public static T Get<T>() where T : class
        => registry.TryGetValue(typeof(T), out var obj) ? obj as T : null;
}
```

**Verify:** Compile. No consumers yet — zero impact.

### Step 1.2: Update `SingletonBehaviour<T>` to auto-register
**File:** `Assets/-Scripts/Core/SingletonBehaviour.cs`

Add `Services.Register<T>(Instance)` after `Instance = this as T` in Awake. Both `.Instance` and `Services.Get<T>()` now work simultaneously.

**Verify:** Compile + play game + check console for errors. Existing code unchanged.

---

## Phase 2: Migrate Consumers (one singleton at a time)

Each step: replace `.Instance` → `Services.Get<T>()` in all callers, then **compile + playtest + check for errors**. Push after each.

### Order (least → most coupled):

| # | Singleton | Callers |
|---|-----------|---------|
| 2.1 | `KeyboardShake` | 1 caller (GameCoordinator) |
| 2.2 | `CameraShakeAndZoom` | 1 caller (GameCoordinator) |
| 2.3 | `FilterManager` | 2 callers (SettingsManager, DisplaySettingsController) |
| 2.4 | `TimerSystem` | 3 callers (GameCoordinator, LevelPanelController, TimerDisplayManager) |
| 2.5 | `SettingsManager` | 5 callers (DisplaySettingsController, AudioSettingsController, BuildDefaultsApplier, etc.) |
| 2.6 | `InputHandler` | 8 callers (GameCoordinator, LevelPanelController, various panels) |
| 2.7 | `GameStateManager` | 10+ callers (GameCoordinator, FeedbackController, KeyboardVisualController, FailBGBridge, etc.) |
| 2.8 | `PhaseManager` | 15+ callers (everywhere — GameCoordinator, UIController, WordListTabManager, LevelPanelController, etc.) |

**Migration pattern per file (example for GameCoordinator):**
```csharp
// Before:
PhaseManager.Instance.LoadWordList(challenges[0]);
// After:
Services.Get<PhaseManager>().LoadWordList(challenges[0]);
```

**Null checks:** Replace `if (PhaseManager.Instance != null)` with `if (Services.Get<PhaseManager>() != null)`.

**Verify per step:** Compile → play game → test affected system → push commit.

---

## Phase 3: Cleanup

### Step 3.1: Remove `Instance` property from `SingletonBehaviour<T>`
Only after all consumers migrated. Remove `public static T Instance { get; private set; }`.

### Step 3.2: Update `SingletonBehaviour<T>.Awake`
Replace `Instance = this as T` with direct `Services.Register<T>(this as T)`.

### Step 3.3: Remove remaining `Instance` guards
Remove patterns like `if (Instance != this) return;` in Awake overrides that check Instance — use `Services.Get<T>()` instead.

**Verify:** Full playtest. Game plays identically.

---

## Phase 4: Architecture Smells from Analysis

Address the 8 known issues from the project analysis doc:

| # | Issue | Fix |
|---|-------|-----|
| 4.1 | `using Unity.VisualScripting` vestigial | Remove from CameraShakeAndZoom.cs |
| 4.2 | Stale `architecture.md` | Update to current project state |
| 4.3 | `OnGameStartManager` — possible duplicate of BuildDefaultsApplier | Audit and remove if redundant |
| 4.4 | `WordListTabManager.Start()` loads provider but doesn't use it | Clean up dead code path |
| 4.5 | Mark `FixedWordListProvider defaultWordList` as fallback only | No code change, document |
| 4.6 | Remove `com.unity.visualscripting` package | Already unused, remove from manifest |
| 4.7 | Remove `com.unity.multiplayer.center` package | Already unused, remove from manifest |

**Verify per fix:** Compile. No behavioral changes.

---

## Phase 5: Final Verification

### Full regression playtest checklist:
- [ ] Game starts, loads challenge_01 "First Steps"
- [ ] Type all phases, word complete sound plays
- [ ] LevelCompletePanel shows with correct name + stars
- [ ] Enter loads next challenge, unlocks it
- [ ] Open LevelPanel — Challenges tab shows locked/unlocked state
- [ ] Custom tab shows list, can select and load
- [ ] WordList panel — edit buttons show/hide based on editable
- [ ] ModeHUD shows correct tag (Challenge/Custom)
- [ ] Settings panel — all toggles and sliders work
- [ ] Timer display updates correctly
- [ ] Backspace resets current phase
- [ ] CRT filter, screen shake, audio feedback all work
- [ ] Close and reopen game — progress persists
- [ ] Reset Challenge Progress (Tools menu) works
- [ ] Zero compile warnings (except pre-existing TMP obsolete warning)
- [ ] Zero runtime errors in console

---

## Rollback Plan

Every step is a git commit. If any step breaks, `git revert <hash>` and investigate before retrying. The `Instance` property stays during migration (Phase 2) so both access patterns work simultaneously — broken consumers can fall back to `.Instance` while debugging.
