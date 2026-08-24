# Agent Lessons Learned

Practical, session-proven gotchas for this repo. Read before similar work.
Add new entries at the top; keep each entry to: **Mistake → Lesson → Action**.

## 2026-08-24 — Silent "pushed, deploy initiated" left prod on the old SHA

- **Rebase leftovers shipped because the agent stopped at push.** `payment-return/page.tsx` had `return () => {, user?.userId` (Turbopack "Expression expected"). `AiPackageCreditServiceTests.cs` had an extra `}` so the next `[Fact]` nested (CS0106). Build & Deploy failed; live stayed on `b23893ec` until the owner asked.
  → Never report done after push. Run `pnpm run ship:gate` before push and `pnpm run ship:watch` until Build & Deploy for **this SHA** succeeds. On fail: dump logs, fix, push again without waiting. Flip private only after that success.
  → `deploy.yml` `syntax-gate` now fails in seconds on conflict markers / leftover splices / brace imbalance so Docker does not start.
- **QA Smoke / Speaking 450 warnings are not deploy blockers.** The one Speaking lint *error* was the same payment-return parse. Ignore chronic red suites unless the error is in a touched file.

## 2026-08-24 — Antigravity gateway hardening + phases 3c–7

- **"Unused import" cleanup removed `get_settings` from server.py while `create_app()` still called it** → gateway crash-looped on the VPS and the blue/green health gate correctly blocked promotion. All 38 tests stayed green because every test passes `settings` explicitly — the production-only `create_app()` no-args path was never exercised.
  → When a factory/function has default-arg production paths, there must be a test that calls it **exactly the way production does** (`create_app()` with no args). Added `test_create_app_production_path_no_args_serves_healthz`.
  → Before deleting any import, grep the whole file, not just the visible section.
- **PowerShell 5.1 `Set-Content -Encoding UTF8` wrote a BOM into `package.json`** → pnpm in the Linux Docker build failed instantly with "Invalid package.json" (node on Windows tolerates the BOM, so local `node -e require()` checks pass — silent until CI).
  → NEVER patch JSON with `Set-Content -Encoding UTF8`. Use `[System.IO.File]::WriteAllText($path, $text)` (BOM-less UTF-8) or the Edit tool. After any JSON edit, check the first bytes: `[IO.File]::ReadAllBytes($p)[0..2]` must not be `EF BB BF`.
  → This broke the web image build for `f6ad1d090`/`5958debdb`; fixed in the BOM-strip commit.
- **Module-level function called a closure that lived inside `create_app()`** → `NameError` at runtime, caught only by tests.
  → When a helper is used by both endpoint closures *and* module-level functions (e.g. SSE generators), define it **module-scope taking `app`** from the start. Don't mix scopes.
- **Wrapped a stream that already acquires `session.lock` with another `async with session.lock`** → guaranteed deadlock (asyncio locks are non-reentrant).
  → Before wrapping generators/middleware around locked code, map **who owns the lock**; put a comment at the outer wrapper.
- **Pool unit tests constructed the real `google.antigravity.Agent`** → would boot the localharness binary in CI.
  → Any SDK-wrapping constructor needs a **factory seam parameter** (`agent_factory=None`) so tests inject a stub. Design the seam *before* writing tests.
- **Metrics label rendering emitted `name,agent="x" value` instead of `name{agent="x"} value`** — caught by the new metrics test.
  → When emitting a text protocol by hand, assert the **exact rendered line** in a test, not just "contains the metric name".
- **Quota/usage estimated the raw prompt while the upstream call sent an enriched one** → accounting drift; test expected one, impl measured the other.
  → Decide the **canonical prompt string once** (enrich at the call site, thread it through), then quota, usage, and tests all measure the same thing.
- **`cargo test` cannot run on this machine** — `tauri-winres`/mingw break on the repo path's spaces ("OET with Dr Hesham") and the GNU linker hits an export-ordinal limit.
  → For Rust validation use: `$env:CARGO_TARGET_DIR="$env:TEMP\oet-target"; cargo check` (type-checks, no link). Full cargo test only in CI/packaging.
- **`python` is not on PATH on this host**; Docker is not installed either.
  → Always use `agent-gateway\.venv\Scripts\python.exe`. Compose YAML is validated by CI/deploy, not locally.
- **Golden-set test expectations drifted from implementation semantics** (off-by-separator in token estimates).
  → Write the implementation's semantics down in the test comment and derive the expected value with the *same formula*, or better, export the formula and reuse it.
- **Pre-existing unrelated modified files (SKILL.md) sat in the worktree.**
  → Stage explicit paths only, never `git add -A`; run scoped `git status -- <paths>` first.
- **Script path arithmetic off-by-one** (`Path.parents[3]` vs `[2]`).
  → Derive repo root from the script location once, and immediately smoke-run the script after writing it (the watch script was run before commit and caught it).

## Environment quick facts (this host)

- Host validation: `pnpm exec tsc --noEmit`, `pnpm run lint`, `pnpm test`, `pnpm run backend:build`, `pnpm run backend:test`.
- Gateway tests: `agent-gateway\.venv\Scripts\python.exe -m pytest agent-gateway/tests -q` (no network/harness needed).
- Rust: `cargo check` with space-free `CARGO_TARGET_DIR` (see above).
- Ship-It: `pnpm run ship:gate` → public → push `main` → `pnpm run ship:watch` → fix/push on red without asking → private only after Build & Deploy for this SHA succeeds → live health.
