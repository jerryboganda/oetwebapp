# Live Interlocutor v1.1 implementation evidence

## Status

**IMPLEMENTED AND PR-OPEN — REMOTE ACCEPTANCE BLOCKED BEFORE RUNNER START.**

The live OET Speaking role-play path is now wired through a server-side disclosure planner and deterministic renderer. Heavy verification was intentionally delegated to GitHub Actions; on 2026-09-20 every required GitHub-hosted job was refused before any workflow step ran because the account reported failed recent payments or an insufficient Actions spending limit.

This document records implementation evidence and the exact verification boundary. It does not claim that build, test, lint, typecheck, security, or E2E checks passed.

## Implemented factuality boundary

- The AI acts as the interlocutor for scored role-play; the server remains authoritative for case facts.
- Seven explicit interlocutor role classes are supported: Patient, RelativeOrCarer, AnimalOwner, Client, Colleague, Examiner, and Interviewer. Missing or unknown roles fail closed.
- Server facts are classified as AlwaysEligible, Conditional, or NeverDisclose.
- Conditional disclosure is evaluated server-side before provider invocation.
- NeverDisclose facts are never copied into the model-safe disclosure projection.
- The provider receives only the normalized `roleClass`, safe disclosure projection, and learner transcript needed for reply planning. Raw authored role text, scenario title, and setting are not sent to the provider.
- Model output is an ID-selection contract: eligible fact IDs plus exact server-approved statement variant IDs, or an approved non-factual response kind. Arbitrary provider-authored spoken text is not renderable.
- Unknown, withheld, duplicate, or mismatched selections fail closed.
- The server renders the final spoken text, sanitizes it, and only that server-rendered text is passed to `ConversationTtsRequest`.
- Defensive copies prevent later snapshot mutation from changing an already projected safe context.

## Candidate-first behavior

- Scored role-play uses `InterlocutorTurnPlanner`; warm-up remains on the existing generic conversation path.
- The silence-prompt path now checks for an actual candidate `roleplay` segment before the interlocutor can emit a silence prompt.
- Patient/interlocutor-only or warm-up history therefore cannot cause the AI to speak first in the scored consultation.

## Hidden-fact relevance hardening

The `.direct_relevant` disclosure path no longer unlocks a hidden sentence from a single generic overlapping token. Direct relevance now requires either:

- at least two overlapping content tokens; or
- one distinctive overlapping content token of at least eight characters.

The broader `.relevant` behavior is unchanged. This keeps intentionally broad elicitation behavior separate from direct hidden-fact unlocking.

## Numeric scoring lock

Numeric OET practice scoring remains fail-closed until authoritative human/expert calibration is implemented and validated.

- New v1.1 numeric assessment generation returns the validation-required conflict before provider scoring work.
- Existing persisted numeric assessment rows are blocked from learner-facing projection.
- Combined scoring stops before release/report aggregation and numeric writes.
- Technical/no-score review envelopes may still project when they contain no numeric fields or report payload.
- No owner/release boolean or current configuration flag can unlock the temporary hold.

This is a safety lock, not evidence that calibration has been completed.

## Regression coverage authored

Focused tests cover the disclosure policy, renderer fail-closed behavior, safe projection, planner behavior, raw-scenario-metadata exclusion, hidden-fact direct relevance, candidate-first silence prompting, and the numeric scoring lock. In particular:

- `Planner_never_sends_raw_scenario_metadata_to_the_model`
- `Hidden_fact_does_not_unlock_from_one_generic_overlap_token`
- `SilencePromptGuard_BlocksPatientPromptUntilCandidateHasSpokenInRoleplay`
- numeric-lock tests using forbidden provider/gateway doubles to prove the lock occurs before AI/provider work

These tests were not executed locally because project policy requires compute-heavy verification to run in GitHub Actions.

## Verification evidence

Local non-heavy checks:

- `git diff --cached --check` passed before commit.
- `git diff --check origin/main...HEAD` passed after the PR branch was created.
- GitHub reports PR #235 as `MERGEABLE`; `mergeStateStatus` is `UNSTABLE` because checks are failing before runner start.
- The only `origin/main` overlap among the four intervening commits is `Program.cs`; main adds TypeSafe/JeV registrations while this change adds the interlocutor planner registration at a separate location. No textual merge conflict is reported.

Remote verification attempt on PR #235:

- Speaking Module CI: run `35483774826`
- QA Smoke: run `35483774811`
- Rulebook Conformance: run `35483774885`
- SBOM and SCA: run `35483774808`

All failed jobs show zero executed workflow steps and the same GitHub annotation:

> The job was not started because recent account payments have failed or your spending limit needs to be increased.

Therefore no CI result currently validates or invalidates the implementation itself. The required next action is to restore GitHub Actions billing/spending availability and rerun the failed checks; no local heavy-compute fallback is permitted by project policy.

## Release boundary

- No deployment was performed.
- No merge was performed.
- Numeric scoring remains locked.
- Private consented v1.1 learner replay now defaults to 30 days when no governed `retention_days` approval exists; legacy/tutor retention defaults are unchanged. Current consent-version matching is enforced at the v1.1 capture seam. CI verification is still pending.
- Post-session v1.1 learner feedback is implemented on candidate exam results and standalone session results. Submission is learner-owned, bounded to a 1–5 rating plus optional comment, and upserts a deterministic record in the existing uniquely constrained IdempotencyRecords store. Only PostgreSQL unique-constraint conflicts enter the concurrent-insert recovery path. Native radio inputs support keyboard rating selection. CI verification is still pending.

Acceptance remains blocked until fresh remote verification actually runs and passes.
