# Minimal adaptive Prime/Sub on native Kilo

This package adds only what native Kilo does not provide:

- global custom primary `prime`;
- global custom subagent `sub`;
- one compact project-local orchestration file: `.prime/state.json`.

Native Kilo owns Task sessions/isolation/resume/background execution, tool execution, permission evaluation, model calls, and built-in agents. There is no custom scheduler, task runner, retry engine, session store, worktree manager, or agent registry.

## Setup

Prerequisites: native Kilo already installed/configured and provider `9router/sub` available.

On Windows, place this package anywhere inside the target Git project and double-click `setup.cmd`. It detects the Git root; use `setup.cmd -ProjectRoot "C:\path\to\project"` to override. Existing `.prime/state.json` is preserved. Existing global `prime.md`/`sub.md` are backed up before replacement.

Setup never edits Kilo configuration or built-in agents. If verification fails, follow `CONFIG-GUIDE.md`, then run setup again.

## Minimal state

Only `.prime/state.json` is canonical orchestration state. Top-level fields are intentionally limited to:

- `objective`: text + revision;
- `tasks`: sparse task status/dependencies;
- `active`: current contract or `null`;
- `status`: current acceptance/recovery status or `null`;
- `next`: next action;
- `git`: current branch/HEAD/relevant dirty relation;
- `evidence`: compact reusable evidence references.

A live active contract contains only:

```json
{
  "id": "C...",
  "objective_revision": 1,
  "task": "T...",
  "owned": ["..."],
  "excluded": ["..."],
  "invariants": ["..."],
  "accept": ["..."],
  "git": {"head": "...", "dirty": ["..."]},
  "evidence": ["E..."],
  "recovery": null
}
```

Do not persist difficulty scores, transcripts, attempt counters, copied logs, or a second task/session database.

## Runtime flow

- EASY: bounded Sub contract -> Sub executes -> reuse strong deterministic evidence -> Prime accepts.
- MEDIUM: short Prime framing of decisive constraints -> Sub owns execution loop -> Prime reviews only non-mechanical correctness.
- HARD: Prime leads reasoning/direct execution before predictable Sub retries -> hand mechanical remainder back to Sub when bounded.
- Recovery: every `FAILED_TECHNICAL` returns to Prime; change strategy/responsibility based on evidence until PASS or true `BLOCKED_EXTERNAL`.

Sub `DONE` is never canonical PASS. Prime accepts with the cheapest sufficient still-valid evidence and never reruns checks merely for ceremony.
