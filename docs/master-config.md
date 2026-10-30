# Master Config

The `GameConfig` asset at `Assets/-Data/GameConfig.asset` selects the active `GameConfigProfile`. The same asset is referenced by the startup defaults component, `PhaseManager`, and `LevelPanelController`, so changing one profile controls startup defaults and implemented feature gates together.

The supplied profiles are `Assets/-Data/Profiles/Development.asset`, `Demo.asset`, and `Playtest.asset`. Development is selected by default. Use the Master Config window to select another profile or duplicate one; duplicates receive a new profile ID so one-time reset markers remain distinct. Use **Build with active profile** in that window to apply the selected profile's window settings for that build. The build helper restores the existing Project Settings afterward. The normal Unity Build command does not apply a profile.

## Profiles

- **Development** is the default active profile. It preserves the current development scene behavior: Chinese and mixed gameplay is allowed, the demo limits remain five challenge levels, one custom list, and five words per list, and the Custom tab is available immediately. Adding and editing words remain enabled; custom-list create, delete, duplicate, import, and export remain restricted.
- **Demo** applies the same five-level, one-list, five-word limits and unlocks the Custom tab after five completed challenges. The list-count and word-count caps remain active after that unlock.
- **Playtest** has no demo caps or temporary unlocks. Custom remains locked until five challenges are completed. It is intended for testing the normal progression path.

All three profiles start with a 1920×1080 window, windowed and non-resizable. They use the current visual and audio defaults, English UI, pinyin visible, the default player name, the existing panel defaults, and the Challenges tab as the first tab. The profile's window values also feed the build-time window settings.

The `actionPrompts` default now controls the gameplay key-action hint through `UIController`; the existing instruction-panel toggle remains a separate setting. **Reset to Defaults** reapplies the selected profile's missing-key defaults immediately.

## Feature toggles

`unlockAllFeatures` bypasses implemented feature gates and demo limits without writing challenge progress, stars, or achievements. `unlockAllChallenges` bypasses challenge locks and the demo challenge cap. `unlockCustomLists` opens Custom without changing progression. Create, edit, delete, duplicate, import, export, and add-word overrides control only their own actions; they do not open the Custom tab. Chinese gameplay remains controlled separately by `allowChineseAndMixed`. The Community panel is still a placeholder and is not enabled by these toggles.

## Defaults and resets

Profile defaults fill PlayerPrefs keys only when those keys are missing. Selecting another profile does not overwrite existing preferences, progress, or local files. Profiles are configuration combinations, not separate save namespaces; they share local saves and the signed-in Steam account's remote achievements.

The Master Config **Reset now** action explicitly previews and executes `AllLocal`; it creates recoverable backups. `AllLocal` includes settings, navigation memory, challenge progress, time records, My Lists, Custom lists, and local achievement profiles. It does not clear Steam achievements.

Each profile has two independent, default-off Master Reset flags. `resetAllLocalDataOnNextEditorPlay` arms a one-shot full local reset for the next Editor Play; the Editor hook clears the flag only after the reset succeeds and keeps it armed if reset fails. `resetAllLocalDataOnFirstBuildRun` makes each build produced with **Build with active profile** carry a fresh reset token, consumed once on that build's first run. Building itself does not reset Editor data. A build run on the same machine still uses the game's shared local data and will reset that data on first launch, so keep this enabled only for builds where that is intended.

**Arm both** sets both flags. All supplied templates have both flags disabled. These flags are separate from the advanced `resetLocalDataOnFirstRun` plus `resetTargets` recipe, which remains available for per-profile scoped resets and manual revision-based reruns. If a Master Reset flag and the advanced recipe are both enabled, the Master Config UI warns that both reset paths are armed; use the full reset alone when a single reset is intended. Profiles remain configuration combinations, not separate save namespaces. A local achievement-profile reset does not clear Steam's remote achievement state; remote Steam reset remains a separate explicit action.
