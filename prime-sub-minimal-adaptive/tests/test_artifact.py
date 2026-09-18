#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def fm(path: str) -> str:
    text = read(path)
    match = re.match(r"---\n(.*?)\n---\n", text, re.S)
    assert match, f"{path}: missing frontmatter"
    return match.group(1)


def test_exact_topology_model_and_native_fuses() -> None:
    prime = fm("agents/prime.md")
    sub = fm("agents/sub.md")
    assert "mode: primary" in prime
    assert '"*": deny' in prime and "sub: allow" in prime
    assert "mode: subagent" in sub
    assert "model: 9router/sub" in sub
    assert re.search(r"^steps:\s*[1-9]\d*\s*$", sub, re.M)
    assert re.search(r"^\s*task:\s*deny\s*$", sub, re.M)
    assert re.search(r"^\s*doom_loop:\s*deny\s*$", sub, re.M)


def test_sub_git_ownership_boundary() -> None:
    sub = fm("agents/sub.md")
    assert '"git *": deny' in sub
    for safe in ("git status *", "git diff *", "git show *", "git log *", "git rev-parse *"):
        assert f'"{safe}": allow' in sub
    body = read("agents/sub.md")
    for phrase in ("Do not create commits", "worktrees", "Do not edit `.prime/state.json`"):
        assert phrase in body


def test_prompts_preserve_adaptive_autonomous_recovery() -> None:
    prime = read("agents/prime.md")
    sub = read("agents/sub.md")
    for needle in ("EASY", "MEDIUM", "HARD", "FAILED_TECHNICAL", "BLOCKED_EXTERNAL", "9router/sub"):
        assert needle in prime
    assert "There is no fixed total recovery-attempt budget" in prime
    assert "Never repeat a no-progress strategy" in prime
    assert "Sub `DONE` is a claim" in prime
    assert "Do not spawn agents or Tasks" in sub
    assert "reversible -> minimal-change -> backward-compatible" in prime


def test_prime_bootstraps_only_minimal_state() -> None:
    prime = read("agents/prime.md")
    assert "If `.prime/state.json` is absent" in prime
    for key in ('"objective"', '"tasks"', '"active"', '"acceptance"', '"next"', '"git"', '"evidence"'):
        assert key in prime
    assert '"revision": 0' in prime
    assert '"next": "capture objective"' in prime
    assert "Native `/memory` is for durable project knowledge, not live orchestration state" in prime
    assert not (ROOT / "templates").exists()


def test_contract_has_only_required_identity_and_context() -> None:
    readme = read("README.md")
    for key in ('"id"', '"revision"', '"objective_revision"', '"owned"', '"excluded"', '"invariants"', '"accept"', '"git"', '"evidence"', '"recovery"'):
        assert key in readme
    for banned in ("policy_fingerprint", "attempt_count", "complexity_score"):
        assert banned not in readme


def test_installer_global_only_no_kilo_config_mutation() -> None:
    ps = read("install.ps1")
    lower = ps.lower()
    assert "join-path $kiloroot 'agents'" in lower
    assert "kilo debug paths" in lower
    assert "kilo debug config" in lower
    assert "kilo debug agent prime" in lower
    assert "kilo debug agent sub" in lower
    assert "task_model_selection" in ps
    assert "chunkTimeout" in ps and "timeout" in ps
    assert "9router" in ps and "modelID" in ps
    assert "kilo.json" not in lower and "kilo.jsonc" not in lower
    assert "created lazily by prime" in lower
    for primitive in ("set-content", "add-content", "out-file", "invoke-restmethod", "invoke-webrequest"):
        assert primitive not in lower


def test_human_config_guide_has_only_required_native_settings() -> None:
    guide = read("CONFIG-GUIDE.md")
    assert "task_model_selection" in guide
    assert "9router/sub" in guide
    assert '"timeout": 300000' not in guide
    assert '"chunkTimeout": 1800000' in guide
    assert '300,000 ms request/first-byte timeout by default' in guide
    assert 'Do not set `provider.9router.options.timeout` to `false`' in guide
    assert "does **not** edit `kilo.json` or `kilo.jsonc`" in guide
    assert "Built-in agents need no changes" in guide
    assert "current native kilo does not impose a universal mid-stream watchdog" in guide.lower()


def test_package_has_no_recreated_runtime_machinery() -> None:
    files = [p.relative_to(ROOT).as_posix() for p in ROOT.rglob("*") if p.is_file()]
    assert sorted(p for p in files if p.startswith("agents/")) == ["agents/prime.md", "agents/sub.md"]
    assert not any("scheduler" in p or "journal" in p or "governance" in p or "worktree" in p for p in files)
    readme = read("README.md")
    assert "There is no custom scheduler" in readme


if __name__ == "__main__":
    tests = [value for name, value in sorted(globals().items()) if name.startswith("test_") and callable(value)]
    for test in tests:
        test()
        print(f"PASS {test.__name__}")
    print(f"PASS {len(tests)} tests")
