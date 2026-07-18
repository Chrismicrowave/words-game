# Implementation: Mixed Display Layout + Word Trimming

**Spec:** `docs/mixed-display-layout-spec.md`
**Cleanup:** 🧹 Delete this file once all steps are crossed off and final verification passes.

## Steps

- [ ] **Step 1: Add trimming utility — filter over-long words from gameplay**
  Add a `DisplayWidth` static helper to a utility class or `PhaseManager`. Compute display width where Chinese char = 1 unit, English letter = 0.5 unit.
  In `PhaseManager.LoadWordList()`: for Chinese/Mixed modes, filter out words exceeding 48 display-width units. For English mode, filter out words exceeding 140 letters. Remove from both `words` list and corresponding `mixedWords`/`chineseWords` lists.
  In `LevelPanelController.ShowWordPreview()`: same filter on the display list.
  **Verify:** Build a word list with a 50-char CN word → reload → confirm it's absent from phase list and preview.

- [ ] **Step 2: Add `SetFixedFontSizes` to `TargetCell.cs`**
  Add method that takes `(float charSize, float pinyinSize)`, disables `enableAutoSizing` on both labels, sets `fontSize` to the passed values.
  **Verify:** Unity compiles without errors. Method exists and is callable.

- [ ] **Step 3: Add `SetFixedFontSize` to `EnglishCell.cs`**
  Add method that takes `float size`, disables `enableAutoSizing` on the label, sets `fontSize`.
  **Verify:** Unity compiles without errors. Method exists and is callable.

- [ ] **Step 4: Add serialized layout fields to `ChineseTargetDisplay.cs`**
  Add fields: `englishFontRatio` (0.8), `charRatio` (0.6), `pinyinRatio` (0.3), `horizontalSpacing` (10), `verticalSpacing` (15).
  **Verify:** Fields appear in Inspector after recompile.

- [ ] **Step 5: Rewrite `PlayEntryAnimation()` — implement manual row layout**
  Disable GridLayoutGroup. Collect all children (TargetCell + EnglishCell) in order. Implement the row count decision algorithm (1 row → try, 2 rows → try at 75%, 3 rows at 50%). Measure English widths via `GetPreferredValues`. Apply uniform font sizes. Position manually via `anchoredPosition`. Maintain same easeOutBack animation. Include English cells in the animation sequence with landing sound.
  **Verify:** Unity compiles. Enter play mode with a mixed word list → observe cells laid out in rows, not in a 16-col grid.

- [ ] **Step 6: Update `AnimateCellsIn()` to include English cells**
  Animate `cellContainer` children (both Chinese and English cells) sequentially from scale 0→1 with easeOutBack. Play landing sound on each item.
  **Verify:** Both Chinese and English cells animate in with bounce effect and sound during mixed phase.

- [ ] **Step 7: Clean up obsolete sync methods**
  In `ChineseDisplayController.RebuildForMixed()`: remove `SyncFontSizesNextFrame()` call. In `ChineseTargetDisplay.cs`: the empty `PrepareEntryAnimation()`, `SyncPinyinFontSize()`, `SyncEnglishFontSize()`, and `SyncFontSizesCoroutine()` can be kept as no-ops or removed.
  **Verify:** Unity compiles. Mixed phase display still works without those calls.

- [ ] **Step 8: Commit incremental progress**
  Commit each prefab/.cs change with descriptive messages. At minimum: after Step 4 (fields added), after Step 5 (layout working), after Step 7 (cleanup done).
  **Verify:** `git log` shows commits with clear messages.

- [ ] **Step 9: Final integration verification**
  Run through the full verification checklist from the spec:
  1. Mixed "你好world" → 1 row, CN square, EN at natural width, font 0.8×
  2. Pure CN, 20 chars → 2 rows at 75%
  3. Mixed with long English word → row scales to fit viewport
  4. Enter animation → all cells animate (CN + EN) with sound
  5. Font sizes identical across all same-type cells
  6. Over-long word (49 char-units CN) → filtered from gameplay entirely
  **Verify:** All 6 checks pass. Cross off final step.
