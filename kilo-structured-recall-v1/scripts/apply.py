#!/usr/bin/env python3
from __future__ import annotations
import argparse, shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NEW_FILES = [
    Path("packages/opencode/src/kilocode/session/structured-recall.ts"),
    Path("packages/opencode/src/kilocode/session/structured-recall-host.ts"),
]
PROMPT = Path("packages/opencode/src/session/prompt.ts")
IMPORT_ANCHOR = 'import { KiloSessionPrompt } from "@/kilocode/session/prompt" // kilocode_change\n'
IMPORT_LINE = 'import { KiloStructuredRecall } from "@/kilocode/session/structured-recall-host" // kilocode_change\n'
MAIN_ANCHOR = '''          yield* plugin.trigger("experimental.chat.messages.transform", {}, { messages: msgs })\n\n          // kilocode_change start — ephemeral context injection + post-summary\n'''
INJECT = '''          yield* KiloStructuredRecall.inject({
            msgs,
            sessionID,
            currentMessageID: lastUser.id,
            projectID: String(ctx.project.id),
            directories: [ctx.worktree],
            sessions,
          })\n'''
MAIN_REPLACEMENT = MAIN_ANCHOR.split("\n\n")[0] + "\n\n" + INJECT + "\n          // kilocode_change start — ephemeral context injection + post-summary\n"
PRUNE_ANCHOR = '''            yield* plugin.trigger("experimental.chat.messages.transform", {}, { messages: msgs })\n            KiloSessionPrompt.injectEditorContext({ msgs, session, sessionID, cache: envCache })\n'''
PRUNE_REPLACEMENT = '''            yield* plugin.trigger("experimental.chat.messages.transform", {}, { messages: msgs })\n''' + INJECT.replace("          ", "            ") + '''            KiloSessionPrompt.injectEditorContext({ msgs, session, sessionID, cache: envCache })\n'''

def transform_prompt(text: str) -> str:
    if IMPORT_LINE not in text:
        if text.count(IMPORT_ANCHOR) != 1: raise RuntimeError("prompt.ts import anchor mismatch; upstream changed")
        text = text.replace(IMPORT_ANCHOR, IMPORT_ANCHOR + IMPORT_LINE, 1)
    if INJECT not in text:
        if text.count(MAIN_ANCHOR) != 1: raise RuntimeError("prompt.ts main injection anchor mismatch; upstream changed")
        text = text.replace(MAIN_ANCHOR, MAIN_REPLACEMENT, 1)
    if PRUNE_REPLACEMENT not in text:
        if text.count(PRUNE_ANCHOR) != 1: raise RuntimeError("prompt.ts post-prune injection anchor mismatch; upstream changed")
        text = text.replace(PRUNE_ANCHOR, PRUNE_REPLACEMENT, 1)
    return text

def check_tree(repo: Path) -> None:
    target = repo / PROMPT
    if not target.is_file(): raise RuntimeError(f"not a Kilo checkout: missing {PROMPT}")
    transform_prompt(target.read_text(encoding="utf-8"))

def apply(repo: Path) -> None:
    check_tree(repo)
    for rel in NEW_FILES:
        src, dst = ROOT / rel, repo / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
    target = repo / PROMPT
    original = target.read_text(encoding="utf-8")
    updated = transform_prompt(original)
    if updated != original: target.write_text(updated, encoding="utf-8", newline="\n")

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("repo", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    repo = args.repo.resolve()
    if args.check:
        check_tree(repo); print("PASS patch anchors"); return 0
    apply(repo); print("PASS applied structured recall V1"); return 0

if __name__ == "__main__":
    raise SystemExit(main())
