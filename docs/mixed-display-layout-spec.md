# Mixed/Chinese Target Display — Manual Row Layout Spec

**Date:** 2026-07-18
**Context:** Replaces GridLayoutGroup-based band sizing in `ChineseTargetDisplay.PlayEntryAnimation()`. Triggered by the need to support mixed Chinese+English target displays with variable-width English cells.

---

## Problem

The existing `GridLayoutGroup` with `constraintCount = 16` forces uniform cell sizes. This doesn't work for mixed content:

| Cell type | Natural shape | Content |
|---|---|---|
| **TargetCell** (Chinese) | Square | One char + pinyin above |
| **EnglishCell** (English word) | Rectangle (wider than tall) | Multi-letter word |

A uniform grid either wastes space on short Chinese phrases or truncates English words.

---

## Solution: Manual Row Layout

Replace `GridLayoutGroup` with code-driven `RectTransform` positioning. The algorithm:

1. Collect all items (Chinese + English) in container child order
2. Determine optimal row count (1-3 rows) and cell size
3. Apply uniform font sizes to all cells (no auto-sizing)
4. Measure English text widths at the determined font size
5. Scale each row independently to fit viewport
6. Position items manually via `anchoredPosition`
7. Animate all items scale 0→1 with easeOutBack

### Row Count Decision

```
Try 1 row (max 16 items):
  naturalRowWidth = Σ(itemWidths) + (count-1) × horizontalSpacing
  rowScale = min(1, viewportWidth / naturalRowWidth)
  cellSize = prefabCellSize × rowScale
  if (cellSize < prefabCellSize × 0.75 && count > 1) → try 2 rows

Try 2 rows at 75% base:
  baseSize = prefabCellSize × 0.75
  split items into 2 groups for balanced natural width
  per-row: naturalRowWidth → rowScale
  effectiveMin = baseSize × min(rowScale_0, rowScale_1)
  if (effectiveMin < prefabCellSize × 0.5 && count > 2) → try 3 rows

3 rows at 50% base:
  baseSize = prefabCellSize × 0.5
  split items into 3 groups
  per-row: naturalRowWidth → rowScale (no further downgrade)
```

### Font Sizing (uniform across all cells, auto-sizing OFF)

| Variable | Default | Applies to |
|---|---|---|
| `charRatio` | 0.6 | `TargetCell.charLabel.fontSize = cellSize × charRatio` |
| `pinyinRatio` | 0.3 | `TargetCell.pinyinLabel.fontSize = cellSize × pinyinRatio` |
| `englishFontRatio` | 0.8 | `EnglishCell.label.fontSize = cellSize × englishFontRatio` |

All cells of the same type get **identical** font sizes every render — no per-cell variation.

### Spacing Controls

| Variable | Default | Purpose |
|---|---|---|
| `horizontalSpacing` | 10 | Gap between items in the same row |
| `verticalSpacing` | 15 | Gap between rows |

### English Width Measurement

After setting `fontSize = cellSize × englishFontRatio` and disabling auto-sizing, measure the actual text extent:

```
label.ForceMeshUpdate()
width = label.GetPreferredValues(label.text, float.PositiveInfinity, float.PositiveInfinity).x
```

This measurement happens on the actual `EnglishCell` instances already created by `BuildMixedCells()`. The measured width includes the font's natural character advance (no wrapping — single line).

### Per-Row Scaling

Each row starts at `baseSize` (which is `prefabCellSize × rowBand`). If the row's natural width exceeds the viewport, the entire row scales down:

```
rowScale = min(1, (viewportWidth - (cols-1)×hSpacing) / naturalRowWidth)
cellSize = baseSize × rowScale
charFontSize = cellSize × charRatio       // re-applied after scaling
enFontSize   = cellSize × englishFontRatio // re-applied after scaling
```

Chinese cells remain square (`cellSize × cellSize`).  
English cells get height `cellSize` and width `measuredTextWidth × rowScale`.

### Landing Sound

Same per-item landing sound for both Chinese and English cells, playing on each cell's animation pop-in.

---

## Files Changed

| File | Change |
|---|---|
| `ChineseTargetDisplay.cs` | Rewrite `PlayEntryAnimation()`. Add serialized fields. Update `AnimateCellsIn()` to handle all children. Remove GridLayoutGroup dependency. |
| `TargetCell.cs` | Add `SetFixedFontSizes(float charSize, float pinyinSize)` — disables auto-sizing, sets uniform sizes. |
| `EnglishCell.cs` | Add `SetFixedFontSize(float size)` — disables auto-sizing, sets uniform size. |
| `ChineseDisplayController.cs` | Remove `SyncFontSizesNextFrame()` call from `RebuildForMixed()` — no longer needed. |

### What's Removed

- `GridLayoutGroup.constraintCount = 16` — grid is disabled, layout is manual
- `SyncEnglishFontSize()` — English font and width set during layout
- `SyncFontSizesNextFrame()` / `SyncFontSizesCoroutine()` — obsolete
- `SyncPinyinFontSize()` — pinyin size set uniformly during layout

### What Stays the Same

- `BuildCells()` / `BuildMixedCells()` — still instantiate and collect cells
- `Clear()` — still destroys children
- `TargetCell` and `EnglishCell` prefab structures — unchanged

---

## Edge Cases

| Case | Behavior |
|---|---|
| Pure Chinese (BuildCells) | Same layout path, items are all square. Row count logic applies equally. |
| 1 item | Always 1 row, cellSize = min(prefabSize, viewportClamp) |
| Mixed with empty English string | `GetPreferredValues` returns near-zero width, cell gets minimum `cellSize × 0.5` width |
| Viewport narrower than single cell | Clamped to minimum cell size (no explicit floor — natural geometry handles it) |
| Row count ambiguity (borderline 0.75) | Prefer fewer rows (1 row at 74% is kept as 1 row; threshold is strict `< 0.75`) |

---

## Verification

1. Build a mixed list with "你好world" — verify 1 row, Chinese cells square, English word at natural width with 0.8× font size
2. Build a pure Chinese list with 20 characters — verify 2 rows at 75%
3. Build a mixed list where the English word is very long — verify row scales to fit viewport
4. Build a list at exactly the 0.75 boundary — verify row count rounds correctly
5. Play the entry animation — verify all cells (CN + EN) animate in sequentially with landing sounds
6. Compare char font sizes across all cells in the same layout — verify they are identical
