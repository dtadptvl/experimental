# Kilo Structured Recall V1

Experimental, minimal Zero-Mem-inspired retrieval for Kilo CLI.

Upstream audited target: `Kilo-Org/kilocode@010f511d731df2649bb3f9b660e309ea7e869ac8`.

V1 reuses Kilo's raw Session/Message/Part store and `RecallSearch`. It adds no database, vector store, model call, agent, scheduler, or daemon.

It adds deterministic engineering-entity extraction, multi-query lexical/entity retrieval, cross-query fusion, temporal neighbor closure, bounded provenance-bearing evidence injection, and an opt-in gate: `KILO_EXPERIMENTAL_STRUCTURED_RECALL=1`.

It does not replace compaction, `kilo_local_recall`, explicit project memory, or auto-consolidation yet.

## Apply

```bash
python scripts/apply.py /path/to/kilocode --check
python scripts/apply.py /path/to/kilocode
```

Then enable with `KILO_EXPERIMENTAL_STRUCTURED_RECALL=1`.

Targeted tests:

```bash
node --experimental-strip-types packages/opencode/test/kilocode/structured-recall.test.ts
python scripts/test_apply.py
```

Safety invariants: raw transcript remains source of truth; retrieved history is untrusted/escaped; current prompt boundary is excluded; injected evidence is synthetic/non-persistent; payload-prune reloads re-inject evidence; current repo/tool state wins conflicts; retrieval is bounded; structured recall makes no model call.

A persistent graph/PPR or dense embeddings are intentionally deferred until coding-session A/B evidence shows material misses.
