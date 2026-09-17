# Prime/Sub minimal native-Kilo setup

`install.ps1` installs only the two global custom agent files and bootstraps `.prime/state.json` in the current Git root (or current directory when not in Git). It does not edit `kilo.json` / `kilo.jsonc` and does not install, upgrade, or replace Kilo.

## Required external Kilo configuration

Patch the existing config; do not replace it. Preserve unrelated providers, plugins, MCP settings, permissions, and experimental properties.

Required resolved behavior:

1. `default_agent` is `prime`.
2. `experimental.task_model_selection` is `true` so Prime can explicitly select `9router/sub` on every native Task call. This is required because current Kilo CLI can persist a per-agent model choice that otherwise takes precedence over the agent frontmatter model pin.
3. Provider/model `9router/sub` is available. Preserve your existing `9router` provider definition; add only the missing model entry according to that provider's existing schema if necessary.
4. Do not define inline `agent.prime` or `agent.sub` unless intentionally overriding the installed Markdown files.
5. Do not disable or replace built-in agents. They are untouched and not required by this architecture.
6. Do not set a global `subagent_model`; Sub owns its model, and Prime also selects it explicitly per Task call.
7. No worktree setting is required or used.

Minimum relevant config patch when `9router/sub` is already available:

```jsonc
{
  "default_agent": "prime",
  "experimental": {
    // preserve every other existing experimental property
    "task_model_selection": true
  }
}
```

## Verification

Run from the target project after applying any needed config patch:

```powershell
kilo debug config
kilo debug agent prime
kilo debug agent sub
```

Resolved requirements:

- config: `default_agent == "prime"`;
- config: `experimental.task_model_selection == true`;
- Prime: `mode == "primary"` and Task tool enabled;
- Sub: `mode == "subagent"`;
- Sub: `model.providerID == "9router"` and `model.modelID == "sub"`;
- Sub: Task tool disabled.

Project agent files can override the global install. If effective output differs, reconcile the project override rather than reinstalling built-in agents or rewriting the whole config.

## Runtime rule

Every Prime -> Sub native Task invocation must explicitly request model `9router/sub`. The frontmatter pin is the default; explicit Task selection is the runtime guard against persisted CLI per-agent model choices. Prime should treat a model mismatch as stale/misconfigured execution and reconcile it before accepting or delegating further work.

Use `kilo run --auto` for fully prompt-free CLI runs. Agent permissions are also broad-allow except for the topology constraints: Prime may Task only `sub`, and Sub may Task nobody. Sensitive-file reads are explicitly allowed in the agent definitions so normal operation does not depend on interactive approval prompts.

Native Task sessions provide isolation. Reuse `task_id` only for interrupted unchanged work under the same parent session; use a fresh Sub after restart/material contract change. Native background Sub tasks may be used for coordination-safe parallel scopes when the Kilo background-subagent feature is enabled; no custom scheduler is added.
