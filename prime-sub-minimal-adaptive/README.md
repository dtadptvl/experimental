# Minimal adaptive Prime/Sub on native Kilo

Adds only three architecture-specific pieces native Kilo does not supply as one policy:

- global custom primary `prime`;
- global custom subagent `sub` pinned/routed to `9router/sub`;
- one compact project-local canonical orchestration file, `.prime/state.json`, created lazily by Prime.

Native Kilo remains the runtime for Task sessions/isolation/resume/background delivery, permissions, model calls, context compaction, project memory, tool execution/timeouts, and built-in agents. The only timeout added by this package is a provider `chunkTimeout` safety net for mid-stream silence; Kilo already supplies the request/first-byte timeout. There is no custom scheduler, task runner, retry engine, session store, worktree manager, agent registry, governance linter, journal, or task-file database.

## Setup

Prerequisites: native Kilo installed/configured and provider `9router/sub` available.

On Windows, extract this package anywhere and double-click `setup.cmd`. It installs the two global agent Markdown files under Kilo's global config directory and verifies effective configuration. Existing global `prime.md`/`sub.md` at that install location are backed up before replacement.

Setup never edits `kilo.json` / `kilo.jsonc` or built-in agents. If verification fails, apply only the Human-owned changes in `CONFIG-GUIDE.md`, then run setup again.

Run autonomous work inside a target project with:

```powershell
kilo run --auto --agent prime "<objective>"
```

## Minimal canonical state

Initial `.prime/state.json`:

```json
{
  "objective": {"revision": 0, "text": ""},
  "tasks": {},
  "active": null,
  "acceptance": null,
  "next": "capture objective",
  "git": null,
  "evidence": []
}
```

A live contract contains only:

```json
{
  "id": "C1",
  "revision": 1,
  "objective_revision": 1,
  "task": "T1",
  "owned": ["..."],
  "excluded": ["..."],
  "invariants": ["..."],
  "accept": ["..."],
  "git": {"branch": "...", "head": "...", "dirty": ["..."]},
  "evidence": ["E1"],
  "recovery": null
}
```

Evidence entries are compact references plus validity facts, not copied logs. Recovery stores only the stable failure signature, disproven hypotheses/strategies, useful preserved partial work/evidence, current phase, and next action.

## Runtime flow

- EASY: bounded contract -> Sub execution -> reuse strong deterministic evidence -> cheapest sufficient Prime acceptance.
- MEDIUM: short Prime framing of decisive constraints -> Sub owns inspect/edit/test/fix -> Prime reviews only non-mechanical correctness.
- HARD: Prime leads hard reasoning/direct execution before predictable Sub retries -> mechanical remainder returns to Sub when bounded.
- Recovery: `FAILED_TECHNICAL`, step/doom-loop exhaustion, Task error, or timeout returns to Prime; Prime changes strategy/responsibility until PASS or true `BLOCKED_EXTERNAL`.
- Parallel Sub work is opportunistic: use native background Task only when Kilo exposes it and scopes are coordination-safe; otherwise stay sequential rather than adding a scheduler.

Sub `DONE` is never canonical PASS. No fixed total recovery-attempt count exists.
