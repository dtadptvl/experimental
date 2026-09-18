# Human Kilo configuration guide

Setup installs/verifies the two global Markdown agents only. It does **not** edit `kilo.json` or `kilo.jsonc`, built-in agents, or project files. Prime creates `.prime/state.json` lazily inside each project.

## Required effective settings

Merge only the relevant fields into your existing Kilo configuration; preserve unrelated settings, comments, provider credentials/metadata, plugins, MCP entries, permissions, and agents.

1. `experimental.task_model_selection` resolves to `true`.
2. Provider `9router` exposes model key `sub`, so `9router/sub` is valid.
3. `provider.9router.options.timeout` is a positive finite millisecond value.
4. `provider.9router.options.chunkTimeout` is a positive finite millisecond value.
5. Do not add global/project Prime/Sub overrides unless intentional. Project agent Markdown has higher precedence than the global installation.
6. Do not rely on `subagent_model` for this architecture.
7. Built-in agents need no changes.

Minimal relevant fragment (merge; do not replace your document):

```jsonc
{
  "experimental": {
    "task_model_selection": true
  },
  "provider": {
    "9router": {
      "options": {
        // Safety-net values, deliberately loose enough not to be normal routing controls.
        "timeout": 300000,
        "chunkTimeout": 300000
      },
      "models": {
        "sub": {
          // Preserve/use the real metadata required by your existing 9router provider.
        }
      }
    }
  }
}
```

`timeout` bounds request setup/first-byte waiting. `chunkTimeout` bounds silent gaps in a streamed response. They are required because current native Kilo does not impose a universal mid-stream watchdog when no provider chunk timeout is configured. The architecture otherwise uses native Kilo's Task lifecycle, Sub `steps`, `doom_loop`, and individual tool timeouts rather than a custom watchdog process.

Prime is intentionally not model-pinned by this package. Select/configure the stronger Prime model through normal Kilo model selection. If desired, set `default_agent` to `prime`; otherwise launch work with `--agent prime`.

## Why explicit Task model selection is required

Current Kilo CLI can reuse saved per-agent model state before an agent's own model field when resolving a Task child. Therefore `model: 9router/sub` in `sub.md` is necessary but not sufficient as a runtime guarantee. Prime explicitly selects `9router/sub` on every Task delegation, and `experimental.task_model_selection: true` enables that override.

## Verify

Run `setup.cmd` after the Human config edit. Setup uses native diagnostics:

```powershell
kilo debug config
kilo debug agent prime
kilo debug agent sub
```

It fails unless:

- Task model selection resolves enabled;
- explicit finite `9router` request and stream-inactivity timeouts are present;
- Prime resolves `mode: primary` and can Task only `sub`;
- Sub resolves `mode: subagent` and exactly `9router/sub`;
- Sub has a finite `steps` fuse;
- Sub's Task tool is disabled and its native `doom_loop` continuation is denied;
- common Git mutation commands remain unavailable to Sub while read-only Git inspection remains available.

For normal autonomous work use:

```powershell
kilo run --auto --agent prime "<objective>"
```

If the installer directory is inside a project, that project's higher-precedence `.kilo/agent(s)/prime.md`, `sub.md`, or other project configuration can affect verification. Reconcile such overrides manually; setup deliberately never overwrites project Kilo configuration. After setup passes, run Prime from each target project; Prime creates `.prime/state.json` there if absent and preserves it thereafter.
