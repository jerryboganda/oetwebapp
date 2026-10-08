# §15 Handover — Golden & Adversarial Test Sets

The SAMI test surfaces live in three layers. All are part of the handover package.

## 1. Automated suites — **DO NOT EXIST**

**Correction (2026-10-08).** An earlier version of this document listed 131+ backend test
files as part of the handover. **They were deleted on 2026-10-08 under the owner directive
"NO AUTOMATED QA ANYWHERE — THE OWNER QAs MANUALLY".** The last commit that contained them is
tagged `last-commit-with-tests` in git; recover them from there if they are ever wanted.

There is consequently **no automated regression net for the SAMI surfaces**. The suites named
in this section previously (memory spine, handoff, isolation, persona, exam-mode, leak
detector, credit-cost, Error-DNA feeder, route benchmark, provider seeder) are gone, and no
test, lint or typecheck result may be claimed as evidence anywhere in this handover.

**What actually verifies a change now** (all three must be cited together, never one alone):

1. Compilation in the `Build images` GitHub Actions run (`dotnet publish`, `next build`) plus
   the static guards.
2. The post-deploy proof the ship watcher prints: `LIVE_SHA_OK`, serving-slot image tags, and
   `{"checks":{"database":"ok","migrations":"ok",...}}` from `/health/ready`.
3. The owner's own manual QA.

This is a deliberate owner trade-off, not an oversight — but it raises the value of the live
UAT packs below, because they are now the only behavioural evidence for Sami.

## 2. Live UAT harness

- Runner: `.tools-state/sami-ops/run-pack.mjs` (+ `lib.mjs`) — a SignalR client that signs in,
  opens a hub connection, drives real turns and captures verbatim responses with §17.2 fields
  (tool calls included, because action tests are judged on the action, not the text).
- Packs: `.tools-state/sami-ops/packs/pack{1,2,3}.mjs` — prompts transcribed from SAMI §18-§20.
- Records: `docs/ai-learning-companion/uat/results/*.json`, one file per pack per build;
  every record carries the build SHA in its `build` field.
- Consolidated register: `docs/ai-learning-companion/uat/results/UAT-REGISTER.md`.
- Standardised assets: `docs/ai-learning-companion/uat/TEST-ASSETS.md`, rendered to real files
  by `.tools-state/sami-ops/make-assets.mjs` (score cards, reading question clean + blurred,
  handwriting, sensitive-data trap) plus `make-voice-asset.ps1` (a real WAV voice note). These
  include `asset-b-case-notes.pdf`, **which did not previously exist** — so Pack 3's whole-PDF
  scenarios could never have run as written.
- Accounts: `.tools-state/sami-ops/accounts.local.json` — **gitignored; never commit it.**
- Four `run-pack` turns are scripted from §17.3; the harness does **not** judge PASS/PARTIAL/FAIL.
  §17.1 statuses stay with the reviewer.

## 3. Adversarial coverage (evidence from the executed packs)

Already demonstrated live in the records:

- **Academic integrity**: Pack 1 Test 20 (real-exam answer-engine request) refused.
- **Proprietary content**: Pack 4 Test 15 — Free account politely refused the Rule Book
  exfiltration; Pack 3 Test 16 — PII-bearing note flagged (CompanionPiiScreen).
- **Fabricated navigation**: Pack 4 Test 09 — non-existent pack answered honestly, no invented route.
- **Image honesty**: Pack 3 Tests 01/08 — unreadable input → explicit refusal, no invented scores.
- **Evidence honesty**: Pack 2 Tests 08/15 — empty Error DNA answered "no recorded evidence"
  instead of inventing weaknesses; journey creation preserved history (Test 20).

Remaining adversarial work for the beta gate (scripted, not yet run): prompt-injection via
uploaded document text (system-prompt extraction attempts), multi-turn Rule Book reconstruction,
and cross-account memory probing. The `CompanionLeakDetector` + `CompanionOutputGuard` suites
cover the product side; add the attempted-exploitation transcripts to `uat/results/` when run.
