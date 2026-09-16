from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
prime = (ROOT / "agents" / "prime.md").read_text(encoding="utf-8")
sub = (ROOT / "agents" / "sub.md").read_text(encoding="utf-8")

checks = {
    "easy_routes_directly_to_sub": all(x in prime for x in ["### EASY", "Delegate directly to Sub", "cheapest sufficient deterministic evidence"]),
    "medium_uses_prime_framing_then_sub": all(x in prime for x in ["### MEDIUM", "bounded, high-leverage inspection", "Delegate the token-heavy inspect -> edit -> test loop to Sub"]),
    "hard_can_skip_sub_first": all(x in prime for x in ["### HARD", "Do not force a ceremonial Sub-first attempt", "enter `PRIME_DIRECT` immediately"]),
    "hard_can_downgrade_mechanical_remainder": "downgrade mechanical remainder to Sub" in prime,
    "recovery_is_bounded": all(x in prime for x in ["one fresh recovery contract", "enter `PRIME_DIRECT` immediately", "do not chain fresh Subs indefinitely"]),
    "technical_failure_never_goes_directly_to_human": all(x in prime for x in ["`FAILED_TECHNICAL` always returns to Prime recovery", "Never ask the Human to solve ordinary implementation"]),
    "external_blocker_is_narrow": all(x in prime for x in ["### `BLOCKED_EXTERNAL`", "2FA/CAPTCHA", "physical hardware", "required external approval"]),
    "sub_model_is_pinned": "model: 9router/sub" in sub,
    "sub_cannot_spawn_agents": all(x in sub for x in ["task: deny", "Do not spawn agents or Tasks"]),
    "sub_returns_forensic_recovery_packet": all(x in sub for x in ["For `FAILED_TECHNICAL`, also include a recovery packet", "Hypotheses disproven", "Exact commands/tests that reproduce the failure"]),
}

failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(f"{'PASS' if ok else 'FAIL'} {name}")

if failed:
    raise SystemExit("policy invariant failures: " + ", ".join(failed))
