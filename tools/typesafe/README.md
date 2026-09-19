# tools/typesafe — Jev (TypeSafe SystemOne) integration kit

Server-side only. The API key lives in the environment
(`TYPESAFE_API_KEY` for this kit / `TYPESAFE__APIKEY` for the .NET backend —
the scripts read either) and is **never** passed on the command line, logged,
or committed. Only the masked form (head…tail, length) is ever reported.

## What lives here

| Path | Purpose |
|---|---|
| `e2e.mjs` | Live canonical E2E: one Choice + one Noul + one Score over an OET-style referral letter. Asserts `urgent_referral`, urgency ≥ 0.9, detail ≥ 1.5. Exit code 0 = pass. |
| `calibrate.mjs` | Phase-1 pilot calibration: replays every fixture through the SAME question designs the backend pilot uses (`Services/Ai/TypeSafe/JevWritingPilot.cs` is the source of truth — keep this script in sync on any design change). Run before flipping any `TypeSafe:Writing*Enabled` flag and after every model bump. Last full run 2026-09-19: 21/21 PASS, ~7.6k input tokens (~$0.0003). |
| `fixtures/*.json` | Calibration anchors: a genuine urgent referral (letter type, route, criteria radar, verify claims), an adversarial injection attempt, a legitimate routine review, and a pure-gibberish submission. `expect` keys with `_gte` / `_lte` suffixes are threshold assertions; `criteria` ranges sit on the pilot's 0–3 level scale. |

## Usage

```bash
cd "OET Project Web App"
TYPESAFE_API_KEY=... node tools/typesafe/e2e.mjs
TYPESAFE_API_KEY=... node tools/typesafe/calibrate.mjs
```

## Conventions the backend enforces (do not drift)

- **Model is pinned** to `jev-1.13.0` in `TypeSafeOptions.Model` — never the
  `jev-latest` alias. Thresholds are tuned against a fixed version; bump the
  pin deliberately and re-run `calibrate.mjs`.
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
