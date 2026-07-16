#!/usr/bin/env python3
"""
validate_level.py -- Validate word-list content for the Words typing game.

Usage:
    python validate_level.py <file.json_or_txt> [--range 3-8] [--verbose]

Parses a level file (JSON or plain-text), simulates the game's WordEngine to
compute difficulty metrics per phase, and validates against a difficulty range.

Chinese text is auto-converted to pinyin (tone-stripped) using pypinyin before
analysis -- mirrors the game's ChinesePhaseData pipeline.

Requires pypinyin for Chinese support:  pip install pypinyin
Without it, Chinese characters are skipped (game will still work, but validation
won't see their pinyin steps).

Exits 0 if all phases pass, 1 if any failure.
"""

import json
import sys
import argparse
import os
import re

# -- Optional: pypinyin for Chinese support ------------------------------------

try:
    import pypinyin
    HAS_PYPINYIN = True
except ImportError:
    HAS_PYPINYIN = False


def to_pinyin(text: str) -> str:
    """Convert Chinese chars to tone-stripped pinyin, leave ASCII unchanged.

    Matches the game's behavior: tone marks are stripped (ma1 -> ma, ma -> ma).
    Returns the full string with Chinese replaced by pinyin (no spaces between
    pinyin syllables unless whitespace was in the original).
    """
    if not HAS_PYPINYIN:
        # Without pypinyin, strip Chinese chars and warn
        ascii_only = ''.join(c for c in text if ord(c) < 128)
        if ascii_only != text:
            print(f"      [WARN] Chinese text detected but pypinyin not installed.", file=sys.stderr)
            print(f"             Install:  pip install pypinyin", file=sys.stderr)
        return ascii_only

    result = []
    for ch in text:
        if ord(ch) < 128:
            result.append(ch)  # ASCII pass-through
        else:
            # Get pinyin without tone marks (style=5 in newer pypinyin = TONE2_NUMBER,
            # then strip numbers; or use style=7 = BOPOMOFO_FIRST... no.
            # Style=3 (TONE2) gives "shi4" - strip trailing numbers.
            py = pypinyin.pinyin(ch, style=pypinyin.Style.TONE3)
            if py and py[0]:
                syllable = py[0][0]
                # Strip tone numbers: "shi4" -> "shi"
                syllable = re.sub(r'[0-9]$', '', syllable)
                result.append(syllable)
            else:
                result.append('')
    return ''.join(result)


def contains_chinese(text: str) -> bool:
    return any(ord(c) > 127 for c in text)


# -- QWERTY assignments (standard touch-typing) -------------------------------

LEFT_HAND  = set("QWERTASDFGZXCVB")
RIGHT_HAND = set("YUIOPHJKLNM")


def classify_hand(letter: str) -> str | None:
    u = letter.upper()
    if u in LEFT_HAND:
        return 'L'
    if u in RIGHT_HAND:
        return 'R'
    return None


# -- Core metric: simulate WordEngine -----------------------------------------

def phase_metrics(phrase: str) -> dict:
    """
    Simulate WordEngine step generation for one phrase.
    Chinese characters are converted to pinyin first.
    """
    # Convert Chinese to pinyin
    if contains_chinese(phrase):
        original = phrase
        phrase = to_pinyin(phrase)
        is_chinese = True
    else:
        original = phrase
        is_chinese = False

    occ: dict[str, int] = {}
    held: set[str] = set()
    peak = 0
    held_at_peak: set[str] = set()
    hand_actions: list[str] = []
    step_count = 0

    for ch in phrase:
        if not ch.isalnum():
            continue
        letter = ch.upper()
        occ[letter] = occ.get(letter, 0) + 1
        c = occ[letter]
        step_count += 1

        if c % 2 == 1:      # Hold
            held.add(letter)
            h = classify_hand(letter)
            if h:
                hand_actions.append(h)
        else:               # Release
            held.discard(letter)

        if len(held) > peak:
            peak = len(held)
            held_at_peak = set(held)

    reps = {l: c for l, c in occ.items() if c >= 2}

    return {
        "peak_hold": peak,
        "held_at_peak": held_at_peak,
        "hand_actions": "".join(hand_actions),
        "repetitions": reps,
        "step_count": step_count,
        "is_chinese": is_chinese,
        "pinyin": phrase if is_chinese else None,
    }


def longest_hand_run(actions: str) -> int:
    if not actions:
        return 0
    best = 1
    cur = 1
    for i in range(1, len(actions)):
        if actions[i] == actions[i - 1]:
            cur += 1
            best = max(best, cur)
        else:
            cur = 1
    return best


def hand_split(actions: str) -> tuple[float, float]:
    n = len(actions)
    if n == 0:
        return (0, 0)
    l = n - actions.count("R")
    r = actions.count("R")
    return (round(l / n * 100, 1), round(r / n * 100, 1))


# -- Difficulty config ---------------------------------------------------------

# (name, max_peak, hand_tolerance) in REVERSE order for band matching
DIFFICULTY_CONFIG = [
    ("challenge",  999, None),
    ("expert",      12, None),
    ("very-hard",   10, None),
    ("hard",         9, None),
    ("normal+",      8, (35, 65)),
    ("normal",       7, (30, 70)),
    ("easy+",        6, None),
    ("easy",         5, None),
    ("tutorial",     4, None),
]

def resolve_band(peak: int) -> str:
    for name, max_peak, _ in DIFFICULTY_CONFIG:
        if peak <= max_peak:
            return name
    return "challenge"

def get_config(band: str):
    for name, max_peak, tol in DIFFICULTY_CONFIG:
        if name == band:
            return max_peak, tol
    return 999, None


# -- Validation ---------------------------------------------------------------

def validate_phase(m: dict, min_peak: int, max_peak: int) -> list[str]:
    issues = []
    peak = m["peak_hold"]
    peak_band = resolve_band(peak)
    _, hand_tol = get_config(peak_band)

    if peak > max_peak:
        issues.append(f"FAIL  peak_hold={peak} exceeds max {max_peak}")
    if peak > 0 and peak < min_peak:
        issues.append(f"WARN  peak_hold={peak} is below min {min_peak}")

    if hand_tol is not None:
        l_pct, r_pct = hand_split(m["hand_actions"])
        if l_pct < hand_tol[0] or l_pct > hand_tol[1]:
            issues.append(
                f"WARN  hand L={l_pct}% R={r_pct}% (target {hand_tol[0]}-{hand_tol[1]}% L)"
            )

    run = longest_hand_run(m["hand_actions"])
    if run >= 6 and peak >= 5:
        issues.append(f"INFO  {run}-key same-hand run")

    return issues


# -- File loading -------------------------------------------------------------

def load_level(filepath: str) -> tuple[str, list[str]]:
    """Return (level_name, list_of_phrases)."""
    with open(filepath, "r", encoding="utf-8") as f:
        raw = f.read().strip()

    content = raw
    if content.startswith("//"):
        nl = content.find("\n")
        if nl >= 0:
            content = content[nl + 1:].strip()

    if content.startswith("{"):
        data = json.loads(content)
        return data.get("name", os.path.splitext(os.path.basename(filepath))[0]), \
               data.get("words", [])

    # Plain text: one phrase per line
    name = os.path.splitext(os.path.basename(filepath))[0]
    words = []
    for line in raw.splitlines():
        line = line.strip()
        if line and not line.startswith("//"):
            words.append(line)
    return name, words


# -- Output helpers -----------------------------------------------------------

def bar(pct: float, width: int = 16) -> str:
    l = round(pct / 100 * width)
    r = width - l
    return "[" + "#" * l + "." * r + "]"


# -- Main ---------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="Validate word-list level content")
    parser.add_argument("file", help="Path to level file (JSON or plain text)")
    parser.add_argument("--range", default=None, help="e.g. '3-8', '5-10', '10-13'")
    parser.add_argument("--verbose", "-v", action="store_true")
    args = parser.parse_args()

    if not os.path.isfile(args.file):
        print(f"Error: file not found: {args.file}")
        sys.exit(1)

    name, words = load_level(args.file)

    if not words:
        print("Error: no words/phrases found")
        sys.exit(1)

    # Resolve range
    low_peak = 0
    high_peak = 999
    max_band = "challenge"
    if args.range:
        parts = args.range.split("-")
        try:
            low_peak, high_peak = int(parts[0]), int(parts[1])
            for n, mp, _ in DIFFICULTY_CONFIG:
                if high_peak <= mp:
                    max_band = n
        except (ValueError, IndexError):
            pass

    print()
    print("=" * 65)
    print(f"  {name}")
    print(f"  Target: {args.range or 'unbounded'}  |  {len(words)} phases  |  "
          f"py={'yes' if HAS_PYPINYIN else 'no'}")
    print("=" * 65)
    print()

    results = []
    all_issues = []

    for i, phrase in enumerate(words):
        m = phase_metrics(phrase)
        issues = validate_phase(m, low_peak, high_peak)
        all_issues.extend(issues)

        l_pct, r_pct = hand_split(m["hand_actions"])
        peak_band = resolve_band(m["peak_hold"])
        reps_str = " ".join(f"{l}x{c}" for l, c in sorted(m["repetitions"].items()))
        run = longest_hand_run(m["hand_actions"])
        mark = "OK" if not issues else ("!!" if any("FAIL" in i for i in issues) else "..")

        display_text = phrase
        if m.get("is_chinese") and m.get("pinyin"):
            display_text = f"{phrase}  >>  {m['pinyin']}"

        print(f"  [{mark}] #{i+1}: {display_text}")
        print(f"        peak={m['peak_hold']} ({peak_band})  "
              f"steps={m['step_count']}  hand={l_pct}/{r_pct}  run={run}")
        if reps_str:
            print(f"        reps: {reps_str}")
        for iss in issues:
            print(f"        | {iss}")
        if args.verbose:
            print(f"        held: {','.join(sorted(m['held_at_peak']))}")
            print(f"        seq:  {m['hand_actions']}")
        print()

        results.append({
            "phase": i + 1,
            "text": phrase,
            "peak_hold": m["peak_hold"],
            "peak_band": peak_band,
            "step_count": m["step_count"],
            "hand_l": l_pct,
            "hand_r": r_pct,
            "hand_run": run,
            "repetitions": m["repetitions"],
            "issues": issues,
            "is_chinese": m.get("is_chinese", False),
        })

    # Level summary
    peaks = [r["peak_hold"] for r in results]
    print("--- Summary ---")
    print(f"  Peak holds: {peaks}")
    print(f"  Range:      {min(peaks)} .. {max(peaks)}")
    ramp = []
    for i in range(1, len(peaks)):
        d = peaks[i] - peaks[i - 1]
        ramp.append(f"{'+' if d >= 0 else ''}{d}")
    if ramp:
        print(f"  Ramp:       {' '.join(str(p) for p in peaks)}")
        print(f"  Deltas:     {' '.join(ramp)}")

    # Validate phase bounds
    out_of_bounds = [p for p in peaks if p < low_peak or p > high_peak]
    if out_of_bounds:
        print(f"  FAIL: phases outside {low_peak}-{high_peak}: {out_of_bounds}")

    # Escalation detection
    esc_groups = []
    for i in range(1, len(words)):
        pw = set(words[i - 1].lower().split())
        cw = set(words[i].lower().split())
        shared = pw & cw
        if shared and len(shared) >= len(pw) * 0.5:
            esc_groups.append((i, shared))
    if esc_groups:
        for idx, shared in esc_groups:
            print(f"  Escalate: phase {idx} -> {idx+1}  ({', '.join(sorted(shared))})")
    else:
        print(f"  No escalation")

    # Hand balance heatmap
    print()
    print("  Hand balance per phase:")
    for r in results:
        bar_str = bar(r["hand_l"])
        cn = " [CN]" if r.get("is_chinese") else ""
        print(f"    #{r['phase']:2d}  {bar_str}  L={r['hand_l']:5.1f}% R={r['hand_r']:5.1f}%{cn}")

    fails = [i for i in all_issues if i.startswith("FAIL")]
    warns = [i for i in all_issues if i.startswith("WARN")]

    print()
    print("--- Verdict ---")
    if fails or out_of_bounds:
        print(f"  XX  {len(fails)} failure(s), {len(warns)} warning(s)")
        sys.exit(1)
    elif warns:
        print(f"  ..  {len(warns)} warning(s), no failures -- review")
        sys.exit(0)
    else:
        print(f"  OK  All {len(words)} phases pass")
        sys.exit(0)


if __name__ == "__main__":
    main()
