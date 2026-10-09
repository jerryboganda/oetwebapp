# F-045 is COMPLETE but NOT SHIPPED — blocked by an in-flight sibling migration

**State at 2026-10-09 ~17:20Z.** F-045 (Learning Fingerprint) + F-070 (confidence vs accuracy) are
built and sitting **uncommitted** in the working tree. They cannot be committed by themselves. This
note records exactly why, and the two safe ways to finish.

## What is built and waiting

| Item | Path |
| --- | --- |
| The feature | `backend/src/OetLearner.Api/Services/AiTools/Tools/CompanionLearningFingerprintTool.cs` (new) |
| The instrumentation | `backend/src/OetLearner.Api/Data/Migrations/20270120090000_AddReadingAnswerConfidence.cs` (new) |
| Registered | `AiToolRegistry.cs` (LearnerSafeToolCodes), `AiToolCatalogSeederHostedService.cs` (CompanionToolCodes), `Program.cs` (DI) — all three required, all verified present |
| Model + capture path | `ReadingEntities.cs` (`ReadingConfidence` enum + `ReadingAnswer.Confidence`), `ReadingAttemptService.cs` (optional confidence), `ReadingLearnerEndpoints.cs` (validated 0..3) |
| Snapshot | `LearnerDbContextModelSnapshot.cs` — the `Confidence` hunk at ~line 19614 |

Nothing was compiled, tested or linted (repo rule). First real proof would be `Build images`.

## The blocker, precisely

This is **one checkout shared with a concurrent agent** who is mid-flight on AI-provider capability
work. Their change is **uncommitted**:

- `backend/src/OetLearner.Api/Domain/AiProviderEntities.cs` — modified, adds `ParticipatesInAutoSelection`
- `backend/src/OetLearner.Api/Data/Migrations/20270119090000_AddAiProviderModelCapabilities.cs` — **untracked**, and it `AddColumn`s `ParticipatesInAutoSelection` plus creates `AiProviderModelCapabilities`

Why that stops an independent commit of F-045:

1. **`LearnerDbContextModelSnapshot.cs` carries both changes.** It holds my 3-line `Confidence` hunk
   (~19614) *and* their ~80 lines (~2145, ~2252). The file cannot be staged by hunk without also
   committing their model change.
2. **`AiProviderEntities.cs` is a tracked file they have modified.** Any commit that includes it
   necessarily includes their entity change.
3. **Their migration is untracked, so EF would see a model with no matching migration.** If the
   snapshot (or their entity) is committed while
   `20270119090000_AddAiProviderModelCapabilities.cs` stays on disk untracked, the committed model
   references a column that no committed migration creates. `speaking-ci`'s
   `migrations-check` (`dotnet ef migrations has-pending-model-changes`) triggers on
   `Data/Migrations/**` — which my own commit touches — and would fail.
4. **My migration sorts after theirs** (`20270120090000` > `20270119090000`), so committing mine
   without theirs also breaks timestamp ordering for a fresh database.

So a clean standalone F-045 commit requires either their work to be committed first, or their
migration to be committed alongside mine — and I will not commit another agent's in-flight work
under my message, because it would attribute and ship their unfinished change on my authority.

## Two safe ways to finish

**Option A — let the sibling land first (simplest, recommended).**
When the sibling commits their AI-provider work (including their migration), the interlock
disappears. Then:

```
git add -- backend/src/OetLearner.Api/Services/AiTools/Tools/CompanionLearningFingerprintTool.cs \
           backend/src/OetLearner.Api/Data/Migrations/20270120090000_AddReadingAnswerConfidence.cs \
           backend/src/OetLearner.Api/Domain/ReadingEntities.cs \
           backend/src/OetLearner.Api/Services/Reading/ReadingAttemptService.cs \
           backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs \
           backend/src/OetLearner.Api/Services/AiTools/AiToolRegistry.cs \
           backend/src/OetLearner.Api/Services/AiTools/AiToolCatalogSeederHostedService.cs
git commit -F .tools-state/sami-ops/COMMIT-MSG-14.txt   # write this message first
```
`LearnerDbContextModelSnapshot.cs` and `Program.cs` then need `git add -p` to take only the
`Confidence` / `CompanionLearningFingerprintTool` hunks, or may be included whole once their
changes are already in HEAD.

**Option B — commit the pair together, deliberately.** Commit their migration and my migration in
one commit with a message naming both. This is legitimate only with the sibling agent's knowledge,
since it ships their change.

⚠️ **Do not** "fix" this by deleting or renumbering `20270119090000_AddAiProviderModelCapabilities.cs`.
It is live work belonging to another session.

## Also outstanding for F-045 (not blockers, but not done)

- **Nothing sends confidence yet.** The DB column and the API field exist, but no learner-facing
  picker was built, so `Confidence` stays `NULL` and the tool honestly reports the confidence block
  as unmeasured. The answer-change, pace and distractor signals work from existing data immediately.
  A picker is a learner-facing timed-exam UX change and should be an owner decision, not a silent one.
- **No read-back:** the GET attempt projection (`ReadingLearnerEndpoints.cs:622`) and
  `ReadingPrivilegedQuestion` do not surface a saved rating. One-line additive change each.
- **Register not updated:** `features.json` still says F-045 `MISSING` and F-070 `PARTIAL`. Update
  only after the deploy is verified. `REGISTER-RECONCILIATION.md` §14.3 already carries the
  correction that three of the four signals already existed.
- **First compile is unverified** — including EF runtime translation of `x.Question!.Part!.PartCode`
  and the revision `GroupBy` (a sibling uses the same GroupBy in `ReadingTutorService.cs:391`).

## One correction worth keeping

The subagent found that `ReadingAttemptService` writes a `ReadingAnswerRevision` row on the **first**
save too, so revisions = changes + 1 and "changed the answer at least once" must be
`revisionCount >= 2`. Using `>= 1` would have reported **every answered question as changed** — a
confident, plausible, completely wrong Learning Fingerprint. It is implemented as `>= 2`.
