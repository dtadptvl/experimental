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
    for phrase in ("commit/branch/push", "worktree", "edit `.prime/state.json`"):
        assert phrase in body


def test_prompts_preserve_adaptive_autonomous_recovery() -> None:
    prime = read("agents/prime.md")
    sub = read("agents/sub.md")
    for needle in ("EASY", "MEDIUM", "HARD", "FAILED_TECHNICAL", "BLOCKED_EXTERNAL", "9router/sub"):
        assert needle in prime
    pl = prime.lower()
    sl = sub.lower()
    assert "fixed total attempt count" in pl
    assert "repeat no-progress strategy" in pl
    assert "sub `done` != pass" in pl
    assert "spawn task/agent" in sl
    assert "reversible -> minimal-change -> backward-compatible" in pl


def test_prime_bootstraps_only_minimal_state() -> None:
    prime = read("agents/prime.md")
    assert "If absent create exactly" in prime
    for key in ('"objective"', '"tasks"', '"active"', '"acceptance"', '"next"', '"git"', '"evidence"'):
        assert key in prime
    assert '"revision":0' in prime
    assert '"next":"capture objective"' in prime
    assert "`/memory` = durable knowledge" in prime
    assert not (ROOT / "templates").exists()


def test_targeted_checks_and_prime_quality_bar() -> None:
    prime = read("agents/prime.md")
    sub = read("agents/sub.md")
    for text in (prime, sub):
        assert "targeted/cheap checks" in text
        assert "rebuild unchanged dependencies" in text
        assert "full acceptance suites" in text
    assert "PRIME'S QUALITY BAR" in prime
    assert "successful execution/tests" in prime.lower() or "tests alone" in sub.lower()
    assert "design quality" in prime
    assert "correctness OR material design quality" in prime


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
