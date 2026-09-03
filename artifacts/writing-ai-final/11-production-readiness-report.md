# 11 — Production Readiness Report

## Readiness state (code-enforced from this release)

- [x] Profession valid (taxonomy + `RulebookProfessionParser` gate)
- [x] Letter Type valid (LT-* catalogue; LT-RP retired → LT-OT; Garcia → LT-DG migration included)
- [x] Canonical case-note text required at publish (`case_notes_required`)
- [x] Exact Writing Task required at publish (`written_task_required`)
- [x] Rulebook resolvable at publish (`rulebook_unresolvable`); runtime v1.0.1
      with 26 FINAL MASTER corrections; profession-specific precedence;
      Speaking isolated
- [x] Saved Model Answer + approval required at publish
      (`model_answer_not_approved`)
- [x] Submit always available (no minima); blank → deterministic zero
- [x] No live OCR in grading; `case_note_pages_unreadable` only for legacy
      unprepared tasks (backfill runbook provided)
- [x] No exemplar-similarity scoring (canary test)
- [x] One submit = one logical job (content guard + stable key + claim + reuse)
- [x] Controlled retries/backoff; no double charge; submission preserved;
      `retry-grade` resume + grading-page retry card
- [x] Normal Practice AI-only (no tutor selector added)

## Incomplete counts (require live DB — fill via runbook)

- Tasks with incomplete canonical case notes: TBD (`preparation-status` CSV)
- Tasks with missing exact Writing Task: TBD
- Tasks without approved Model Answer: TBD
- Tasks without valid rulebook mapping: TBD (expected 0 — parser covers all catalogue professions)
- Candidate-visible broken tasks: TBD → drive to 0 via backfill, then optionally
  flip learner list to grading-ready-only (deferred deliberately; see 02)

Target for each: 0 before declaring the data migration complete.

## Remaining risks / blockers (honest)

1. **Prod data backfill not executed here** (no DB access): preparation-status
   export → extract-from-pdf → task-text completion → generate-missing →
   review/approve → re-verify. Owner/admin runbook in 02 + script below.
2. **Assessment pack approvals**: preflight still requires an approved
   candidate-facing pack per (profession, pack-letter-type) plus derivable
   recipient/diagnosis from task text. Tasks whose packs are unapproved stay
   release-blocked BY DESIGN (governed release). Approve packs or narrow the
   gate only via owner decision — not silently in code.
3. **Profession-specific OW rulebook editions** (237–240 rules/profession in the
   FINAL MASTER PDFs) are not yet in the runtime JSONs (172 cloned rules).
   Needs a content sync with owner review; runtime is now CORRECT (no
   overridden rules) but not yet COMPLETE vs the newest edition.
4. **Live 10-way parallel submit test** recommended post-deploy (06).
5. Concurrent catalogue work in the same tree (other actor) — coordinate before
   deploy; my commit stages only the files listed in §14 of the final report.

## Database changes

None (no migration in this change). The Garcia LT-DG reclassification
migration (`20261216090000_ReclassifyWritingResponseLetterType`, idempotent,
data-only) ships alongside from the catalogue track.
