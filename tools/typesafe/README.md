# tools/typesafe — Jev (TypeSafe SystemOne) integration kit

Server-side only. The API key lives in the environment
(`TYPESAFE_API_KEY` for this kit / `TYPESAFE__APIKEY` for the .NET backend —
the scripts read either) and is **never** passed on the command line, logged,
or committed. Only the masked form (head…tail, length) is ever reported.

## What lives here

| Path | Purpose |
|---|---|
| `e2e.mjs` | Live canonical E2E: one Choice + one Noul + one Score over an OET-style referral letter. Asserts `urgent_referral`, urgency ≥ 0.9, detail ≥ 2. Exit code 0 = pass. |
| `fixtures/*.json` | Calibration anchors: a genuine urgent referral, an adversarial injection attempt, a legitimate routine review + a gibberish submission. `expect` keys with `_gte` / `_lte` suffixes are threshold assertions. |

## Usage

```bash
cd "OET Project Web App"
TYPESAFE_API_KEY=... node tools/typesafe/e2e.mjs
```

## Conventions the backend enforces (do not drift)

- **Model is pinned** to `jev-1.13.0` in `TypeSafeOptions.Model` — never the
  `jev-latest` alias. Thresholds are tuned against a fixed version; bump the
  pin deliberately and re-run every fixture.
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
- **Context rot:** filter the state to the fields each question needs
  (32k-token state cap; accuracy falls with irrelevant detail).

## Phase 2 calibration (when Writing-pilot flags exist)

Extend this kit with a runner that replays every fixture through the
`jev.*` question designs and prints observed rates vs `expect`, before any
`TypeSafe:Enabled` flip. Seed further anchors from the 55 medicine model
answers and the Writing regression letters.
