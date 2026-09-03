# 05 — Model Answer Pre-Generation (Preparation-Time)

## Lifecycle (strictly separated from grading)

1. Prepare source: canonical sentences + exact task + profession rulebook.
2. Generate once: `POST /v1/admin/writing/tasks/{id}/model-answer/generate`
   (`writing.model-answer-pregen.v1`, pinned anthropic/claude-sonnet-5,
   max thinking effort) — admin-triggered ONLY, never from candidate submit.
3. Save: `WritingTaskModelAnswers` 1:1 per `ScenarioId` with `SourceContentHash`
   (task + relevant/maybe sentences), `RulebookVersion`, `PromptVersion`,
   `ModelUsed`, `GeneratedAt`.
4. Version/staleness: shared `ComputeSourceContentHash`; status endpoint and
   batch flow recompute it to detect drift (task/case-note edits).
5. Quality gate: 180–200 words, `WritingModelAnswerGroundingValidator`
   (every sentence traceable to case notes), hold reasons
   (`…_unreadable|word_count_out_of_range|unmapped_sentence|generation_failed|…_unavailable`);
   held answers are invisible until fixed. Explicit `approve` sets
   `Ready + IsCandidateVisible`.
6. Display: after grading, the SAVED answer is copied into the per-submission
   report row and shown as the reference exemplar.
7. Reuse: every later candidate on the task sees the same stored version.
8. Regenerate only when: source drifts, rulebook changes materially, admin
   re-runs, or quality review fails. Normal submissions: ZERO generation calls
   (proven by `GenerateMissing_*` tests: repeat runs add no provider calls).

## Batch backfill (new)

`POST /v1/admin/writing/model-answers/generate-missing?limit=5&includeStale=false`
→ oldest published tasks first; skips Ready+visible+fresh (`already_ready`),
Ready+visible+stale unless requested (`stale_refresh_not_requested`),
Ready+invisible (`awaiting_approval` — never discards a pending review);
generates missing/Held/Rejected one-by-one (sequential, limit clamped 1–25,
per-item try/catch so one failure never aborts the batch). Re-run to resume.

## Legacy per-submit fallback (unchanged, now gated)

`WritingModelAnswerService.PopulateAsync` still covers already-published tasks
without an approved pregen (runs AFTER scoring, uses task+case notes, never
scores). The publish gate (`model_answer_not_approved`) ensures all newly
published tasks have an approved pregen, so the fallback trends to zero. It
was NOT removed to avoid breaking display on legacy tasks before backfill.

## Quality requirements mapping

Exact case facts + task + profession rulebook + OET purpose/recipient/register
are prompt-enforced; no invented facts (grounding validator + approval);
exemplar-not-key (grading never reads it — canary test); admin review before
visibility (approve gate).
