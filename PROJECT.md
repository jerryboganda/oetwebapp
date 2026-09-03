# Project: OET Preparation Platform & Multi-Exam Engine

## Architecture

The OET Preparation Platform is an enterprise-grade medical English and healthcare communication exam preparation platform built with a high-performance, decoupled architecture:
- **Frontend**: Next.js 16 (App Router), React 19, TypeScript, Tailwind CSS v4, Motion v12, Canvas/Tiptap annotations, and Recharts analytics.
- **Backend API**: ASP.NET Core Minimal API (.NET 10), Entity Framework Core, PostgreSQL with row-level concurrency, SignalR real-time communication, and ClamAV scanning.
- **Storage Infrastructure**: Abstracted file storage (`IFileStorage`) backed by persistent named Docker volumes (`oetwebsite_oet_learner_storage`, `oetwebsite_oet_postgres_data`, `oetwebsite_oet_db_backups`, `oetwebsite_oet_clamav_data`).
- **AI Gateway & Governance**: Centralized server-side gateway (`IAiGatewayService`) with strict rulebook grounding, credit reservation lifecycles, circuit breakers, and immutable single-turn audit logging (`AiUsageRecord`).
- **Domain Decoupling**: Modular multi-exam abstraction layer (`IExamScoringStrategy`, `IExamSessionDriver`, `ExamType`, `TaskType`) supporting OET, IELTS, PTE, and TOEFL without altering existing OET assets.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            CLIENT APPLICATIONS                              │
│   (Candidate Web App, Admin Management Portal, Mobile Capacitor, Desktop)   │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │ HTTP / WebSockets
                                       ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                       ASP.NET CORE MINIMAL API (.NET 10)                    │
├───────────────────┬───────────────────┬───────────────────┬─────────────────┤
│   Exam Engines    │  Scoring & SoR    │   Billing/Credits │  Admin Ingest   │
│ - Reading 20/6/16 │ - 0-500 Scale     │ - Shared Ledger   │ - Chunk Uploads │
│ - Listening 24/6  │ - 30/42=350 Anchor│ - Flex W/S Pool   │ - Zero-Dev Gate │
│ - Writing 45m     │ - 6/9 Rubrics     │ - Course Gifts    │ - ZIP Staging   │
│ - Speaking 2-Card │ - SoR Generator   │ - Gating Filter   │ - Asset Binding │
└─────────┬─────────┴─────────┬─────────┴─────────┬─────────┴────────┬────────┘
          │                   │                   │                  │
          ▼                   ▼                   ▼                  ▼
┌───────────────────┐ ┌───────────────────┐ ┌───────────────────┐ ┌──────────┐
│  AI Gateway &     │ │ Strategy Pattern  │ │ EF Core & Postgre │ │ Docker   │
│  Usage Audit Logs │ │ Multi-Exam Engine │ │ Concurrent DB     │ │ Storage  │
│  (Claude/GPT/STT) │ │ (OET/IELTS/PTE)   │ │ (Users/Attempts)  │ │ Volumes  │
└───────────────────┘ └───────────────────┘ └───────────────────┘ └──────────┘
```

---

## Feature Inventory

Every feature identified in the requirements and survey phases is inventoried below with its assigned milestone:

| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Reading 42-Item Sub-Test Engine | Official 20/6/16=42 structure, Part A 15-min lock, Part B/C break system, PDF-first split view, strikethrough annotations, debounced autosave, and offline answer reconciliation. | M1 | ORIGINAL_REQUEST §R1 |
| 2 | Listening 42-Item Sub-Test Engine | 24/6/12=42 structure, 10-phase sub-section sequencing, strict one-play exam mode, audio integrity monitoring, blocked seeking/pause, and audio chunk pre-buffering. | M1 | ORIGINAL_REQUEST §R1 |
| 3 | Writing 45-Min Sub-Test Engine | Clinical case-notes stimulus viewer, 5-min locked reading window + 40-min writing phase, Tiptap editor, live word counter (180–200 target), and draft/highlight autosave. | M1 | ORIGINAL_REQUEST §R1 |
| 4 | Speaking 2-Card Sub-Test Engine | 2 clinical role-play cards, 3-min prep countdown + 5-min active consultation, WebRTC/WebSocket audio streaming with Whisper STT, and dual-track local recording safety fallback. | M1 | ORIGINAL_REQUEST §R1 |
| 5 | 4-Skill Unified Mock Orchestrator | Seamless cross-subtest mock session coordinator (Reading -> Listening -> Writing -> Speaking), aggregate session persistence, and unified timer governance. | M1 | ORIGINAL_REQUEST §R1, R4 |
| 6 | Objective Server-Authoritative Scoring | Deterministic 0–500 scale calculation, canonical 30/42=350 (Grade B) pass benchmark, database-governed 43-row snapshot tables, and item-level evaluation. | M2 | ORIGINAL_REQUEST §R2 |
| 7 | Subjective Rubric Grading & Destination Policy | Writing 6 criteria (max 38 raw $\equiv 500$), Speaking 9 criteria (max 39 raw $\equiv 500$), mandatory destination country resolution (UK/IE/AU/NZ/CA vs US/QA), and 12-profession calibration profiles. | M2 | ORIGINAL_REQUEST §R2 |
| 8 | Statement of Results & Predictive Analytics | Pixel-faithful CBLA Statement of Results card with SVG band chart, dashed threshold dividers, test details, legal practice disclaimers, and weighted moving average trend analytics. | M2 | ORIGINAL_REQUEST §R2 |
| 9 | Centralized AI Gateway & Audit Telemetry | Centralized `IAiGatewayService`, rulebook grounding, credit reservation, SHA-256 fingerprinting, circuit breakers, exactly one `AiUsageRecord` per turn, and kill-switches. | M2 | ORIGINAL_REQUEST §R5 |
| 10 | Persistent Volume Storage Infrastructure | Abstracted `IFileStorage` (`LocalFileStorage`, `S3CompatibleFileStorage`), persistent named Docker volumes (`oetwebsite_oet_*`), and deletion-protected host wrappers. | M2 | ORIGINAL_REQUEST §R5 |
| 11 | Multi-Exam Extensible Strategy Pattern | Modular `IExamScoringStrategy` & `IExamSessionDriver` strategy-pattern abstractions supporting OET, IELTS, PTE, and TOEFL without modifying database schemas or breaking existing OET assets. | M3 | ORIGINAL_REQUEST §R3 |
| 12 | Candidate Hub & Entitlement Enforcement | Candidate dashboard, attempt history, progress analytics, Master Catalogue universal Shared Credits (R1/L1/W2/S2) vs Flexible W/S pool, 5-credit course gift, and `CandidateVisible` gating. | M3 | ORIGINAL_REQUEST §R4 |
| 13 | Admin Content Management & Zero-Deviation Ingestion | Admin paper management, chunked uploads (`/v1/admin/uploads`), bulk ZIP import, Reading Zero-Deviation 20/6/16 contract validation, and fail-closed publish gates. | M3 | ORIGINAL_REQUEST §R4 |
| 14 | E2E Requirement-Driven Test Suite (Tiers 1–4) | Comprehensive opaque-box test suite covering feature tests, boundary/corner cases, pairwise cross-feature tests, and full real-world 4-skill application scenarios. | M-E2E | ORIGINAL_REQUEST §Verification |
| 15 | E2E Test Pass & Adversarial Hardening (Tiers 1–5) | Phase 1: 100% pass verification of all Tier 1–4 E2E tests; Phase 2: Tier 5 Challenger-driven adversarial coverage hardening and white-box gap elimination. | M4 | ORIGINAL_REQUEST §Verification |

---

## Milestones

| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| **M1** | 4-Skill Exam & Practice Engine Hardening | Features 1, 2, 3, 4, 5: Complete Reading (20/6/16), Listening (24/6/12 audio pre-cache), Writing (45m case notes), Speaking (dual-track recording), and unified 4-skill mock coordinator. | None | DONE |
| **M2** | Server-Authoritative Scoring, Analytics & AI Gateway | Features 6, 7, 8, 9, 10: Deterministic 0–500 scale (30/42=350), 6/9-criteria rubrics, 12-profession calibration, Statement of Results card, AI Gateway audit records, and storage persistence. | None | DONE |
| **M3** | Multi-Exam Architecture, Candidate Hub & Admin Governance | Features 11, 12, 13: `IExamScoringStrategy` & `IExamSessionDriver` extensible microkernel, Master Catalogue credit ledger (Shared vs Flexible), admin chunked upload & Zero-Deviation publish gates. | M1, M2 | DONE |
| **M-E2E** | E2E Testing Track (Requirement-Driven) | Feature 14: Parallel opaque-box test infrastructure and comprehensive test suite across Tiers 1–4 (Feature, Boundary, Combinatorial, Real-World Workload), publishing `TEST_READY.md`. | None | DONE |
| **M4** | Final Milestone: 100% E2E Pass & Adversarial Coverage Hardening | Feature 15: Phase 1: 100% pass across all Tier 1–4 E2E tests; Phase 2: Tier 5 Challenger-driven adversarial stress testing and coverage hardening. | M1, M2, M3, M-E2E | DONE |

---

## Interface Contracts

### 1. Multi-Exam Scoring Strategy Contract (`IExamScoringStrategy`)
```csharp
public interface IExamScoringStrategy
{
    string ExamTypeCode { get; }
    ExamScoreResult CalculateScore(string subtestCode, int rawScore, int maxRawScore, string? countryCode = null);
    IReadOnlyList<GradeBandDefinition> GetGradeBands(string subtestCode);
    bool IsPass(string subtestCode, int scaledScore, string? countryCode = null);
    string FormatScoreDisplay(string subtestCode, int scaledScore, string? gradeLetter = null);
}
```

### 2. Objective & Subjective Scoring Contracts
- **Reading/Listening Scaled Mapping**:
  $$\text{ScaledScore}(r) = \begin{cases} 0 & r = 0 \\ \operatorname{round}\left(\frac{r \times 350}{30}\right) & 0 < r < 30 \\ 350 & r = 30 \\ 350 + \operatorname{round}\left(\frac{(r - 30) \times 150}{12}\right) & 30 < r < 42 \\ 500 & r = 42 \end{cases}$$
- **Writing Country Resolution Contract**:
  - `GB`, `IE`, `AU`, `NZ`, `CA` $\rightarrow$ Pass requires $\ge 350$ (Grade B).
  - `US`, `QA` $\rightarrow$ Pass requires $\ge 300$ (Grade C+).
  - Null/Unspecified $\rightarrow$ `passed: null, reason: 'country_required'`.

### 3. Centralized AI Gateway Contract (`IAiGatewayService`)
```csharp
public interface IAiGatewayService
{
    Task<AiCompletionResult> ExecuteGroundedCompletionAsync(
        AiCompletionRequest request,
        AiGroundingContext groundingContext,
        CancellationToken cancellationToken = default);
}
```
- Every provider execution MUST write an `AiUsageRecord` row with `RequestHash`, `TokenCounts`, `CostEstimateUsd`, `Provider`, and `Model`.

### 4. Statement of Results Adapter Contract (`lib/adapters/oet-sor-adapter.ts`)
```typescript
export interface OetStatementOfResults {
  candidate: OetSorCandidateInfo;
  testDetails: OetSorTestDetails;
  results: Record<OetSubtestCode, OetSorSubtestScore>;
  overallStatus: 'PASS' | 'FAIL' | 'CONDITIONAL';
  governanceHash: string;
  isPracticeNotice: boolean;
}
```

---

## Code Layout

- **Frontend Routes**: `app/**/page.tsx` (App Router, Server Component default).
- **Frontend Components**: `components/domain/**` (Domain specific), `components/ui/**` (Base primitives), `components/admin/**` (Admin operational).
- **Frontend Core Logic**: `lib/**` (`scoring.ts`, `api.ts`, `rulebook/**`, `adapters/**`, `exam-family-scoring.ts`).
- **Backend Endpoints**: `backend/src/OetLearner.Api/Endpoints/**` (Minimal API endpoints).
- **Backend Domain Entities**: `backend/src/OetLearner.Api/Domain/**` (EF Core entity models).
- **Backend Services**: `backend/src/OetLearner.Api/Services/**` (Scoring, AI Gateway, Billing, Content, Evaluation).
- **Backend Data Context**: `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`.
- **E2E & Unit Tests**: `tests/**`, `backend/tests/**`, Vitest and xUnit.
