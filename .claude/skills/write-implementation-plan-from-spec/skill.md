---
name: write-implementation-plan-from-spec
description: Use when a spec document exists and you need to write a step-by-step implementation plan with verifiable checkpoints before coding
---

# Write Implementation Plan From Spec

Converts a spec document into an actionable, step-by-step implementation plan where every step has a clear verification action and can be crossed off once confirmed.

## Format

Each plan uses this template:

```markdown
# Implementation: [Feature Name]

Spec: `docs/spec-name.md`

## Steps

- [ ] **Step 1: [action]**
  What to do. What files to change. What the change looks like.
  **Verify:** [exact command or action to confirm this step worked — compile, test, inspect, etc.]

- [ ] **Step 2: [action]**
  ...
```

### Rules

1. **Every step has a verification action in bold.** If you can't verify it independently, it's not a step — split it.
2. **Steps build on each other.** Step N must be verifiable before Step N+1 starts.
3. **Include at least one build/compile verification** early in the plan.
4. **The final step is always an integration verification** that exercises the full spec requirement end-to-end.
5. **Cross off steps with `[x]`** only after the verification passes. If verification fails, the step is not done.
6. **When verification fails:** diagnose and fix before proceeding. Do not skip to the next step.
7. **For Unity projects:** commit after every step that modifies a `.prefab` or `.unity` file.

### Verification types by category

| Category | Good verification | Bad verification |
|---|---|---|
| **Compile** | `dotnet build` / Unity compile check | "Code compiles" (who checked?) |
| **Behavior** | "Enter word X → observe cell size = Y" | "Layout looks right" |
| **Edge case** | "Feed 50-char CN word → verify filtered from word list" | "Trimming works" |
| **Data** | "Inspect generated file at path → verify field Z = expected" | "Data is correct" |
| **Visual** | Screenshot capture + describe expected visual state | "Looks good in editor" |
