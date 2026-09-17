#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def fm(path: str) -> str:
    text = read(path)
    m = re.match(r"---\n(.*?)\n---\n", text, re.S)
    assert m, f"{path}: missing frontmatter"
    return m.group(1)


def test_topology_and_model() -> None:
    prime = fm("agents/prime.md")
    sub = fm("agents/sub.md")
    assert "mode: primary" in prime
    assert '"*": deny' in prime and "sub: allow" in prime
    assert "mode: subagent" in sub
    assert "model: 9router/sub" in sub
    assert re.search(r"\n\s*task:\s*deny\s*$", sub, re.M)
    assert "steps:" not in prime
    assert "steps:" not in sub


def test_prompts_preserve_adaptive_recovery() -> None:
    prime = read("agents/prime.md")
    sub = read("agents/sub.md")
    for needle in ("EASY", "MEDIUM", "HARD", "FAILED_TECHNICAL", "BLOCKED_EXTERNAL", "9router/sub"):
        assert needle in prime
    assert "There is no fixed attempt budget" in prime
    assert "Never repeat a no-progress strategy" in prime
    assert "Sub `DONE` is a claim" in prime
    assert "contract revision" in prime
    assert "contract revision" in sub
    assert "Do not spawn agents or Tasks" in sub


def test_prime_bootstraps_minimal_state_lazily() -> None:
    prime = read("agents/prime.md")
    assert "if `.prime/state.json` does not exist" in prime
    for key in ('"objective"', '"tasks"', '"active"', '"status"', '"next"', '"git"', '"evidence"'):
        assert key in prime
    assert '"revision": 0' in prime
    assert '"revision": 1' in read("README.md")
    assert '"next": "capture objective"' in prime
    assert "preserve it and reconcile" in prime
    assert not (ROOT / "templates" / "state.json").exists()


def test_installer_is_global_only_and_verifies_without_config_mutation() -> None:
    ps = read("install.ps1")
    lower = ps.lower()
    assert "git " not in lower
    assert "projectroot" not in lower
    assert "kilo debug paths" in lower
    assert "kilo debug config" in lower
    assert "kilo debug agent prime" in lower
    assert "kilo debug agent sub" in lower
    assert "task_model_selection" in ps
    assert "9router" in ps and "modelID" in ps
    assert "kilo.json" not in lower and "kilo.jsonc" not in lower
    assert "userprofile" not in lower
    assert "created lazily by prime" in lower
    for primitive in ("set-content", "add-content", "out-file", "invoke-restmethod", "invoke-webrequest"):
        assert primitive not in lower


def test_guide_keeps_config_human_owned() -> None:
    guide = read("CONFIG-GUIDE.md")
    assert "task_model_selection" in guide
    assert "9router/sub" in guide
    assert "does **not** edit Kilo configuration" in guide
    assert "Built-in agents need no changes" in guide


if __name__ == "__main__":
    tests = [value for name, value in sorted(globals().items()) if name.startswith("test_") and callable(value)]
    for test in tests:
        test()
        print(f"PASS {test.__name__}")
    print(f"PASS {len(tests)} tests")
