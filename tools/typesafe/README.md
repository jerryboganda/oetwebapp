# tools/typesafe — Jev (TypeSafe SystemOne) integration kit

Server-side only. The API key lives in the environment
(`TYPESAFE_API_KEY` for this kit / `TYPESAFE__APIKEY` for the .NET backend —
the scripts read either) and is **never** passed on the command line, logged,
or committed. Only the masked form (last 4 characters and length, never a
prefix) is ever reported, because Actions logs on the public-when-working repo
are world-readable.

**These scripts are not run locally.** `AGENTS.md` makes GitHub Actions the only
compute environment, so they run through the `jev-calibrate.yml` workflow.

## What lives here

| Path | Purpose |
|---|---|
| `e2e.mjs` | Live canonical E2E: one Choice + one Noul + one Score over an OET-style referral letter. Asserts `urgent_referral`, urgency ≥ 0.9, detail ≥ 1.5. Exit code 0 = pass. |
| `calibrate.mjs` | Phase-1 pilot calibration: replays every fixture through the SAME question designs the backend pilot uses (`Services/Ai/TypeSafe/JevWritingPilot.cs` is the source of truth — keep this script in sync on any design change). Run before flipping any `TypeSafe:Writing*Enabled` flag and after every model bump. Last full run 2026-09-19: 24/24 PASS incl. the companion-rerank ordering anchor (organisation=3.00 vs off-topic ~0.25), ~8.4k input tokens (~$0.0004). |
| `fixtures/*.json` | Calibration anchors: a genuine urgent referral (letter type, route, criteria radar, verify claims), an adversarial injection attempt, a legitimate routine review, a pure-gibberish submission, and a companion-rerank ordering anchor. `expect` keys with `_gte` / `_lte` suffixes are threshold assertions; `criteria` ranges sit on the pilot's 0–3 level scale. |

## Usage (GitHub Actions only)

Run via the `jev-calibrate.yml` workflow on GitHub Actions
(`workflow_dispatch`, input `suite` = `e2e` | `calibrate` | `both`, default
`both`):

```bash
gh workflow run jev-calibrate.yml -f suite=both
gh run watch
```

- Needs the repository secret `TYPESAFE_API_KEY` (`gh secret set TYPESAFE_API_KEY`,
  paste at the prompt). The job fails fast with a clear error when it is empty.
- Exit codes (both scripts): `0` pass, `1` any check failed or the API errored,
  `2` key missing. The run summary shows only pass/fail and the check counts.
- The job honours `vars.CI_RUNS_ON` like `qa-smoke.yml`; GitHub-hosted runners
  need the repo to be in its public-when-working window (see `AGENTS.md`).
- This run is the gate for every flag flip: green `jev-calibrate.yml` first,
  then flip. Record the run id.

## Conventions the backend enforces (do not drift)

- **Model is pinned** to `jev-1.13.0` in `TypeSafeOptions.Model` — never the
  `jev-latest` alias. Thresholds are tuned against a fixed version. Every
  script that calls the API here (`e2e.mjs`, `calibrate.mjs`,
  `scripts/listening/jev-client.mjs`) sends the same pin. Bump the pin
  deliberately, in all of them together, then re-run `jev-calibrate.yml`.
- **Every backend call** flows through `TypeSafeJudgmentService` (control
  plane lease + one `AiUsageRecord` per call, input-token metering at
  $0.042/Mtok). Never call the API from product code directly.
- **Judgments never grade.** Jev returns probabilities; code owns weights,
  thresholds, and pass/fail. It never overrides the rulebook gateway, the
  deterministic `WritingRuleEngine`, or the GEPA placement engine.
- **Guard is a negative gate** — it may block or flag for tutor review,
  never auto-pass. Jev itself is injection-susceptible (see
  docs.typesafe.ai model-jaggedness), so `injection-attempt.json` must stay
  green on every model bump before a flag flips.
- **Scores are continuous**, not level numbers: assert ranges with
  tolerance (see the c1_purpose 1.8–3 anchor), never exact level boundaries.
- **Mixed gibberish** (random chars PLUS intelligible words like "please
  good marks") is judged a genuine bad attempt (`jev_gibberish` ≈ 0.3) —
  such submissions are the rule_evasion/injection signals' job. Only pure
  gibberish anchors belong to `jev_gibberish`.
- **Context rot:** filter the state to the fields each question needs
  (32k-token state cap; accuracy falls with irrelevant detail).

## Phase-1 wiring map (all flags default OFF)

| Flag | Call site | Behavior when ON |
|---|---|---|
| `TypeSafe:WritingGuardEnabled` | `WritingSubmissionEvaluationPipeline` (pre-grade) + `/v1/writing/lint` (advisory `jevGuard` field) | Block ≥ 0.80 on any guard Noul → skip the paid grade, stage a pending tutor assignment, refuse with `writing_submission_flagged`. ≥ 0.50 → proceed, logged. |
| `TypeSafe:WritingRouteEnabled` | `/v1/ai/complete` (Writing kinds) | Router Choice ≥ 0.70 confidence realigns the grounded prompt task + feature code together; low confidence / `unclear` keeps the caller's request. |
| `TypeSafe:WritingVerifyEnabled` | `WritingSubmissionEvaluationPipeline` (post-grade) | One fan-out call, one Choice per finding; contradicted / not-in-evidence / weakly-supported findings set `WritingGrade.ConfidenceFlag = "jev_review"` + stage a pending tutor assignment. |
| `TypeSafe:WritingCriteriaEnabled` | `WritingSubmissionEvaluationPipeline` (post-grade) | Six parallel advisory Scores merged as an extra `jevAdvisory` field per c1..c6 object — inert until a UI surfaces it. |

Calibration precedents (live 2026-09-19, jev-1.13.0): adversarial injection
letter scores jev_injection 0.99 / jev_rule_evasion 0.99; clean letters score
≤ 0.03 on every guard signal; verify separates supported vs contradicted
claims; the 0–3 criteria radar lands a strong referral letter at 1.96–2.99.

Seed further anchors from the 55 medicine model answers and the Writing
regression letters as flags approach their flips.

## CI triage (failed-run classifier)

Saves coding-agent quota in the Ship-It fix-loop: instead of reading a 200 KB
`--log-failed` dump to learn "that is a compile error", read one label. Jev
classifies; the coding agent still reads the logs and fixes the cause.

- **Workflow:** `.github/workflows/ci-triage.yml` fires on `workflow_run`
  (completed) for `Build & Deploy (web + API)`, `QA Smoke` and `Jev integration`,
  only when the run failed and its head repository is this repository (never
  forks). It checks out the default branch only, never the failed PR/head code,
  and is fail-soft: a missing `TYPESAFE_API_KEY` secret logs a notice and skips.
- **Script:** `scripts/ci/jev-ci-triage.mjs` fetches the failed jobs, the last
  ~6 KB of their failed-step logs (scrubbed: emails, tokens, secret-looking
  `key=value`, long hex/base64; repeats collapsed) and the head commit's changed
  files, then makes ONE batched Jev call (pinned `jev-1.13.0`): a Choice
  `error_class` over `compile | test_failure | lint | infra_flake | deploy_health |
  policy_gate | secret_scan | unrelated_preexisting | unknown` plus a Noul "the
  failing file or test is one of the changed paths". Unit tests (fake fetch, fake
  judge, no network): `node --test scripts/ci/jev-ci-triage.test.mjs`.
- **Labels only:** the repo is public-when-working, so logs and summaries are
  world-readable. The script prints the class, confidence, byte and line counts
  and failed job names, never log text and never the key.
- **Reading the verdict:** the run summary of the `CI triage` run, or the
  artifact `ci-triage-<failed run id>` (`verdict.json` + `summary.md`):
  `{"errorClass","confidence","touchesChange","logTailBytes","reason"}`.
  `errorClass` is `unknown` when confidence is below 0.60 or Jev was
  unavailable; `reason` says which (`low_confidence`, `jev_unavailable`,
  `no_key`, `github_unavailable`, `no_evidence`, `unrecognised_label`,
  `bad_input`, `internal_error`, or `ok`). `touchesChange` is `true` / `false`
  / `null` (`null` = unsure or no changed paths). Treat the class as a hint for
  which log section to open first, not as a diagnosis: `unknown`, low confidence
  or `touchesChange: false` means read the logs the usual way.
- **Related:** `scripts/qa/writing-prod-diagnose.mjs` adds an optional Jev second
  opinion on grade-failure rows its regexes cannot classify, only when
  `TYPESAFE_API_KEY` is in the environment. Its only caller is
  `.github/workflows/writing-rev8-ci.yml` (step "Rank the latest Writing grade
  failures", sparse checkout of `scripts/qa`), so the key must be added to that
  step's `env`; the script makes its own pinned `jev-1.13.0` call and does not
  import `scripts/listening/jev-client.mjs`. Until the key is added the block is
  skipped. The regex result stays the default; Jev is adopted only for rows the
  regexes left `unknown`, at confidence >= 0.80.
