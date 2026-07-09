# Project Analysis — Words Typing Game

**Date:** 2026-07-10 | **Branch:** `feat/levelsProgression` | **Unity:** 6000.3.11f1

---

## Game Summary

Keyboard typing game. Players hold/release letter keys to match words one phase at a time. Alternating hold-release-hold pattern per letter. Supports English, Chinese, and Mixed-language word lists.

**Current features:**
- 20 challenge levels with progressive unlock + 3-star rating
- Custom word lists (import/export .txt, create/edit/delete)
- Trending tab (placeholder for future social features)
- Daily word lists from JSON
- Chinese/Mixed language with pinyin overlay
- Full localization (EN / ZH-Hans) via Unity Localization
- CRT post-processing filter
- Screen shake, keyboard shake, audio feedback
- Per-phase timer with total elapsed time
- Settings: audio (master/SFX/BGM), display (fullscreen/resolution/CRT/shake), gameplay
- Screenshot save via StandaloneFileBrowser
- Demo/debug toggles (unlockAllChallenges, unlockCustomTab)

---

## Core Engine (`Core.asmdef`)

| Class | Lines | Role |
|---|---|---|
| `WordEngine` | ~270 | Pure C# (no MB). Parses input, generates `Step[]` per word, validates hold/release |
| `GameStateManager` | ~60 | State machine: `Idle → Playing → PhaseFailed → PhaseComplete → AllComplete`. Fires C# events per transition |
| `InputHandler` | ~115 | New Input System → legacy `KeyCode` events (`OnKeyAction`, `OnBackspacePressed`, `OnEnterPressed`). `SetGameplayBlocked()` gate |
| `PhaseManager` | ~215 | Active word list holder, phase index, language mode, error counter. `OnWordListChanged` / `OnPhaseWordChanged` events |
| `TimerSystem` | ~70 | Phase + total elapsed time. Pauses on failure, excludes paused time |
| `SettingsManager` | ~185 | PlayerPrefs-backed settings (typed getters/setters), audio mixer / display / CRT application |
| `FilterManager` | ~30 | CRT-Free shader wrapper: `SetFilter(index, enabled)` |
| `BuildDefaultsApplier` | ~50 | First-launch PlayerPrefs defaults (`ExecuteOrder=-50`) |
| `SingletonBehaviour<T>` | ~18 | `Instance` pattern base with duplicate-destroy guard |
| `GameConfig` | ~5 | ScriptableObject: `isDemo` flag |
| `GameState` | ~2 | Enum + `Step`/`StepResult` structs |
| `LevelMode` | ~4 | `Challenge` / `Custom` enum |

## Central Coordinator

| Class | Lines | Role |
|---|---|---|
| `GameCoordinator` | ~295 | **Only class that knows all systems.** Lives at `-Scripts/` root (not Core asmdef — references Assembly-CSharp types). Wires input → state transitions → UI/feedback. Loads first challenge at start |

## Word List Providers (`WordList.asmdef`)

| Class | Lines | Source | Editable |
|---|---|---|---|
| `IWordListProvider` (interface) | ~10 | — | — |
| `LevelWordListProvider` | ~165 | JSON `.txt` in `StreamingAssets/Levels/` | challenge=no, custom=yes |
| `FileWordListProvider` | ~135 | `.json` in `persistentDataPath/WordLists/` | yes |
| `DailyWordListProvider` | ~95 | `.json` in `StreamingAssets/DailyLists/` | no |
| `FixedWordListProvider` | ~15 | ScriptableObject (inspector) | no |
| `MixedPhaseParser` | ~175 | Static: Chinese/English segmentation via `MixedWordEntry` segments |
| `PinyinLookup` | ~40 | Static: Chinese chars → pinyin, loads from `Resources/PinyinLookup.json` |
| `TxtWordListImporter` | ~35 | Static: import/export `.txt` ↔ `FileWordListProvider` |

## UI Layer (Assembly-CSharp)

**Orchestrators:**

| Class | Lines | Role |
|---|---|---|
| `UIController` | ~500 | **Too large.** Gameplay display: cursor blink, matched text, delete animation, panel toggles, phase edit buttons, import/export, Chinese phase handling |
| `KeyboardVisualController` | ~90 | Key cap highlighting (hold/release colors), flash coroutine |
| `LevelPanelController` | ~340 | Challenge/Custom/Trending tab grid, locked states, star display, selection persistence, completion handling |
| `SettingsPanelController` | ~75 | Settings tab switching |
| `DisplaySettingsController` | ~165 | Fullscreen, CRT, resolution, screen shake, language toggles |
| `AudioSettingsController` | ~60 | Master/SFX/BGM sliders → SettingsManager |
| `DailyPickerPanelController` | ~140 | Daily list picker with search |

**Sub-managers:**

| Class | Lines | Role |
|---|---|---|
| `TimerDisplayManager` | ~30 | Subscribes to TimerSystem, formats labels |
| `PhaseListUIManager` | ~100 | Phase scroll list builder |
| `WordListTabManager` | ~105 | My List / Daily tab init, edit button visibility |
| `LevelCompletePanelController` | ~80 | Challenge clear panel |
| `ModeHUDController` | ~40 | Challenge/Custom tag |

**Chinese display system:**
| Class | Lines | Role |
|---|---|---|
| `ChineseDisplayController` | ~60 | Facade for matched + target displays |
| `ChineseTargetDisplay` | ~140 | Target cells with pinyin overlay (TargetCell prefabs) |
| `ChineseMatchedDisplay` | ~150 | Matched cells for mixed-language phases (CharacterCell prefabs) |
| `ChinesePinyinPopup` | ~200 | Pinyin confirmation popup |

**Cell components:**
`CharacterCell`, `TargetCell`, `EnglishCell`

**Feedback UI:**
`FailBGBridge` (~30), `FailFlashController` (~35), `CurTextTMPanim` (~190), `MenuAnimOnOff` (~48)

**Localization:**
`LocalizeText`, `LocalizePlaceholder`, `LocalizationBootstrapper`, `LocalizationService`, `DropdownTMPBridge`

## Feedback Layer (Assembly-CSharp)

| Class | Lines | Role |
|---|---|---|
| `FeedbackController` | ~130 | Subscribes to `OnStepProcessed`, triggers audio + shake |
| `CameraShakeAndZoom` | ~105 | Mild/strong shake + overshoot-zoom coroutines |
| `KeyboardShake` | ~75 | Perlin-noise keyboard sprite shake |

## Audio

| Class | Lines | Role |
|---|---|---|
| `AudioManager` | ~80 | AudioSource + AudioMixerGroup, per-clip playback, pitch shifting |

## Other Systems

| Class | Lines | Role |
|---|---|---|
| `ChallengeProgression` | ~50 | Static: PlayerPrefs unlock count + star ratings |
| `OnGameStartManager` | ~20 | Inspector-driven GO active state setup |
| `StableId` | ~5 | Stable ID component (MCP/UCC) |
| `DeactiveOnStart` | ~5 | Disables GO in Start |
| `SaveScreenshot` | ~30 | Save screenshot to PNG via file dialog |

---

## Scene Hierarchy (simplified)

```
Main Camera
Directional Light
Background (Animated BG text elements)
GameSystems GO:
  ├── GameStateManager, InputHandler, PhaseManager, TimerSystem
  ├── SettingsManager, FilterManager, BuildDefaultsApplier
  ├── FeedbackController, CameraShakeAndZoom, KeyboardShake
  ├── AudioManager × 2 (audioKeys, audioResult)
  ├── FailBGBridge, OnGameStartManager
  └── GameCoordinator

--- UI --- (Canvas):
  ├── Main display: MatchedText, TargetTMP, PhaseInputField
  ├── Chinese: ChineseMatchedDisplay, ChineseTargetDisplay
  ├── HUD buttons: Words/Timer/Info/Reset/Settings/Close/Play
  ├── Keyboard area: 50+ KeyButton instances (key visualizer)
  ├── Panels: WordListPanel, ModeHUD, LevelCompletePanel, Timer panel
  └── Menus: SettingsPanel, DailyPickerPanel, ChinesePinyinPopup, LevelPanel
```

## Prefab Inventory

14 prefabs in `Assets/-Prefabs/`: `LevelCellButton`, `TabBtn1`, `WordLabel`, `KeyButton`, `CharacterCell`, `EnglishCell`, `EnglishTargetCell`, `TargetCell`, `TargetCellCustomAdd`, `PhaseBtnInScrollView`, `AnimatedBackgroundText` ×2, `keyText`

## Package Dependencies

| Package | Used | Notes |
|---|---|---|
| `com.unity.ugui` 2.0.0 | ✅ | Canvas + TMP |
| `com.unity.inputsystem` 1.19 | ✅ | Input |
| `com.unity.localization` 1.5.11 | ✅ | String Tables |
| `com.unity.render-pipelines.universal` 17.3 | ✅ | CRT filter |
| `com.unity.ai.navigation` 2.0.11 | ⚠️ | NavMesh on 3 prefabs (BG text elements) |
| `com.unity.visualscripting` 1.9.10 | ❌ | Unused (`using Unity.VisualScripting` is vestigial) |
| `com.unity.multiplayer.center` 1.0.1 | ❌ | Not used |
| `com.unity.test-framework` 1.6 | ✅ | EditMode tests |
| `com.coplaydev.coplay` | ✅ | MCP |

## PlayerPrefs Key Registry

| Key | Type | Default | Source |
|---|---|---|---|
| `settings_masterVolume` | float | 0.7 | Audio |
| `settings_sfxVolume` | float | 0.7 | Audio |
| `settings_bgmVolume` | float | 0.7 | Audio |
| `settings_fullscreen` | int (bool) | 1 | Display |
| `settings_resolution` | int | 1 (1080p) | Display |
| `settings_crtFilter` | int (bool) | 1 | Display |
| `settings_screenShake` | int (bool) | 1 | Gameplay |
| `settings_actionPrompts` | int (bool) | 1 | Gameplay |
| `settings_uiLanguage` | string | "en" | Display |
| `ShowPinyin` | int (bool) | 1 | Display |
| `WordsPanelOn` | int (bool) | 0 | UI State |
| `TimerPanelOn` | int (bool) | 0 | UI State |
| `InfoPanelOn` | int (bool) | 1 | UI State |
| `ActiveTab` | string | "daily" | UI State |
| `ChallengeUnlockedCount` | int | 1 | Progression |
| `ChallengeStar_N` | int | 0 | Progression |
| `LevelPanel_LastPath` | string | "" | LevelPanel |
| `LevelPanel_LastTab` | string | "Challenges" | LevelPanel |
| `MyListPath` | string | (default) | WordList |

## Known Issues & Architecture Smells

1. **UIController is 75 symbols, ~500 lines** — handles too much. Gameplay display + panel management + import/export + Chinese phases + delete animation + panel state save/restore
2. **CurTextTMPanim** — complex vertex animation (190 lines) but unclear if it's still used
3. **`using Unity.VisualScripting`** in `CameraShakeAndZoom.cs` — vestigial import from a package that's only in manifest because it ships by default
4. **OnGameStartManager** — same functionality as `BuildDefaultsApplier` (GO active state at start), possible duplication
5. **AudioManager has two instances** — `audioKeys` and `audioResult` on `GameSystems` — not singletons
6. **WordListTabManager.Start() loads mylist provider but doesn't use it** — no longer loads into PhaseManager since challenge-first change
7. **architecture.md is stale** — references `CircularScrollingText`, `-Recovery/`, `-pkg/` which no longer exist
8. **FixedWordListProvider defaultWordList** field on GameCoordinator — potentially unused since challenge-first loading was added
