# Minimal adaptive Prime/Sub on native Kilo

This package adds only what native Kilo does not provide:

- global custom primary `prime`;
- global custom subagent `sub`;
- one compact project-local orchestration file: `.prime/state.json`, created lazily by Prime when first run in that project.

Native Kilo owns Task sessions/isolation/resume/background execution, tool execution, permission evaluation, model calls, context compaction, project memory, and built-in agents. Compaction/memory may supply context, but the exact orchestration checkpoint remains `.prime/state.json` so restart/compaction cannot depend on reconstructing lossy or recall-oriented context. There is no custom scheduler, task runner, retry engine, session store, worktree manager, or agent registry.

## Setup

Prerequisites: native Kilo already installed/configured and provider `9router/sub` available.

On Windows, extract this package anywhere and double-click `setup.cmd`. The installer does **not** need to be inside a Git repository and does not need a project path. It installs/verifies the two global agents only. Existing global `prime.md`/`sub.md` are backed up before replacement.

Setup never edits Kilo configuration or built-in agents. If verification fails, follow `CONFIG-GUIDE.md`, then run setup again.

After setup, open a terminal **inside the target project** and run Prime, for example:

```powershell
kilo run --auto --agent prime "<objective>"
```

On its first run in that project, Prime creates `.prime/state.json` if absent. If the file already exists, Prime preserves and reconciles it instead of resetting it.

## Minimal state

Only `.prime/state.json` is canonical orchestration state. Its initial shape is:

```json
{
  "objective": {"revision": 0, "text": ""},
  "tasks": {},
  "active": null,
  "status": null,
  "next": "capture objective",
  "git": null,
  "evidence": {}
}
```

A live active contract contains only:

```json
{
  "id": "C...",
  "revision": 1,
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

Each task record needs only its status, dependency IDs, and any stable refs needed to resume or invalidate the affected dependency closure. Evidence entries are references plus the minimum validity anchors needed to know whether they still apply; never copy logs into state.

Every contract change increments `revision` (or creates a new contract ID). A Sub result is current only when `(contract id, contract revision, objective revision)` still match.

Do not persist difficulty scores, transcripts, attempt counters, copied logs, or a second task/session database.

## Runtime flow

- EASY: bounded Sub contract -> Sub executes -> reuse strong deterministic evidence -> Prime accepts.
- MEDIUM: short Prime framing of decisive constraints -> Sub owns execution loop -> Prime reviews only non-mechanical correctness.
- HARD: Prime leads reasoning/direct execution before predictable Sub retries -> hand mechanical remainder back to Sub when bounded.
- Recovery: every `FAILED_TECHNICAL` returns to Prime; change strategy/responsibility based on evidence until PASS or true `BLOCKED_EXTERNAL`.

Sub `DONE` is never canonical PASS. Prime accepts with the cheapest sufficient still-valid evidence and never reruns checks merely for ceremony.
