# Prime/Sub 3-Tier — Non-Destructive Setup

Run:

```bat
setup.cmd
```

`setup.cmd` is only a small wrapper around the included `install.ps1`; no external dependency is required beyond Windows PowerShell. Set `PRIME_SUB_NO_PAUSE=1` when running non-interactively.

## What setup does

The installer is deliberately non-destructive.

- If `kilo` already exists in `PATH`, it keeps the installed Kilo version unchanged.
- If `kilo` is absent, it installs `@kilocode/cli@latest` with npm.
- It creates `%USERPROFILE%\.config\kilo\agent` only if needed.
- If global `prime.md` or `sub.md` already exists, it backs those files up under `%USERPROFILE%\.config\kilo\prime-sub-backups\<timestamp>-<random>\`.
- It then replaces only `%USERPROFILE%\.config\kilo\agent\prime.md` and `%USERPROFILE%\.config\kilo\agent\sub.md`.

It does **not** stop a running Kilo process, uninstall/upgrade an existing Kilo CLI, delete state, or rewrite unrelated configuration. In particular it leaves these untouched:

- `kilo.json` / `kilo.jsonc` and all unrelated config properties;
- global/project plugins and plugin configuration;
- built-in agents and other custom agents;
- auth/provider credentials;
- sessions, databases, prompt history, state and caches;
- user-level Kilo environment overrides;
- project `.kilo` files.

A currently running Kilo session may already have loaded the old agent prompt. Installing the files does not guarantee hot reload; start a new Kilo session when you want the new definitions to take effect.

## Configuration contract

Setup intentionally does not rewrite `kilo.jsonc`, because preserving arbitrary JSONC comments, plugins and unrelated settings is safer than replacing the document. If your current config already satisfies the requirements below, no config change is needed.

Required behavior:

1. Top-level `default_agent` is `prime`.
2. `experimental.task_model_selection` is `false`, preserving other `experimental` properties.
3. Provider `9router` exposes a model whose config key is exactly lowercase `sub`.
4. Do not add inline `agent.prime` or `agent.sub` definitions; the Markdown agents are global.
5. Do not disable or modify built-in agents.
6. Do not set a global `subagent_model`; Sub owns its model in agent frontmatter.
7. Preserve all unrelated providers, plugins, MCP settings, permissions and other configuration.

Minimum relevant shape:

```jsonc
{
  "default_agent": "prime",
  "experimental": {
    "task_model_selection": false
  },
  "provider": {
    "9router": {
      // Keep your existing npm/name/options and other models.
      "models": {
        "sub": {
          "id": "Sub",
          "name": "Sub",
          "reasoning": true,
          "tool_call": true
        }
      }
    }
  }
}
```

If config changes are needed, patch only these required properties into the existing document; do not replace the entire config.

## Verification

From a normal project directory:

```powershell
kilo debug agent prime
kilo debug agent sub
```

Prime must resolve as `mode: "primary"`, with effective Task permissions denying `*` and allowing `sub`.

Sub must resolve to:

```json
{
  "mode": "subagent",
  "model": {
    "providerID": "9router",
    "modelID": "sub"
  }
}
```

Sub's effective `tools.task` must be `false`.

Project `.kilo/agent/sub.md` or `.kilo/agents/sub.md` files may take precedence over the global installation. Reconcile project overrides if effective output differs.

## Routing policy

```text
EASY
  -> Sub executes directly
  -> Prime accepts with cheapest sufficient evidence

MEDIUM
  -> Prime performs bounded framing/architecture inspection
  -> Sub executes token-heavy work
  -> Prime reviews only where deterministic evidence is insufficient

HARD
  -> Prime owns initial hypothesis/design/decomposition
  -> Sub handles separable mechanical execution
  -> PRIME_DIRECT immediately when continuous stronger reasoning is required

FAILED_TECHNICAL
  -> Prime diagnoses/reclassifies
  -> at most one materially different SUB_RECOVERY when justified
  -> PRIME_DIRECT on repeated/no-progress failure

BLOCKED_EXTERNAL
  -> Human only for genuinely unavailable external capability/decision
```

Technical difficulty itself must never be escalated to Human.

## Rollback of agent files

If setup reported a backup directory, restore only the agent files you want from that directory back to:

```text
%USERPROFILE%\.config\kilo\agent\
```

No other Kilo files need to be restored because setup does not modify them.
