---
name: generate-levels
description: Generate word/phrase list levels for the Words typing game, with theme, difficulty, and hand-balance validation.
---

# Words Level Generator

Generate themed word-list levels for the Words typing game. Handles difficulty (peak hold), left/right hand balance, Chinese pinyin, and escalation patterns.

## Workflow

1. **Ask the user for parameters**: difficulty range, theme, phase count, escalation preference
2. **Generate content** following the design rules below
3. **Run the validator** to check metrics
4. **Iterate** if validator flags issues
5. **Present** to the user for review/playtest

## Difficulty Chart

Difficulty = **Peak Hold** (max simultaneously-held keys during a phase).

| Difficulty  | Peak Hold | Playtest Reference |
|-------------|-----------|-------------------|
| Tutorial    | 3–4       | |
| Easy        | 4–5       | |
| Easy+       | 5–6       | |
| Normal      | 6–7       | |
| Normal+     | 7–8       | Melbourne (5-10% stuck here) |
| Hard        | 8–9       | |
| Very Hard   | 9–10      | No Food and Drinks |
| Expert      | 11–12     | ...in the lab (excellent players may/may not beat it) |
| Challenge   | 13+       | Last phase of test list (impossible solo) |

The user specifies a range (e.g. `5-9`, `8-12`, `10+`). Generate each phase's peak hold within that bound. The ramp across phases should generally increase, with occasional plateaus.

## Content Design Rules

### Phase Structure
- 4–8 phases per level (default 6)
- Ramp generally increases peak hold by +1 to +3 per phase
- Can have flat phases (recovery) between climbs
- Last phase should feel "barely possible" at the top of the range
- Mix short and long phrases within the range for rhythm variety

### Surprise Escalation (optional)
The "No Food" pattern — 2–3 consecutive phases sharing a root phrase that grows longer:

```
Phase N:   "[root]"                   → peak hold 4-6
Phase N+1: "[root] and [add-on]"      → peak hold 7-9
Phase N+2: "[root] and [add-on] and more" → peak hold 10+
```

The escalation is a surprise for the player who recognizes the pattern. Only use on ~1 out of every 3 levels — not every level needs it.

### Hand Balance
- **Easy (peak 3-5)**: no constraint
- **Normal (peak 6-8)**: target 35-65% per hand (balance matters most here)
- **Hard (peak 9+)**: ignore balance, hold count IS the difficulty

QWERTY assignments:
| Hand | Keys |
|------|------|
| Left | Q, W, E, R, T, A, S, D, F, G, Z, X, C, V, B |
| Right | Y, U, I, O, P, H, J, K, L, N, M |

Long runs of 6+ consecutive same-hand presses in a phase get flagged.

### Letter Economy
- Prefer letters that repeat naturally (e, t, a, o, i, n, s, h, r) — they create hold/release patterns
- A letter appearing 3 times produces HOLD-RELEASE-HOLD (harder)
- A letter appearing 2 times produces HOLD-RELEASE
- A letter appearing 1 time produces HOLD (adds to concurrent hold count without release)
- For high peak hold (10+), use multiple letters appearing 2-3 times to keep keys held
- Avoid rare letters in isolation (q, z, x, j) — they're single-hold clutter

### Chinese Content
- Chinese characters are automatically converted to pinyin (tone-stripped) by the game
- Pinyin syllables concatenate without spaces: `你好世界` → `nihaoshijie`
- The validator handles this via pypinyin — always run it for Chinese content
- Mixed Chinese+English works: `混合English` → `hunheEnglish`
- Tone marks stripped: `mā` → `ma`, `shì` → `shi`

## Theme Catalog

| Category | Examples | Notes |
|----------|----------|-------|
| Memes | "skrrrt", "This is fine", "Why so serious?", "It's free real estate" | Recognizable, fun. Short phrases work well for early phases |
| Movie/TV | "Winter is coming", "I'll be back", "May the Force be with you", "One does not simply", "To infinity and beyond" | Natural multi-word phrases. Check peak hold carefully — "Winter is coming" peaks at ~10 |
| Song Lyrics | "Never gonna give you up", "We will rock you", "Hello from the other side", "Don't stop believing" | Built-in rhythm. Often peak hold 7-12 |
| Anime | "Believe it", "Bankai", "Plus Ultra", "I'm going to be the king of pirates", "Eren Jaeger" | Niche audience, great variety |
| Gaming | "It's dangerous to go alone", "The cake is a lie", "Hadouken", "GG WP", "Would you kindly" | Core audience connection |
| Progressive Story | "Wake up" → "Wake up and fight" → "Wake up and fight the system" | Builds tension. Escalation pattern |
| Food | "Spaghetti", "Pineapple on pizza", "Extra spicy ramen", "Bubble tea" | Relatable, comedy potential |
| Nonsense/Tongue Twisters | "She sells seashells", "Unique New York", "Irish wristwatch", "Red lorry yellow lorry" | Letter rep patterns = natural difficulty |
| Sci-fi/Fantasy | "Live long and prosper", "So say we all", "The answer is 42", "Make it so" | Multi-word, letter variety |
| Philosophy | "I think therefore I am", "The only constant is change", "Know thyself" | Meaningful + good letter patterns |
| Programming | "Hello World", "Segmentation fault", "404 not found", "git push force" | Dev in-jokes |
| Shakespeare | "To be or not to be", "All that glitters", "The lady doth protest" | Public domain, varied |
| Chinese Idioms | 一心一意, 心想事成, 万事如意 | Short pinyin strings, moderate difficulty |
| Chinese Memes | 打工人打工魂, 我太难了, 卷起来 | Modern Chinese internet culture |

## Validator

Always run after generating. Located at `Tools/validate_level.py`:

```bash
python Tools/validate_level.py <output_file> --range <min-max> [-v]
```

Example:
```bash
python Tools/validate_level.py Assets/StreamingAssets/Levels/my_level.txt --range 5-10 -v
```

The validator simulates the game's WordEngine and reports:
- Peak hold per phase → FAIL if above max, WARN if below min
- Hand balance per phase → WARN if outside tolerance for the difficulty band
- Same-hand runs → INFO for 6+ consecutive same-hand
- Escalation patterns → detected automatically
- Chinese text → auto-converts to pinyin (requires pypinyin: `pip install pypinyin`)

## Output Format

Write to `Assets/StreamingAssets/Levels/<name>.txt`:

```json
{
  "name": "Level Title",
  "nameZh": "中文标题",
  "words": [
    "phase one phrase",
    "second phase is harder",
    "third phase builds on it"
  ]
}
```

If no Chinese name is needed, omit `nameZh`.

## Generation Rules

1. Always validate after generation
2. If validation fails, adjust and re-validate until clean
3. Present clean results to user with the full validator output
4. Ask for playtest feedback after user approval
5. Save user feedback to memory for future generation improvements
