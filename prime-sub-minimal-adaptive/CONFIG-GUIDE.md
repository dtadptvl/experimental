# Human Kilo configuration guide

Setup installs the two global Markdown agents and project state only. It does **not** edit Kilo configuration.

## Required effective settings

Merge only these requirements into your existing configuration; preserve unrelated settings, comments, providers, plugins, MCP entries, permissions, and agents.

1. `experimental.task_model_selection` resolves to `true`.
2. Provider `9router` exposes a model whose model key is exactly `sub`, so `9router/sub` is valid.
3. Do not add a project/global Prime/Sub override unless intentional. Project agent files have higher precedence than the installed global files.
4. Do not rely on `subagent_model` for this architecture.
5. Built-in agents need no changes.

Minimal relevant fragment (merge; do not replace your document):

```jsonc
{
  "experimental": {
    "task_model_selection": true
  },
  "provider": {
    "9router": {
      "models": {
        "sub": {
          // Preserve/use the real metadata required by your existing 9router provider.
        }
      }
    }
  }
}
```

Prime is intentionally not model-pinned by this package. Select/configure the stronger Prime model through normal Kilo model selection. If desired, set `default_agent` to `prime`; otherwise launch work with `--agent prime`.

## Why explicit Task model selection is required

Current Kilo can reuse saved per-agent CLI model state before an agent's own model field when resolving a Task child. Therefore `model: 9router/sub` in `sub.md` is necessary but not sufficient as a runtime guarantee. Prime explicitly selects `9router/sub` on every Task delegation, and `experimental.task_model_selection: true` enables that override.

## Verify

Run `setup.cmd` again after the Human config edit. Setup executes, in the target project:

```powershell
kilo debug config
kilo debug agent prime
kilo debug agent sub
```

It fails unless:

- Task model selection resolves enabled;
- Prime resolves `mode: primary` and can Task only `sub`;
- Sub resolves `mode: subagent`;
- Sub resolves exactly `9router/sub`;
- Sub's Task tool is disabled.

For normal autonomous work use Kilo's auto mode, for example:

```powershell
kilo run --auto --agent prime "<objective>"
```

A project `.kilo/agent(s)/prime.md` or `sub.md`, higher-precedence inline/managed configuration, or other effective override may make setup verification fail. Reconcile that override manually; setup deliberately never overwrites project Kilo configuration.
