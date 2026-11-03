# Level Panel — Implementation Checklist

**Date:** 2026-07-06
**Branch:** `feat/levelsPanel`

## Scene Hierarchy

```
Level (root)
├── Dim
├── Card (VerticalLayoutGroup)
│   ├── Title (LocalizeText)
│   ├── TabRows
│   │   ├── TabBtn1 → Challenges
│   │   ├── TabBtn1 (1) → Custom
│   │   └── TabBtn1 (2) → Trendy
│   ├── LevelContent
│   │   ├── LevelGrid (4×5 scrollable grid)
│   │   └── WordList (word preview)
│   └── ButtonRow
│       ├── CancelBtn | OKBtn | ImportBtn | ExportBtn | CreateListBtn | DeleteListBtn
```

## Steps

- [x] **Step 1:** Create `StreamingAssets/Levels/` + 20 challenge `.txt` files
- [x] **Step 2:** Create `LevelWordListProvider.cs` — txt-backed IWordListProvider
- [x] **Step 3:** Create `LevelPanelController.cs` — drives all tabs + buttons
- [x] **Step 4:** Wire scene — assign script, wire all references, set Level inactive
      - [x] Assign LevelPanelController to Level root
      - [x] Wire tab buttons, grid (LevelGridContent), word preview (WordListContent)
      - [x] Wire all 6 button row buttons (Cancel, OK, Import, Export, CreateList, DeleteList)
      - [x] Create and wire Trendy placeholder ("Coming Soon")
      - [x] Level starts inactive (IsActive: false)
      - [x] Add PlayBtn to HUD-Btns with onClick → UIController.OnPlayBtnClicked
      - [x] Add `levelPanel` serialized ref + `OnPlayBtnClicked()` to UIController
      - [x] Remove old ImportBtn/ExportBtn from WordListPanel MyListPanelBtns
      - [x] Clear importBtn/exportBtn serialized refs on UIController
      - [x] WordListPanel kept as-is for per-phase editing (Add/Del/Up/Down/Swap/InputField)
- [ ] **Step 5:** Post-merge cleanup
      - [ ] Remove `ChinesePinyinPopup` from Level root (⚠️ currently conflicts with OK/Cancel button listeners)
      - [ ] Add localization keys for Level panel UI elements
      - [ ] Tweak level grid cell sizes and spacing if layout needs adjustment

## Verification (user to test in Editor)

1. [ ] Compile — no errors
2. [ ] Click PlayBtn → Level panel opens showing Challenges tab
3. [ ] Challenges tab shows 20 tiles in 4×5 grid
4. [ ] Click a challenge tile → WordList shows that level's words
5. [ ] Click OK → game starts playing that level
6. [ ] Click Cancel → panel closes
7. [ ] Custom tab → import/export/create/delete buttons visible
8. [ ] Custom tab → create a new list, see it in grid, select, OK to play
9. [ ] Trendy tab → shows "Coming Soon" placeholder
10. [ ] WordListPanel still works for per-phase editing
