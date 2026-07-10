# Community Features Build Plan

**Date:** 2026-07-10
**Status:** Post-launch (Steam initial launch first)
**Trigger:** Design discussion about username, community word lists, leaderboards

---

## Guiding Decisions

| Decision | Choice | Why |
|----------|--------|-----|
| **Backend** | Supabase | Unifies Steam + itch + standalone builds under one DB. Free tier handles launch. |
| **Username** | PlayerPrefs first, Steam persona later | Simple, works everywhere. When Steam integrates, Steam name becomes default with in-game override. |
| **Community lists** | Supabase DB table (not Steam Workshop) | Workshop is Steam-only. Supabase means one system for all stores. Workshop is overkill for tiny JSON word lists. |
| **Leaderboard** | Supabase table (not Steam leaderboards) | Same reason — unified across stores. |

---

## Architecture Overview

```
Game Client (any build)
        │
        ├── POST /rest/v1/leaderboard ← score submission
        ├── GET  /rest/v1/leaderboard ← fetch rankings
        ├── POST /rest/v1/word_lists  ← upload a list
        ├── GET  /rest/v1/word_lists  ← browse/download lists
        │
        ▼
   Supabase Project
        ├── anon auth (per-install JWT)
        ├── RLS policies (server-side validation)
        ├── word_lists table
        ├── leaderboard_entries table
        └── ghost_data (future)
```

---

## Tables & Schema

### `leaderboard_entries`

```sql
CREATE TABLE leaderboard_entries (
  id          BIGSERIAL PRIMARY KEY,
  player_name TEXT NOT NULL CHECK (char_length(player_name) BETWEEN 2 AND 20),
  word_list   TEXT NOT NULL,          -- name of the list played
  total_time  REAL NOT NULL,          -- seconds
  phase_count INT NOT NULL,
  created_at  TIMESTAMPTZ DEFAULT now()
);

-- Anti-cheat: server-enforced validation
-- Reject impossible runs: each phase minimum ~4s even for fast typists
ALTER TABLE leaderboard_entries ADD CONSTRAINT valid_score
  CHECK (total_time >= phase_count * 4.0 AND total_time <= 3600 AND phase_count >= 1);
```

### `word_lists`

```sql
CREATE TABLE word_lists (
  id            BIGSERIAL PRIMARY KEY,
  author        TEXT NOT NULL CHECK (char_length(author) BETWEEN 2 AND 20),
  title         TEXT NOT NULL CHECK (char_length(title) BETWEEN 1 AND 100),
  phases        JSONB NOT NULL,       -- array of phase objects matching WordListData schema
  downloads     INT DEFAULT 0,
  created_at    TIMESTAMPTZ DEFAULT now(),
  reported      INT DEFAULT 0,        -- flag count
  hidden        BOOL DEFAULT false    -- auto-hidden after N reports
);

-- Index for browsing
CREATE INDEX idx_word_lists_created ON word_lists(created_at DESC);
```

### `ghost_data` (future)

```sql
CREATE TABLE ghost_data (
  id            BIGSERIAL PRIMARY KEY,
  leaderboard_id BIGINT REFERENCES leaderboard_entries(id),
  word_list     TEXT NOT NULL,
  keystrokes    JSONB NOT NULL,       -- [{key, action:"press|release", timestamp_ms}, ...]
  compressed    BOOL DEFAULT true
);
```

---

## Anti-Cheat / Safety (MVP)

| Measure | Implementation | Complexity |
|---------|---------------|------------|
| **Score validation** | `CHECK (total_time >= phase_count * 4.0)` — SQL constraint, bypassable only by DB admin | 1 line |
| **Spam limit (uploads)** | Rate limit in Supabase dashboard: 3 uploads/hr per IP/user | Click once |
| **Spam limit (leaderboard)** | Rate limit: 1 submission per 30s per user | Click once |
| **Name blocklist** | Server-side check on upload: reject offensive content. ~50 word list. | Low |
| **Report button** | Increment `reported` column. Auto-hide after N flags. | Low |

## Anti-Cheat (Post-MVP)

| Measure | Detail |
|---------|--------|
| **Ghost replay verification** | On suspicious score, compare keystroke timestamps against word list. Inhuman gaps → reject. Also a *feature*: players can watch ghost replays. |
| **Voting / reputation** | Upvote/downvote lists. High-rep authors visible. Auto-hide low-rated. |
| **LLM content check** | Call LLM API on upload to catch offensive content that blocklist misses. |
| **Score anomaly detection** | Flag runs where average press speed is statistically impossible for a human. |

---

## What Goes in the Initial Launch (No Community)

The first Steam launch ships with **zero online features** beyond what you already have:

- All leaderboard calls go to `NullLeaderboardService` (already exists) — no-op
- Word lists are local only (FixedWordListProvider, FileWordListProvider, DailyWordListProvider — all exist)
- No username screen needed
- No internet connection required

This keeps the launch scope small. Community features are a **post-launch update** that adds an online layer behind the same `ILeaderboardService` and `IWordListProvider` interfaces.

---

## Suggested Development Order (Post-Launch)

```
Phase 1: Backend Foundation
  1. Spin up Supabase project
  2. Create tables + RLS policies
  3. Write SupabaseService C# class (wraps REST calls)
  4. Swap NullLeaderboardService → SupabaseLeaderboardService

Phase 2: Username System
  1. TMP_InputField panel on first launch
  2. Save to PlayerPrefs via SettingsManager
  3. Profanity blocklist on client + server

Phase 3: Leaderboards
  1. Leaderboard panel in game (scrollable list)
  2. Submit on PhaseComplete/AllComplete
  3. Rate limiting

Phase 4: Community Word Lists
  1. "Upload" button on My List tab
  2. Browse/download panel
  3. Report button + auto-hide
  4. Search/sort by newest, popular, author

Phase 5: Advanced
  1. Ghost replay capture + playback
  2. Voting / reputation system
  3. LLM content moderation
```

---

## Key Files This Will Touch (When Built)

| File | Role |
|------|------|
| `Assets/-Scripts/Leaderboard/ILeaderboardService.cs` | Interface exists. Will get real implementation. |
| `Assets/-Scripts/Leaderboard/NullLeaderboardService.cs` | Stub for offline/launch. |
| `Assets/-Scripts/Leaderboard/SupabaseLeaderboardService.cs` | New — real implementation. |
| `Assets/-Scripts/UI/SupabaseService.cs` | New — shared Supabase REST client. |
| `Assets/-Scripts/UI/CommunityPanelController.cs` | New — browse/upload/download UI. |
| `Assets/-Scripts/Core/SettingsManager.cs` | Add `PlayerName` key. |
| `Assets/-Scripts/WordList/` | New `IWordListProvider` for community lists. |

**Cleanup:** 🧹 Delete this file once community features are fully implemented.
