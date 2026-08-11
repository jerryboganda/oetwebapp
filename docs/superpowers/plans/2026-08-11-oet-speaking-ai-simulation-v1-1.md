# OET Speaking AI Simulation and Assessment v1.1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the supplied OET Speaking AI Simulation & Assessment Specification v1.1 across the existing website Speaking exam, actor, transcript, assessment, feedback, authoring, privacy, and operations boundaries.

**Architecture:** Reuse `SpeakingExamSession`, child `SpeakingSession`, `SpeakingRecording`, `SpeakingTranscript`, `MediaAsset`, `ConversationHub.SpeakingRoleplay`, the grounded AI gateway, rulebook services, AI usage ledger, and existing audit/retention paths. Add a versioned v1.1 simulation release, immutable card/persona snapshots, structured evidence and criterion scores, per-card/combined assessments, feedback projections, and release gates without reinterpreting historical nine-criterion tutor rows.

**Tech Stack:** ASP.NET Core Minimal API, EF Core/PostgreSQL, existing grounded AI gateway and rulebook services, Next.js App Router, React, TypeScript, Tailwind CSS, Vitest, Playwright, xUnit.

## Global Constraints

- Website/computer-based delivery only; no native/mobile scope.
- Full mock is warm-up plus two cards, with three-minute preparation and five-minute role play per card.
- Warm-up is excluded from every score.
- Audio is the only core scoring modality; eye contact, gestures, and facial expression are excluded.
- The actor never coaches, scores, reveals hidden tasks, corrects, praises, or gives real-world medical advice during scored role play.
- Card-to-card memory resets by default; carry-over requires an explicit second-visit flag and explicit approved indicator, and carries only owner-approved facts.
- “Your patient” alone never enables follow-up memory.
- Each evidence item has exactly one primary scoring criterion; teaching references cannot create additional deductions.
- Every learner score is labelled `AI Estimated Practice Score — not an official OET result`.
- Rulebook Rule 55 is excluded from Speaking runtime behavior.
- Existing media/user file I/O uses `IFileStorage`/`S3CompatibleFileStorage` and `/var/opt/oet-learner/storage`; no raw media `File.*`, `Path.*`, or `Directory.*` calls.
- All AI calls use grounded gateway helpers/services and record one usage row.
- Never read, write, stage, or commit `.env*`, credentials, tokens, customer recordings, or unrelated worktree changes.
- Run one focused host validation per completed slice; broad compile/build/deploy gates run through GitHub Actions.
- Stage explicit paths only. Preserve the existing tracked Listening/Reading edits and untracked `.codex/`, `.superpowers/`, `pdf-policy-release/`, and `pdf-policy-release2/` paths.

## File and boundary map

| Boundary | Files created or modified | Responsibility |
| --- | --- | --- |
| v1.1 persistence | `backend/src/OetLearner.Api/Domain/SpeakingSimulationV11Entities.cs`, `backend/src/OetLearner.Api/Data/LearnerDbContext.SpeakingSimulationV11.cs`, `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`, `backend/src/OetLearner.Api/Data/Migrations/20260901090000_AddSpeakingSimulationV11.cs` | Immutable release, snapshots, evidence, scores, assessments, and turn metrics |
| Release governance | `backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11ReleaseGate.cs`, `backend/src/OetLearner.Api/Endpoints/SpeakingSimulationV11GovernanceEndpoints.cs`, `app/admin/speaking/simulation/page.tsx` | Owner approvals, release state, calibration gate, budget gate, audit |
| Runtime actor | `backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11PersonaService.cs`, `backend/src/OetLearner.Api/Hubs/ConversationHub.SpeakingRoleplay.cs`, `backend/src/OetLearner.Api/Services/Speaking/SpeakingExamService.cs` | Current-card persona isolation, follow-up classification, actor guardrails, strict lifecycle |
| Transcript and scoring | `backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11EvidenceService.cs`, `backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11AssessmentService.cs`, `backend/src/OetLearner.Api/Services/SpeakingEvaluationPipeline.cs`, `backend/src/OetLearner.Api/Services/Speaking/SpeakingTranscriptionPipeline.cs` | Evidence extraction, technical fairness, primary-criterion assignment, calibrated score |
| Contracts and results | `backend/src/OetLearner.Api/Contracts/SpeakingSimulationV11Contracts.cs`, `backend/src/OetLearner.Api/Endpoints/SpeakingSimulationV11Endpoints.cs`, `lib/api/speaking-exams.ts`, `components/domain/speaking/SpeakingSimulationV11Result.tsx`, `components/domain/speaking/SpeakingSimulationV11ScoreGraph.tsx`, `app/speaking/exam/[id]/results/page.tsx` | Learner-safe API and complete feedback surface |
| Authoring | `backend/src/OetLearner.Api/Endpoints/AdminSpeakingContentEndpoints.cs`, `backend/src/OetLearner.Api/Services/AdminService.SpeakingRolePlayCards.cs`, `components/domain/speaking/RolePlayCardEditor.tsx`, `components/domain/speaking/InterlocutorScriptEditor.tsx`, `app/admin/content/speaking/role-play-cards/[id]/page.tsx`, `app/admin/content/speaking/role-play-cards/[id]/interlocutor/page.tsx` | Persona, allowed/prohibited facts, task map, scenario, difficulty, anchors, timing, version, follow-up fields |
| Operations and safety | `backend/src/OetLearner.Api/Services/Speaking/SpeakingAudioRetentionWorker.cs`, `backend/src/OetLearner.Api/Services/Speaking/SpeakingComplianceService.cs`, `backend/src/OetLearner.Api/Services/Conversation/ConversationRealtimeTurnStore.cs`, `docs/speaking/ai-simulation-v1-1.md` | Retention consent, access audit, latency/cost telemetry, fairness and runbook evidence |

---

### Task 1: Add the immutable v1.1 simulation data model and release gate

**Files:**
- Create: `backend/src/OetLearner.Api/Domain/SpeakingSimulationV11Entities.cs`
- Create: `backend/src/OetLearner.Api/Data/LearnerDbContext.SpeakingSimulationV11.cs`
- Modify: `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`
- Create: `backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11ReleaseGate.cs`
- Create: `backend/src/OetLearner.Api/Contracts/SpeakingSimulationV11Contracts.cs`
- Create: `backend/src/OetLearner.Api/Data/Migrations/20260901090000_AddSpeakingSimulationV11.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11ReleaseGateTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11SchemaTests.cs`

**Interfaces:**
- Produces `SpeakingSimulationV11ReleaseGate.EvaluateAsync(string professionId, CancellationToken ct)` returning an immutable `SpeakingSimulationV11GateResult`.
- Produces `SpeakingSimulationV11PersonaSnapshot`, `SpeakingSimulationV11Evidence`, `SpeakingSimulationV11CriterionScore`, `SpeakingSimulationV11Assessment`, and `SpeakingSimulationV11TurnMetric` entities.
- Produces `SpeakingSimulationV11RubricCriteria` with exactly ten criteria and the PDF weights `10,12,8,10,14,10,10,12,9,5`.

- [ ] **Step 1: Write failing release-gate tests.** Assert that the draft release is blocked when calibration approval, concurrency budget, cost ceiling, retention approval, graph approval, or profession-pack approval is absent; assert that Rule 55 is never an enabled Speaking rule; assert that the seeded weights sum to 100.

```csharp
[Fact]
public async Task EvaluateAsync_blocks_release_until_all_owner_gates_are_approved()
{
    await using var fixture = await SimulationV11Fixture.CreateAsync();
    var result = await fixture.Gate.EvaluateAsync("medicine", CancellationToken.None);

    Assert.False(result.IsReleased);
    Assert.Contains("calibration_approval_required", result.BlockingReasons);
    Assert.Contains("concurrency_budget_required", result.BlockingReasons);
    Assert.Contains("cost_ceiling_required", result.BlockingReasons);
    Assert.DoesNotContain("rule55", result.EnabledRuleIds);
}
```

- [ ] **Step 2: Run the focused tests and verify they fail for missing entities/gate.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingSimulationV11ReleaseGateTests|FullyQualifiedName~SpeakingSimulationV11SchemaTests --nologo`

Expected: compile/test failure because the v1.1 entities, rubric, and release gate do not exist.

- [ ] **Step 3: Implement the entities and EF mapping.** Store spec release, rubric/calibration release, owner approval records, card/persona snapshots, evidence, criterion scores, assessment status, audio-quality status, confidence/range, graph disclaimer, and per-turn latency/cost fields. Add indexes for exam/session, card, assessment status, evidence primary criterion, and generated time.

```csharp
public enum SpeakingSimulationV11AssessmentStatus
{
    Pending = 0,
    Complete = 1,
    TechnicalReview = 2,
    Invalid = 3,
}

public sealed record SpeakingSimulationV11GateResult(
    bool IsReleased,
    IReadOnlyList<string> BlockingReasons,
    string[] EnabledRuleIds,
    string SpecVersion,
    string RubricVersion);
```

- [ ] **Step 4: Add the PostgreSQL migration using idempotent table/index creation consistent with existing Speaking migrations.** Do not hand-edit the generated global model snapshot unless the existing migration workflow requires it; keep the migration provider-safe for PostgreSQL and test providers.

- [ ] **Step 5: Implement the release gate and exact rubric seed.** Treat owner-approved values as data, not environment-only comma-separated settings. Reject production release when required approval records are absent or values are non-positive. Keep development fixtures able to exercise released and blocked branches.

- [ ] **Step 6: Run the focused tests and verify they pass.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingSimulationV11ReleaseGateTests|FullyQualifiedName~SpeakingSimulationV11SchemaTests --nologo`

Expected: all focused tests pass; existing unrelated tests are not used as evidence for this task.

- [ ] **Step 7: Commit only Task 1 paths.**

```powershell
git add -- backend/src/OetLearner.Api/Domain/SpeakingSimulationV11Entities.cs backend/src/OetLearner.Api/Data/LearnerDbContext.SpeakingSimulationV11.cs backend/src/OetLearner.Api/Data/LearnerDbContext.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11ReleaseGate.cs backend/src/OetLearner.Api/Contracts/SpeakingSimulationV11Contracts.cs backend/src/OetLearner.Api/Data/Migrations/20260901090000_AddSpeakingSimulationV11.cs backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11ReleaseGateTests.cs backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11SchemaTests.cs
git diff --cached --check
git commit -m "feat(speaking): add v1.1 simulation release model"
```

### Task 2: Implement card/persona snapshots and strict actor memory isolation

**Files:**
- Create: `backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11PersonaService.cs`
- Modify: `backend/src/OetLearner.Api/Services/Speaking/SpeakingExamService.cs`
- Modify: `backend/src/OetLearner.Api/Endpoints/SpeakingExamEndpoints.cs`
- Modify: `backend/src/OetLearner.Api/Hubs/ConversationHub.SpeakingRoleplay.cs`
- Modify: `backend/src/OetLearner.Api/Contracts/SpeakingExamContracts.cs`
- Modify: `backend/src/OetLearner.Api/Services/Speaking/SpeakingSessionService.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11PersonaTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11MemoryScopeTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11ActorGuardrailTests.cs`

**Interfaces:**
- `Task<SpeakingSimulationV11PersonaSnapshot> CreateSnapshotAsync(string examId, string cardId, string slot, CancellationToken ct)`.
- `Task<SpeakingSimulationV11ActorContext> GetCurrentActorContextAsync(string sessionId, CancellationToken ct)`.
- `bool IsExplicitSecondVisit(RolePlayCard card)`; the implementation requires the persisted author flag and an approved indicator phrase, and returns false for “your patient” alone.

- [ ] **Step 1: Add failing tests for independent Card 2 reset, approved follow-up carry-over, and hidden-field non-projection.** Include a Card 1 transcript containing a unique fact and assert that an independent Card 2 actor context cannot read it. Include a Card 2 card whose only follow-up text is “your patient” and assert independent classification.

- [ ] **Step 2: Run the focused tests and verify they fail.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingSimulationV11PersonaTests|FullyQualifiedName~SpeakingSimulationV11MemoryScopeTests|FullyQualifiedName~SpeakingSimulationV11ActorGuardrailTests --nologo`

Expected: failure because snapshot/context services and explicit indicator validation are absent.

- [ ] **Step 3: Implement snapshot creation at card reveal.** Pin card version, profession pack, persona version, allowed/prohibited facts, reveal conditions, scenario type, difficulty, explicit second-visit flag, approved trigger, timing, rulebook version, actor prompt version, and release identifiers. Never copy the full Card 1 transcript into Card 2.

- [ ] **Step 4: Update the ConversationHub actor path.** Load only the current snapshot, enforce candidate-safe session state, keep turns brief, preserve overlap/barging transcript events, use configured neutral silence behavior only, and reject actor output containing hidden task/persona fields or coaching language before delivery. Keep assessor calls outside the active actor turn path.

- [ ] **Step 5: Run the focused tests and verify they pass.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingSimulationV11PersonaTests|FullyQualifiedName~SpeakingSimulationV11MemoryScopeTests|FullyQualifiedName~SpeakingSimulationV11ActorGuardrailTests --nologo`

- [ ] **Step 6: Commit only Task 2 paths.**

```powershell
git add -- backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11PersonaService.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingExamService.cs backend/src/OetLearner.Api/Endpoints/SpeakingExamEndpoints.cs backend/src/OetLearner.Api/Hubs/ConversationHub.SpeakingRoleplay.cs backend/src/OetLearner.Api/Contracts/SpeakingExamContracts.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingSessionService.cs backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11PersonaTests.cs backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11MemoryScopeTests.cs backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11ActorGuardrailTests.cs
git diff --cached --check
git commit -m "feat(speaking): isolate v1.1 actor persona state"
```

### Task 3: Add transcript, audio-quality, and primary-criterion evidence extraction

**Files:**
- Create: `backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11EvidenceService.cs`
- Modify: `backend/src/OetLearner.Api/Services/Speaking/SpeakingTranscriptionPipeline.cs`
- Modify: `backend/src/OetLearner.Api/Services/Speaking/SpeakingPreAnalysisService.cs`
- Modify: `backend/src/OetLearner.Api/Services/Speaking/SpeakingToneAssessor.cs`
- Modify: `backend/src/OetLearner.Api/Services/SpeakingEvaluationPipeline.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11EvidenceTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11TechnicalFairnessTests.cs`

**Interfaces:**
- `Task<SpeakingSimulationV11EvidenceBundle> ExtractAsync(SpeakingSimulationV11CardSnapshot snapshot, SpeakingTranscript transcript, IReadOnlyList<SpeakingRecording> recordings, CancellationToken ct)`.
- `SpeakingSimulationV11Evidence.PrimaryCriterionCode` is required, validated against the released rubric, and unique per evidence item.
- `SpeakingSimulationV11AudioQuality` remains separate from language-performance score inputs.

- [ ] **Step 1: Write failing tests for speaker/timestamp/confidence evidence, low-confidence exclusion, audio anchors, task states, and primary-criterion uniqueness.** Include the same transcript moment referenced by two teaching categories and assert exactly one score-affecting primary criterion.

- [ ] **Step 2: Run the focused evidence tests and verify failure.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingSimulationV11EvidenceTests|FullyQualifiedName~SpeakingSimulationV11TechnicalFairnessTests --nologo`

- [ ] **Step 3: Implement evidence extraction.** Preserve speaker labels, timestamps, word/segment confidence, fillers, pauses, false starts, repetitions, interruptions, overlaps, monologues, jargon, empathy opportunities, permission, checking-understanding, recap, closure, timing, and task coverage. Use rulebook R06-R12 and scenario rules through the rulebook service; do not read rulebook JSON directly from endpoints or UI.

- [ ] **Step 4: Implement technical fairness.** Mark low-confidence ASR, clipping, severe noise, missing audio, and abnormal turn latency as technical evidence. Do not turn those signals into candidate-language deductions. Add exact transcript/audio segment references for every score-affecting evidence item.

- [ ] **Step 5: Run the focused tests and verify pass.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingSimulationV11EvidenceTests|FullyQualifiedName~SpeakingSimulationV11TechnicalFairnessTests --nologo`

- [ ] **Step 6: Commit only Task 3 paths.**

```powershell
git add -- backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11EvidenceService.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingTranscriptionPipeline.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingPreAnalysisService.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingToneAssessor.cs backend/src/OetLearner.Api/Services/SpeakingEvaluationPipeline.cs backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11EvidenceTests.cs backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11TechnicalFairnessTests.cs
git diff --cached --check
git commit -m "feat(speaking): add v1.1 transcript evidence"
```

### Task 4: Implement calibrated ten-criterion assessment and complete feedback data

**Files:**
- Create: `backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11AssessmentService.cs`
- Create: `backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11FeedbackService.cs`
- Modify: `backend/src/OetLearner.Api/Services/SpeakingEvaluationPipeline.cs`
- Modify: `backend/src/OetLearner.Api/Services/Speaking/SpeakingAiAssessmentService.cs`
- Create: `backend/src/OetLearner.Api/Endpoints/SpeakingSimulationV11Endpoints.cs`
- Modify: `backend/src/OetLearner.Api/Program.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11AssessmentTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11FeedbackTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11CombinedScoreTests.cs`

**Interfaces:**
- `Task<SpeakingSimulationV11Assessment> AssessCardAsync(string sessionId, CancellationToken ct)`.
- `Task<SpeakingSimulationV11CombinedResult> BuildCombinedResultAsync(string examId, CancellationToken ct)`.
- `Task<SpeakingSimulationV11FeedbackBundle> BuildFeedbackAsync(string assessmentId, CancellationToken ct)`.
- `GET /v1/speaking/simulation-v1-1/{examId}/results` returns learner-safe card and combined projections.
- `POST /v1/speaking/simulation-v1-1/sessions/{sessionId}/assess` is idempotent and refuses warm-up or active-roleplay sessions.

- [ ] **Step 1: Write failing tests for exact ten-criterion weights, one-primary-criterion scoring, calibrated release gate, per-card score, valid combined score, invalid-card technical review, and persistent disclaimer text.**

- [ ] **Step 2: Run the focused assessment tests and verify failure.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingSimulationV11AssessmentTests|FullyQualifiedName~SpeakingSimulationV11FeedbackTests|FullyQualifiedName~SpeakingSimulationV11CombinedScoreTests --nologo`

- [ ] **Step 3: Implement the assessment service.** Load only the immutable snapshot and evidence bundle; reject unapproved rubric releases; assign each finding to one primary criterion; use the approved calibration table/service for the 0–500 practice estimate; persist confidence/range, rubric, model, prompt, transcript, and evidence references.

- [ ] **Step 4: Implement combined scoring.** Exclude warm-up. Require both valid card assessments for a full-mock combined score. If either card is technical-review or invalid, return `technical_review` without fabricating a combined number. Keep legacy OET/tutor score projections unchanged.

- [ ] **Step 5: Implement complete feedback.** Generate criterion strength/weakness/action, task map, communication timeline, language report, timing report, top five improvements, timestamp-linked better alternatives, 2–5 targeted tips, and ordered practice-plan recommendations through grounded services. Every quote must be a verified substring of the stored transcript segment.

- [ ] **Step 6: Register endpoints/services and run focused tests.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingSimulationV11AssessmentTests|FullyQualifiedName~SpeakingSimulationV11FeedbackTests|FullyQualifiedName~SpeakingSimulationV11CombinedScoreTests --nologo`

- [ ] **Step 7: Commit only Task 4 paths.**

```powershell
git add -- backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11AssessmentService.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingSimulationV11FeedbackService.cs backend/src/OetLearner.Api/Services/SpeakingEvaluationPipeline.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingAiAssessmentService.cs backend/src/OetLearner.Api/Endpoints/SpeakingSimulationV11Endpoints.cs backend/src/OetLearner.Api/Program.cs backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11AssessmentTests.cs backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11FeedbackTests.cs backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11CombinedScoreTests.cs
git diff --cached --check
git commit -m "feat(speaking): add v1.1 calibrated assessment"
```

### Task 5: Deliver the learner simulation and result surfaces

**Files:**
- Modify: `lib/api/speaking-exams.ts`
- Create: `lib/api/speaking-simulation-v11.ts`
- Create: `components/domain/speaking/SpeakingSimulationV11ScoreGraph.tsx`
- Create: `components/domain/speaking/SpeakingSimulationV11Result.tsx`
- Modify: `app/speaking/exam/[id]/page.tsx`
- Modify: `app/speaking/exam/[id]/results/page.tsx`
- Modify: `app/speaking/sessions/[id]/results/page.tsx`
- Test: `components/domain/speaking/__tests__/SpeakingSimulationV11Result.test.tsx`
- Test: `components/domain/speaking/__tests__/SpeakingSimulationV11ScoreGraph.test.tsx`
- Test: `app/speaking/exam/[id]/results/page.test.tsx`

**Interfaces:**
- `fetchSpeakingSimulationV11Results(examId: string): Promise<SpeakingSimulationV11Results>`.
- `SpeakingSimulationV11ScoreGraph` always renders the exact disclaimer string and never uses official OET green-band styling.
- Result components accept only learner-safe contracts and use `apiClient` for HTTP access.

- [ ] **Step 1: Write failing component/page tests.** Assert visible preparation/role-play phase, warm-up exclusion copy, technical-review branch, both card scores, combined score, ten criteria, confidence, graph disclaimer, task map, timestamp evidence, playable audio controls, top-five improvements, tips, and practice plan.

- [ ] **Step 2: Run the focused Vitest tests and verify failure.**

Run: `pnpm exec vitest run components/domain/speaking/__tests__/SpeakingSimulationV11Result.test.tsx components/domain/speaking/__tests__/SpeakingSimulationV11ScoreGraph.test.tsx "app/speaking/exam/[id]/results/page.test.tsx" --reporter=dot`

- [ ] **Step 3: Add typed API clients and learner-safe mapping.** Reject hidden fields at the client type boundary; preserve `technical_review` and `invalid` states instead of mapping them to zero scores.

- [ ] **Step 4: Implement the accessible exam/result UI.** Keep server timing authoritative, show reconnect/technical states, render the graph with platform palette and persistent label, and make transcript evidence activate audio at the exact timestamp without exposing hidden persona data.

- [ ] **Step 5: Run focused Vitest and scoped ESLint.**

Run: `pnpm exec vitest run components/domain/speaking/__tests__/SpeakingSimulationV11Result.test.tsx components/domain/speaking/__tests__/SpeakingSimulationV11ScoreGraph.test.tsx "app/speaking/exam/[id]/results/page.test.tsx" --reporter=dot`

Run: `pnpm exec eslint lib/api/speaking-exams.ts lib/api/speaking-simulation-v11.ts components/domain/speaking/SpeakingSimulationV11ScoreGraph.tsx components/domain/speaking/SpeakingSimulationV11Result.tsx "app/speaking/exam/[id]/page.tsx" "app/speaking/exam/[id]/results/page.tsx" "app/speaking/sessions/[id]/results/page.tsx" --quiet`

- [ ] **Step 6: Commit only Task 5 paths.**

```powershell
git add -- lib/api/speaking-exams.ts lib/api/speaking-simulation-v11.ts components/domain/speaking/SpeakingSimulationV11ScoreGraph.tsx components/domain/speaking/SpeakingSimulationV11Result.tsx "app/speaking/exam/[id]/page.tsx" "app/speaking/exam/[id]/results/page.tsx" "app/speaking/sessions/[id]/results/page.tsx" components/domain/speaking/__tests__/SpeakingSimulationV11Result.test.tsx components/domain/speaking/__tests__/SpeakingSimulationV11ScoreGraph.test.tsx "app/speaking/exam/[id]/results/page.test.tsx"
git diff --cached --check
git commit -m "feat(speaking): add v1.1 learner results"
```

### Task 6: Complete card/persona authoring and governance UI

**Files:**
- Modify: `backend/src/OetLearner.Api/Endpoints/AdminSpeakingContentEndpoints.cs`
- Modify: `backend/src/OetLearner.Api/Services/AdminService.SpeakingRolePlayCards.cs`
- Modify: `components/domain/speaking/RolePlayCardEditor.tsx`
- Modify: `components/domain/speaking/InterlocutorScriptEditor.tsx`
- Modify: `app/admin/content/speaking/role-play-cards/[id]/page.tsx`
- Modify: `app/admin/content/speaking/role-play-cards/[id]/interlocutor/page.tsx`
- Create: `backend/src/OetLearner.Api/Endpoints/SpeakingSimulationV11GovernanceEndpoints.cs`
- Create: `app/admin/speaking/simulation/page.tsx`
- Test: `backend/tests/OetLearner.Api.Tests/AdminSpeakingSimulationV11GovernanceTests.cs`
- Test: `app/admin/speaking/simulation/page.test.tsx`

**Interfaces:**
- Admin card save accepts candidate fields plus hidden persona, allowed facts, prohibited facts, reveal conditions, scenario pack, difficulty, anchors, timing, profession pack, version, `secondVisitEnabled`, and `secondVisitIndicator`.
- Governance endpoints expose release status and audited owner approval mutations only to authorized admin roles.

- [ ] **Step 1: Write failing API/UI tests for all required authoring fields, default-false second-visit flag, explicit indicator validation, publish blockers, owner-gate visibility, and role-based denial.**

- [ ] **Step 2: Run focused backend/frontend tests and verify failure.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~AdminSpeakingSimulationV11GovernanceTests --nologo`

Run: `pnpm exec vitest run app/admin/speaking/simulation/page.test.tsx --reporter=dot`

- [ ] **Step 3: Add persistence and publish validation.** Reject missing allowed/prohibited fact boundaries, missing profession pack, unsafe scenario content, missing timing, and enabled follow-up without an explicit approved indicator. Never expose hidden fields through learner card projections.

- [ ] **Step 4: Add the admin governance surface.** Show draft/released/blocked status, exact blocking reasons, rubric weights, calibration state, budget values, retention state, graph approval state, and Rule 55 exclusion. Require audited admin permission for mutations.

- [ ] **Step 5: Run focused tests, TypeScript, and diff checks.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~AdminSpeakingSimulationV11GovernanceTests --nologo`

Run: `pnpm exec vitest run app/admin/speaking/simulation/page.test.tsx --reporter=dot`

Run: `git diff --check`

- [ ] **Step 6: Commit only Task 6 paths.**

```powershell
git add -- backend/src/OetLearner.Api/Endpoints/AdminSpeakingContentEndpoints.cs backend/src/OetLearner.Api/Services/AdminService.SpeakingRolePlayCards.cs components/domain/speaking/RolePlayCardEditor.tsx components/domain/speaking/InterlocutorScriptEditor.tsx "app/admin/content/speaking/role-play-cards/[id]/page.tsx" "app/admin/content/speaking/role-play-cards/[id]/interlocutor/page.tsx" backend/src/OetLearner.Api/Endpoints/SpeakingSimulationV11GovernanceEndpoints.cs app/admin/speaking/simulation/page.tsx backend/tests/OetLearner.Api.Tests/AdminSpeakingSimulationV11GovernanceTests.cs app/admin/speaking/simulation/page.test.tsx
git diff --cached --check
git commit -m "feat(admin): govern Speaking simulation v1.1"
```

### Task 7: Add retention, access audit, latency/cost telemetry, and fairness gates

**Files:**
- Modify: `backend/src/OetLearner.Api/Services/Speaking/SpeakingAudioRetentionWorker.cs`
- Modify: `backend/src/OetLearner.Api/Services/Speaking/SpeakingComplianceService.cs`
- Modify: `backend/src/OetLearner.Api/Services/Conversation/ConversationRealtimeTurnStore.cs`
- Modify: `backend/src/OetLearner.Api/Services/Speaking/SpeakingAnalyticsService.cs`
- Modify: `ops/dashboards/speaking-funnel.json`
- Modify: `ops/dashboards/speaking-livekit.json`
- Create: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11OperationsTests.cs`
- Create: `docs/speaking/ai-simulation-v1-1.md`

**Interfaces:**
- Retention uses the approved release policy and keeps candidate-visible retention/deletion state synchronized with consent.
- Turn metrics persist p95-relevant latency, provider/model, retries, cost estimate, concurrency bucket, degradation, and technical-review outcome.
- Access logs record candidate/tutor/admin/support role, assignment/ticket scope, resource, timestamp, and action outcome.

- [ ] **Step 1: Write failing operations tests.** Cover shortest-approved retention behavior, deletion failure preserving DB pointers, owner-gate cost/latency blocking, technical-review transition on SLA breach, candidate/tutor/admin access boundaries, and no model-training reuse without consent.

- [ ] **Step 2: Run focused operations tests and verify failure.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingSimulationV11OperationsTests --nologo`

- [ ] **Step 3: Implement retention and access enforcement.** Reuse `IFileStorage`, existing consent/audit services, and the Speaking retention worker; never clear a media pointer before confirmed storage deletion.

- [ ] **Step 4: Implement telemetry and alert inputs.** Record turn-start p95 samples and completed-attempt cost components; mark sessions `technical_review` when approved thresholds are exceeded; expose dashboard fields without exposing secrets or customer audio.

- [ ] **Step 5: Run focused operations tests and `git diff --check`.**

- [ ] **Step 6: Commit only Task 7 paths.**

```powershell
git add -- backend/src/OetLearner.Api/Services/Speaking/SpeakingAudioRetentionWorker.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingComplianceService.cs backend/src/OetLearner.Api/Services/Conversation/ConversationRealtimeTurnStore.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingAnalyticsService.cs ops/dashboards/speaking-funnel.json ops/dashboards/speaking-livekit.json backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11OperationsTests.cs docs/speaking/ai-simulation-v1-1.md
git diff --cached --check
git commit -m "feat(speaking): add v1.1 operations safeguards"
```

### Task 8: Prove the PDF acceptance matrix and prepare the release

**Files:**
- Create: `backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11AcceptanceTests.cs`
- Create: `tests/e2e/speaking-simulation-v11.spec.ts`
- Modify: `tests/e2e/speaking-learner-ai-flow.spec.ts`
- Modify: `docs/ci/speaking.md`
- Modify: `docs/speaking/release-checklist.md`
- Modify: `.github/workflows/speaking-ci.yml`
- Modify: `.github/agent-state.local.md`

**Interfaces:**
- The acceptance tests use deterministic fixture providers for actor, ASR, TTS, calibration, and storage; they do not use production credentials or customer data.
- The E2E flow covers `/speaking/exam/[id]` and `/speaking/exam/[id]/results` with authenticated fixture identities and asserts learner-visible behavior only.

- [ ] **Step 1: Write the S-01–S-18 test matrix.** Each test name must cite the PDF ID and assert the exact invariant, including S-16 and S-17 memory rules and S-18 approved-budget latency.

- [ ] **Step 2: Run the focused acceptance tests before broad CI.**

Run: `dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter FullyQualifiedName~SpeakingSimulationV11AcceptanceTests --nologo`

Run: `pnpm exec vitest run components/domain/speaking/__tests__/SpeakingSimulationV11Result.test.tsx app/admin/speaking/simulation/page.test.tsx --reporter=dot`

Run: `pnpm exec playwright test tests/e2e/speaking-simulation-v1-1.spec.ts --project=chromium`

- [ ] **Step 3: Run the touched-area TypeScript/lint/build checks.**

Run: `pnpm exec tsc --noEmit --pretty false`

Run: `pnpm exec eslint lib/api/speaking-simulation-v11.ts components/domain/speaking/SpeakingSimulationV11Result.tsx components/domain/speaking/SpeakingSimulationV11ScoreGraph.tsx --quiet`

Run: `git diff --check`

- [ ] **Step 4: Run the GitHub Actions Speaking CI and Build & Deploy workflows.** Confirm the completed run SHA equals `origin/main`, migration application succeeds, web/API images build in Actions, and no source build runs on the VPS.

- [ ] **Step 5: Verify deployment evidence.** Check production web health, API live/readiness, database/migration/storage/stuck-job gates, deployed image SHA, and the protected learner/expert/admin endpoint boundaries. Record authenticated browser/provider/manual acceptance as verified or explicitly unverified.

- [ ] **Step 6: Update the agent state with exact evidence and remaining owner boundaries.** Do not claim v1.1 production completion while any owner approval gate, calibration evidence, authenticated browser acceptance, provider delivery, or live latency/cost budget remains unverified.

- [ ] **Step 7: Stage, commit, push `main`, and release.** Stage only the explicit Task 8 paths and any remaining Speaking implementation files; preserve unrelated worktree edits. Pushing `main` triggers the repository’s blue/green deployment workflow.

```powershell
git status --short
git add -- backend/tests/OetLearner.Api.Tests/Speaking/SpeakingSimulationV11AcceptanceTests.cs tests/e2e/speaking-simulation-v1-1.spec.ts tests/e2e/speaking-learner-ai-flow.spec.ts docs/ci/speaking.md docs/speaking/release-checklist.md .github/workflows/speaking-ci.yml .github/agent-state.local.md
git diff --cached --check
git commit -m "feat(speaking): complete AI simulation assessment v1.1"
git push origin main
```

## Plan self-review

- **Spec coverage:** Sections 1–19 map to Tasks 1–8: actor separation and memory (Task 2), lifecycle/timing (Task 2), transcript/technical fairness (Task 3), rubric/scoring/double-counting (Task 4), feedback/graph (Task 5), authoring/professions/roles (Task 6), privacy/retention/operations (Task 7), and S-01–S-18/deployment evidence (Task 8).
- **Placeholder scan:** No unresolved value is hidden as a production default. Owner-controlled fields are explicit release gates with concrete blocked states.
- **Type consistency:** The release gate owns spec/rubric eligibility; persona snapshots consume the gate’s release identifiers; evidence consumes snapshots; assessment consumes evidence; API/UI consume assessment projections; acceptance tests consume the public contracts.
- **Scope safety:** Existing session, recording, storage, tutor, and legacy assessment contracts remain in place. New data is versioned and additive.
