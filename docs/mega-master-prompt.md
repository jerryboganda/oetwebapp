# MEGA MASTER PROMPT: World-Class English Language Exam Preparation Platform

> **Version:** 1.0 | **Date:** 2026-04-03 | **Stack:** ASP.NET Core 10 + Next.js 15 App Router
> **Scope:** Transform the existing OET preparation platform into the world's most comprehensive, immersive, AI-powered English language exam preparation system -- starting with OET, expanding to IELTS, PTE, Cambridge, and TOEFL.

---

## TABLE OF CONTENTS

- [Section 0: Identity & Operating Rules](#section-0-identity--operating-rules)
- [Section 1: Business Vision & Product Identity](#section-1-business-vision--product-identity)
- [Section 2: Multi-Exam Architecture](#section-2-multi-exam-architecture)
- [Section 3: Phase 1 -- Foundation Enhancements](#section-3-phase-1--foundation-enhancements)
- [Section 4: Phase 2 -- AI-First Features](#section-4-phase-2--ai-first-features)
- [Section 5: Phase 3 -- Complete Ecosystem](#section-5-phase-3--complete-ecosystem)
- [Section 6: Phase 4 -- Scale & Polish](#section-6-phase-4--scale--polish)
- [Section 7: Data Model Reference](#section-7-data-model-reference)
- [Section 8: API Endpoint Reference](#section-8-api-endpoint-reference)
- [Section 9: Frontend Route Map](#section-9-frontend-route-map)
- [Section 10: Background Job Extensions](#section-10-background-job-extensions)
- [Section 11: Quality & Testing Requirements](#section-11-quality--testing-requirements)
- [Section 12: Migration & Deployment Sequence](#section-12-migration--deployment-sequence)
- [Section 13: Integration Architecture](#section-13-integration-architecture)
- [Section 14: Business Rules & Entitlements](#section-14-business-rules--entitlements)
- [Section 15: Incremental Implementation Protocol](#section-15-incremental-implementation-protocol)

---

## SECTION 0: IDENTITY & OPERATING RULES

### 0.1 Who You Are

You are an expert full-stack implementation agent working on the **OET Prep Platform** -- a production-grade, multi-exam English language preparation system. Your job is to implement features incrementally, correctly, and in harmony with the existing codebase. You are not starting from scratch -- you are enhancing a live product that already has 77+ frontend pages, 60+ API endpoints, 70+ database entities, and 27 backend services.

### 0.2 Codebase Conventions -- MANDATORY

Before writing ANY code, you MUST read the relevant existing files to understand patterns. New code must follow existing conventions exactly. Deviating from established patterns is a defect.

**Backend Conventions (ASP.NET Core 10):**

| Area | Convention | Reference File |
|------|-----------|----------------|
| Entities | String PKs `[MaxLength(64)]` with manual IDs, `[MaxLength(N)]` on all strings, `*Json` string columns for flexible data, `DateTimeOffset` for timestamps | `backend/src/OetLearner.Api/Domain/Entities.cs` |
| Enums | State machines as C# enums in `Enums.cs` | `backend/src/OetLearner.Api/Domain/Enums.cs` |
| DbContext | Register `DbSet<T>`, configure indexes in `OnModelCreating` | `backend/src/OetLearner.Api/Data/LearnerDbContext.cs` |
| Services | Scoped, injected via constructor, methods named `Get*Async`, `Create*Async`, `Update*Async`, `Submit*Async` | `backend/src/OetLearner.Api/Services/LearnerService.cs` |
| Endpoints | Minimal API route groups, `RequireAuthorization`, `RequireRateLimiting`, return anonymous objects | `backend/src/OetLearner.Api/Program.cs` |
| Contracts | Request/response DTOs in `Contracts/` | `backend/src/OetLearner.Api/Contracts/` |
| Errors | `ApiException` with `ErrorCode`, `FieldErrors`, `Retryable` | `backend/src/OetLearner.Api/Services/LearnerService.cs` |
| Seed Data | Reference data + demo data in `SeedData.cs` | `backend/src/OetLearner.Api/Services/SeedData.cs` |
| Background Jobs | `BackgroundJobItem` with `JobType` enum, processed by `BackgroundJobProcessor` | `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs` |
| Configuration | Options pattern classes in `Configuration/` | `backend/src/OetLearner.Api/Configuration/` |

**Frontend Conventions (Next.js 15 App Router):**

| Area | Convention | Reference File |
|------|-----------|----------------|
| Pages | `app/{feature}/page.tsx` with `"use client"` where needed | `app/speaking/task/[id]/page.tsx` |
| API Client | Functions in `lib/api.ts`, use `ApiError` class | `lib/api.ts` |
| Types | TypeScript interfaces in `lib/types/{feature}.ts` | `lib/types/expert.ts` |
| Stores | Zustand stores in `lib/stores/` | `lib/stores/expert-store.ts` |
| Hooks | Custom hooks in `lib/hooks/` or `hooks/` | `lib/hooks/use-dashboard-home.ts` |
| Domain Components | `components/domain/` | `components/domain/` |
| UI Primitives | `components/ui/` (button, card, badge, modal, tabs, etc.) | `components/ui/` |
| Layout | App shell, sidebar, top-nav in `components/layout/` | `components/layout/app-shell.tsx` |
| Auth | Context in `contexts/auth-context.tsx` | `contexts/auth-context.tsx` |
| Notifications | SignalR context in `contexts/notification-center-context.tsx` | `contexts/notification-center-context.tsx` |
| Styling | Tailwind CSS 4, utility-first, responsive | `app/globals.css` |
| Analytics | Event tracking via `lib/analytics.ts` | `lib/analytics.ts` |

### 0.3 Source of Truth Hierarchy

1. **Existing codebase patterns** -- always match what exists
2. **This master prompt** -- the feature specification
3. **Existing documentation** in `docs/` -- supplementary context

### 0.4 Validation Gates

After implementing ANY feature, run:

```bash
# Frontend
npm run lint
npm test
npm run build

# Backend
dotnet build backend/OetLearner.sln
dotnet test backend/OetLearner.sln
```

Do NOT proceed to the next feature until all gates pass.

### 0.5 Feature Flag Discipline

**Every new feature** must ship behind a feature flag using the existing `FeatureFlag` entity (types: `Release`, `Experiment`, `Operational`). Add seed data for each flag in `SeedData.cs`. The admin panel at `/admin/flags` controls toggles. Frontend checks flags via the bootstrap API. Backend checks flags in service methods.

### 0.6 Non-Negotiable Rules

1. Never reinvent existing patterns. Read first, code second.
2. Never drop columns or tables. Migrations are additive only.
3. Never hardcode exam-specific logic. Use the ExamType abstraction.
4. Never skip feature flags. Even for "obvious" features.
5. Never commit code without passing lint, tests, and build.
6. Never create a frontend page without a corresponding backend endpoint.
7. Never bypass the existing auth/authorization system.
8. Keep the professional, clinical tone. No childish UI elements.

---

## SECTION 1: BUSINESS VISION & PRODUCT IDENTITY

### 1.1 Mission

**Be the definitive AI-powered preparation platform for English language exams, starting with OET, expanding to IELTS, PTE, Cambridge, and TOEFL.**

The platform must deliver a complete closed-loop learning experience: **Diagnose** baseline ability -> **Prescribe** a personalized study plan -> **Practice** with adaptive tasks -> **Evaluate** with AI and human experts -> **Review** mistakes with spaced repetition -> **Predict** exam readiness -> **Certify** achievement.

### 1.2 Core Value Proposition

A student who subscribes should feel:
- "I don't need anything else to prepare for my exam."
- "The platform knows exactly what I'm weak at and targets those areas."
- "I'm actually getting better, and I can see the evidence."
- "The AI practice feels like a real exam environment."
- "Expert feedback is specific, actionable, and fast."
- "I'm engaged daily and motivated to keep my streak going."

### 1.3 Product Principles

| Principle | What It Means |
|-----------|---------------|
| **OET-native, exam-expandable** | OET is the gold standard. Every OET criterion, profession, and task type is first-class. Other exams (IELTS, PTE) share the infrastructure but have their own task types, scoring, and criteria. |
| **Practice-first, not content-library** | The platform is for DOING, not browsing. Every page leads to active practice within 2 clicks. |
| **AI-immersive** | AI is not a gimmick. AI conversation partners, AI writing coaches, AI pronunciation trainers, AI-generated content, AI adaptive difficulty -- all core to the experience. |
| **Trust-first** | No fake scores. AI evaluation includes confidence bands. Predictions include disclaimers. Expert reviews are calibrated. |
| **Time-poor-user friendly** | Study plans respect schedules. Sessions have time estimates. Quick-practice options exist for 10-minute study windows. |
| **Professional tone** | Clean, clinical, modern. No cartoon characters, no excessive emojis, no gamification that feels juvenile. Subtle progress indicators, milestone celebrations, and professional achievement badges. |
| **Data-driven personalization** | Every interaction feeds the skill profile. Every evaluation updates the adaptive engine. Every study plan is unique. |

### 1.4 Business Model

#### Subscription Tiers

| Tier | Price (AUD/month) | Target User | Revenue Stream |
|------|-------------------|-------------|----------------|
| **Free** | $0 | Trial users, window shoppers | Conversion funnel |
| **Standard** | $29 | Self-study learners | Core subscription |
| **Premium** | $59 | Serious candidates, retakers | Premium subscription |
| **Institutional** | Custom (per-seat) | Nursing schools, hospitals, language centers | Enterprise contracts |

#### Additional Revenue Streams

| Stream | Model |
|--------|-------|
| Expert review credits | Pay-per-review (standard: $15, express: $25) |
| Live tutoring sessions | Per-session booking ($40-80/session) |
| Intensive courses | Time-limited course packages ($99-299) |
| Exam booking commission | Referral fee from OET/IELTS booking partners |
| Content marketplace | Revenue share with educator content contributors |
| Referral program | $10 credit per successful referral (both parties) |
| Certificates | Premium achievement certificates ($5-15) |

### 1.5 Target Exams (Priority Order)

| Priority | Exam | Full Name | Key Differentiator |
|----------|------|-----------|-------------------|
| 1 (Primary) | **OET** | Occupational English Test | Healthcare-specific, profession-based, criterion-referenced |
| 2 | **IELTS** | International English Language Testing System | General/Academic, band scoring (0-9), most widely accepted |
| 3 | **PTE** | Pearson Test of English Academic | Computer-scored, integrated skills, fast results |
| 4 | **Cambridge** | Cambridge English Qualifications (B2 First, C1 Advanced, C2 Proficiency) | Level-based certifications, long-form |
| 5 | **TOEFL** | Test of English as a Foreign Language | US-focused, iBT format, integrated tasks |

### 1.6 OET Profession Support

Already existing: Nursing, Medicine, Dentistry, Pharmacy, Physiotherapy, Academic English.

**Expand to include ALL 12 OET professions:**

| Existing | New (Add) |
|----------|-----------|
| Nursing | Veterinary Science |
| Medicine | Optometry |
| Dentistry | Podiatry |
| Pharmacy | Occupational Therapy |
| Physiotherapy | Speech Pathology |
| Academic English | Radiography |

### 1.7 OET Subtest Deep Dive (What "World-Class" Means)

**Writing (OET):**
- Referral letters, discharge summaries, transfer letters, case notes
- All 12 professions have profession-specific scenarios
- Model answers for every task
- Criterion-by-criterion feedback (Purpose, Content, Conciseness & Clarity, Genre & Style, Organization, Language)
- Revision workflow with diff comparison
- AI writing coach with real-time suggestions
- Expert human review with anchored comments

**Speaking (OET):**
- Roleplay scenarios with AI patient/relative/professional
- All 12 professions have profession-specific scenarios
- 5-minute roleplay cards with warm-up period
- Real-time AI transcription and evaluation
- Pronunciation analysis at phoneme level
- Phrasing improvement drills
- Expert human review of audio + transcript
- Interlocutor audio playback for preparation

**Reading (OET):**
- Part A: Expeditious reading (skimming and scanning) -- 4 medical texts on one clinical topic (variable length; Text C may include large tables/graphs), 20 items (matching / short-answer / sentence-completion)
- Part B: Careful reading -- 6 short extracts from different healthcare contexts (policies, notices, guidelines, clinical communications), 3-option MCQ
- Part C: Careful reading -- 2 long healthcare articles, 8 items each (4-option MCQ)
- Timed practice matching real exam conditions
- Strategy-specific drills (detail extraction, inference, vocabulary in context)
- Answer analysis with explanation for every option

**Listening (OET):**
- Part A: Consultation extracts (note completion)
- Part B: Workplace extracts (multiple choice)
- Part C: Presentation extracts (multiple choice)
- Audio with healthcare-specific accents (Australian, British, American, Indian, South African)
- Distractor identification drills
- Transcript review with audio sync
- Playback speed control for difficulty adjustment

---

## SECTION 2: MULTI-EXAM ARCHITECTURE

### 2.1 The ExamType Abstraction Layer

**Key Discovery:** Multi-exam scaffolding already exists in the database. The `SignupExamTypeCatalog` table has both "oet" and "ielts" entries. `LearnerRegistrationProfile.ExamTypeId` stores the chosen exam. `SignupProfessionCatalog.ExamTypeIdsJson` links professions to exam types.

**Gap:** Core content and learning tables (`ContentItem`, `Attempt`, `Evaluation`, `StudyPlan`, `DiagnosticSession`, `MockAttempt`, `LearnerGoal`, `CriterionReference`) have no `ExamTypeCode` field. All data is implicitly OET-only.

#### New Entity: ExamType (Reference Table)

```csharp
public class ExamType
{
    [Key]
    [MaxLength(16)]
    public string Code { get; set; } = default!;            // "oet", "ielts", "pte", "cambridge", "toefl"

    [MaxLength(128)]
    public string Label { get; set; } = default!;            // "OET - Occupational English Test"

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    public string SubtestDefinitionsJson { get; set; } = "[]";  // Defines subtests for this exam
    public string ScoringSystemJson { get; set; } = "{}";       // Scoring ranges, bands, grades
    public string TimingsJson { get; set; } = "{}";             // Per-subtest time limits
    public string ProfessionIdsJson { get; set; } = "[]";       // Applicable professions (empty = all)

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public int SortOrder { get; set; }
}
```

#### New Entity: TaskType (Content Taxonomy)

```csharp
public class TaskType
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;               // "oet-writing-referral", "ielts-writing-task2"

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;             // "Referral Letter", "Task 2 Essay"

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    public string ConfigJson { get; set; } = "{}";            // Word limits, time limits, format rules
    public string CriteriaIdsJson { get; set; } = "[]";       // Evaluation criteria for this task type

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public int SortOrder { get; set; }
}
```

### 2.2 Scoring Systems by Exam

```json
{
  "oet": {
    "type": "criterion_referenced",
    "subtestScoreRange": { "min": 0, "max": 500 },
    "gradeMapping": {
      "A": { "min": 450, "max": 500, "description": "High proficiency" },
      "B": { "min": 350, "max": 449, "description": "Adequate proficiency" },
      "C+": { "min": 300, "max": 349, "description": "Developing proficiency" },
      "C": { "min": 200, "max": 299, "description": "Limited proficiency" },
      "D": { "min": 100, "max": 199, "description": "Minimal proficiency" },
      "E": { "min": 0, "max": 99, "description": "Insufficient proficiency" }
    },
    "passMark": 350,
    "criterionScaleMax": 6
  },
  "ielts": {
    "type": "band_score",
    "subtestScoreRange": { "min": 0, "max": 9, "step": 0.5 },
    "overallFormula": "average_rounded_to_nearest_half",
    "bandDescriptors": {
      "9": "Expert user",
      "8": "Very good user",
      "7": "Good user",
      "6": "Competent user",
      "5": "Modest user",
      "4": "Limited user"
    }
  },
  "pte": {
    "type": "continuous_score",
    "subtestScoreRange": { "min": 10, "max": 90 },
    "enabledSkills": ["communicative_skills", "enabling_skills"],
    "overallFormula": "weighted_composite"
  }
}
```

### 2.3 Task Type Taxonomy

| Exam | Subtest | Task Types |
|------|---------|-----------|
| **OET** | Writing | Referral Letter, Discharge Summary, Transfer Letter, Case Notes Summary |
| **OET** | Speaking | Roleplay (12 profession-specific scenarios per set) |
| **OET** | Reading | Part A (Expeditious), Part B (Careful Reading) |
| **OET** | Listening | Part A (Consultation), Part B (Workplace), Part C (Presentation) |
| **IELTS Academic** | Writing | Task 1 (Graph/Chart/Diagram Description), Task 2 (Essay) |
| **IELTS General** | Writing | Task 1 (Letter), Task 2 (Essay) |
| **IELTS** | Speaking | Part 1 (Introduction & Interview), Part 2 (Long Turn/Cue Card), Part 3 (Discussion) |
| **IELTS** | Reading | Section 1-3 (MCQ, TFNG, Matching, Completion, Heading Match) |
| **IELTS** | Listening | Section 1 (Conversation), Section 2 (Monologue), Section 3 (Discussion), Section 4 (Lecture) |
| **PTE** | Speaking & Writing | Read Aloud, Repeat Sentence, Describe Image, Re-tell Lecture, Answer Short Question, Summarize Written Text, Write Essay |
| **PTE** | Reading | MCQ (Single/Multiple), Re-order Paragraphs, Fill in Blanks (Reading/R&W) |
| **PTE** | Listening | Summarize Spoken Text, MCQ, Fill in Blanks, Highlight Correct Summary, Select Missing Word, Highlight Incorrect Words, Write from Dictation |

### 2.4 Schema Migration Strategy

**Phase 1 (Non-breaking):** Add `ExamTypeCode` column with default `"oet"` to:
- `ContentItem`
- `Attempt`
- `Evaluation`
- `StudyPlan`
- `DiagnosticSession`
- `MockAttempt`
- `LearnerGoal`
- `CriterionReference`
- `ReviewRequest`

Add `ActiveExamTypeCode` to `LearnerUser` (default `"oet"`).

**Phase 2:** Add `ExamType` and `TaskType` reference tables. Seed OET and IELTS data.

**Phase 3:** Add exam type switcher in learner settings. Filter all queries by `ActiveExamTypeCode`.

**Phase 4:** Add IELTS-specific content, criteria, and evaluation logic.

**Phase 5:** Add PTE and Cambridge support.

### 2.5 Frontend Exam Switching

Add an exam type selector to:
1. **Onboarding flow** -- step 1 asks "Which exam are you preparing for?"
2. **Settings > Goals** -- change active exam type
3. **Top navigation** -- subtle exam badge showing current exam (clickable to switch)

When exam type changes:
- Dashboard data refreshes for new exam type
- Study plan regenerates
- Content filters update
- Criteria reference updates
- Scoring display format changes (OET: 0-500 score, IELTS: 0-9 band, PTE: 10-90 score)

### 2.6 Shared vs. Exam-Specific Features

| Feature | Shared (all exams) | OET-Specific | IELTS-Specific | PTE-Specific |
|---------|--------------------|-------------|----------------|-------------|
| Study plan engine | Yes | | | |
| Adaptive difficulty | Yes | | | |
| Spaced repetition | Yes | | | |
| Gamification | Yes | | | |
| Vocabulary builder | Yes | Medical terminology focus | Academic word list (AWL) | Academic + colloquial |
| Writing evaluation criteria | | Purpose, Content, Conciseness, Genre, Organization, Language | Task Response, Coherence, Lexical Resource, Grammar | Content, Form, Grammar, Vocabulary, Spelling |
| Speaking evaluation criteria | | Intelligibility, Fluency, Appropriateness, Grammar, Clinical Communication | Fluency, Lexical Resource, Grammar, Pronunciation | Content, Pronunciation, Oral Fluency |
| Reading task types | | Part A/B (healthcare texts) | TFNG, Matching, Heading Match | Re-order, Fill in Blanks |
| Listening task types | | Consultation/Workplace/Presentation | 4 sections (social to academic) | 8 task types (integrated) |
| Profession support | | 12 healthcare professions | N/A (general/academic) | N/A |
| AI conversation partner | | Profession-specific roleplay | 3-part interview simulation | Describe Image, Re-tell Lecture |

---

## SECTION 3: PHASE 1 -- FOUNDATION ENHANCEMENTS

> **Timeline:** Weeks 1-6
> **Goal:** Strengthen the existing OET platform while laying the multi-exam foundation.

### 3.1 Multi-Exam Database Foundation

**Priority: FIRST -- Do this before all other Phase 1 features.**

**Backend Changes:**

1. Create new entity files:
   - `ExamType` in `Domain/ExamTypeEntities.cs`
   - `TaskType` in `Domain/ExamTypeEntities.cs`

2. Add `ExamTypeCode` column to existing entities:
   ```csharp
   // Add to ContentItem, Attempt, Evaluation, StudyPlan, DiagnosticSession,
   // MockAttempt, LearnerGoal, CriterionReference
   [MaxLength(16)]
   public string ExamTypeCode { get; set; } = "oet";
   ```

3. Add `ActiveExamTypeCode` to `LearnerUser`:
   ```csharp
   [MaxLength(16)]
   public string ActiveExamTypeCode { get; set; } = "oet";
   ```

4. Register `DbSet<ExamType>` and `DbSet<TaskType>` in `LearnerDbContext`.

5. Add seed data for OET and IELTS exam types in `SeedData.cs`.

6. Add indexes on `ExamTypeCode` columns.

7. Update ALL service queries to filter by `ExamTypeCode` where applicable.

**Frontend Changes:**

1. Add exam type to bootstrap response.
2. Add exam switcher component.
3. Update dashboard and practice hubs to filter by active exam type.

### 3.2 Adaptive Difficulty Engine

**Purpose:** Automatically adjust practice task difficulty based on learner performance. No more random task selection.

**New Entities:**

```csharp
public class LearnerSkillProfile
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(32)]
    public string CriterionCode { get; set; } = default!;

    public double CurrentRating { get; set; } = 1500;      // Elo-like rating
    public int ConfidenceLevel { get; set; } = 0;           // 0-100, increases with more data
    public int EvidenceCount { get; set; }
    public string RecentScoresJson { get; set; } = "[]";    // Last 20 scores for trend
    public DateTimeOffset LastUpdatedAt { get; set; }
}
```

**Algorithm (Elo-like):**
- Each content item has a difficulty rating (easy=1200, medium=1500, hard=1800, expert=2100)
- After each evaluation:
  - K-factor = 32 (high for new learners, decreasing with evidence count)
  - Expected score = 1 / (1 + 10^((difficulty - currentRating) / 400))
  - Actual score = normalized evaluation score (0.0 to 1.0)
  - New rating = currentRating + K * (actualScore - expectedScore)
- Content recommendation: select tasks where `abs(contentDifficulty - learnerRating) < 200`

**New Service: `AdaptiveDifficultyService`**

Methods:
- `UpdateSkillProfileAsync(userId, evaluationId)` -- called after every evaluation
- `GetRecommendedContentAsync(userId, examTypeCode, subtestCode, count)` -- returns content matched to skill level
- `GetSkillProfileAsync(userId, examTypeCode)` -- returns full skill profile for display

**New Endpoints:**
- `GET /v1/content/recommended?examType={code}&subtest={code}&count={n}` -- LearnerOnly
- `GET /v1/skill-profile` -- LearnerOnly, returns per-subtest-per-criterion ratings

**Frontend:**
- Add difficulty indicator badge to `TaskCard` component (color-coded: green/amber/red/purple)
- Add "Recommended for You" section to each subtest home page
- Add skill profile visualization to progress page (radar chart per subtest)

### 3.3 Spaced Repetition System (Mistake Review)

**Purpose:** Systematically resurface mistakes and weak areas using scientifically-proven spaced repetition (SM-2 algorithm).

**New Entities:**

```csharp
public class ReviewItem
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SourceType { get; set; } = default!;      // "evaluation_issue", "vocabulary", "grammar_error", "pronunciation"

    [MaxLength(64)]
    public string? SourceId { get; set; }                    // Reference to originating entity

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(32)]
    public string? CriterionCode { get; set; }

    public string QuestionJson { get; set; } = "{}";         // What to show (the mistake or concept)
    public string AnswerJson { get; set; } = "{}";           // Correct version + explanation

    public double EaseFactor { get; set; } = 2.5;            // SM-2 ease factor
    public int IntervalDays { get; set; } = 1;               // Current interval
    public int ReviewCount { get; set; }
    public int ConsecutiveCorrect { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";           // "active", "mastered", "suspended"
}
```

**SM-2 Algorithm Implementation:**
```
After each review, learner rates difficulty: 0=forgot, 1=hard, 2=hesitant, 3=correct, 4=easy, 5=trivial

If quality >= 3 (correct):
  if reviewCount == 0: interval = 1
  elif reviewCount == 1: interval = 6
  else: interval = round(interval * easeFactor)
  easeFactor = max(1.3, easeFactor + (0.1 - (5-quality) * (0.08 + (5-quality) * 0.02)))

If quality < 3 (incorrect):
  interval = 1
  reviewCount = 0 (reset)
  // easeFactor unchanged

If consecutiveCorrect >= 5 and easeFactor >= 2.5: mark as "mastered"
```

**Auto-creation triggers:**
- When `WritingEvaluation` has issues in `IssuesJson` -> create ReviewItem per issue
- When `SpeakingEvaluation` has pronunciation issues -> create ReviewItem
- When vocabulary quiz answer is wrong -> create ReviewItem
- When grammar exercise is failed -> create ReviewItem

**New Endpoints:**
- `GET /v1/review-items/due?limit={n}` -- get due review items
- `GET /v1/review-items/stats` -- review stats (due today, overdue, mastered total)
- `POST /v1/review-items/{id}/review` -- submit review response with quality rating

**Frontend:**
- `app/review/page.tsx` -- Review hub showing due count, stats, "Start Review" button
- `app/review/session/page.tsx` -- Card-based review interface:
  - Shows the question/mistake
  - Flip to reveal correct answer + explanation
  - Rate difficulty (Again / Hard / Good / Easy)
  - Progress bar showing remaining items
  - Session summary at end
- Add "Due for Review" badge to dashboard sidebar
- Add review items count to study plan daily summary

### 3.4 Gamification System

**Purpose:** Drive daily engagement and long-term retention through professional, non-childish motivation mechanics.

**New Entities:**

```csharp
public class LearnerStreak
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateOnly LastActiveDate { get; set; }
    public int StreakFreezeCount { get; set; } = 1;          // Free freezes available
    public int StreakFreezeUsedCount { get; set; }
    public DateOnly? LastFreezeUsedDate { get; set; }
}

public class LearnerXP
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public long TotalXP { get; set; }
    public long WeeklyXP { get; set; }
    public long MonthlyXP { get; set; }
    public int Level { get; set; } = 1;
    public DateOnly WeekStartDate { get; set; }
    public DateOnly MonthStartDate { get; set; }
}

public class Achievement
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    [MaxLength(32)]
    public string Category { get; set; } = default!;         // "practice", "streak", "milestone", "mastery", "social"

    [MaxLength(256)]
    public string? IconUrl { get; set; }

    public int XPReward { get; set; }
    public string CriteriaJson { get; set; } = "{}";         // Unlock conditions
    public int SortOrder { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

public class LearnerAchievement
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string AchievementId { get; set; } = default!;

    public DateTimeOffset UnlockedAt { get; set; }
    public bool Notified { get; set; }
}

public class LeaderboardEntry
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(128)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(16)]
    public string Period { get; set; } = default!;            // "weekly", "monthly", "alltime"

    public DateOnly PeriodStart { get; set; }
    public long XP { get; set; }
    public int Rank { get; set; }
    public bool OptedIn { get; set; } = true;
}
```

**XP Award Schedule:**

| Action | XP | Notes |
|--------|-----|-------|
| Complete a practice task | 10 | Any subtest |
| Submit writing for evaluation | 25 | |
| Submit speaking for evaluation | 25 | |
| Complete a reading set | 15 | |
| Complete a listening set | 15 | |
| Complete a review session (10+ items) | 20 | Spaced repetition |
| Complete daily vocabulary quiz | 10 | |
| Complete a grammar lesson | 15 | |
| Complete a diagnostic subtest | 30 | |
| Complete a full diagnostic | 100 | |
| Complete a mock exam | 50 | |
| Maintain 7-day streak | 100 | Bonus |
| Maintain 30-day streak | 500 | Bonus |
| Maintain 100-day streak | 2000 | Bonus |
| First practice in a new subtest | 50 | One-time |
| First mock exam | 100 | One-time |
| Receive expert review | 25 | |
| Complete revision after feedback | 20 | |
| Refer a friend (converted) | 200 | |

**Level System:**
- Level = floor(sqrt(TotalXP / 100))
- Level 1: 0 XP, Level 5: 2,500 XP, Level 10: 10,000 XP, Level 20: 40,000 XP, Level 50: 250,000 XP

**Achievement Examples (seed 30+):**

| Code | Label | Category | Criteria |
|------|-------|----------|----------|
| `first_writing` | First Draft | practice | Complete 1 writing task |
| `writing_10` | Prolific Writer | practice | Complete 10 writing tasks |
| `writing_50` | Master Scribe | practice | Complete 50 writing tasks |
| `first_speaking` | Voice Activated | practice | Complete 1 speaking task |
| `streak_7` | Weekly Warrior | streak | 7-day streak |
| `streak_30` | Monthly Champion | streak | 30-day streak |
| `streak_100` | Century Streak | streak | 100-day streak |
| `streak_365` | Year of Dedication | streak | 365-day streak |
| `mock_first` | Mock Debut | milestone | Complete first mock exam |
| `mock_pass` | Pass Predicted | milestone | Mock exam score above pass mark |
| `all_subtests` | Full Spectrum | milestone | Practice in all 4 subtests |
| `vocab_100` | Word Collector | mastery | Master 100 vocabulary terms |
| `vocab_500` | Lexicon Builder | mastery | Master 500 vocabulary terms |
| `review_mastered_50` | Error Eliminator | mastery | Master 50 review items |
| `expert_review_5` | Feedback Seeker | social | Request 5 expert reviews |
| `referral_1` | Ambassador | social | Refer 1 friend |
| `level_10` | Rising Star | milestone | Reach level 10 |
| `level_25` | Seasoned Learner | milestone | Reach level 25 |
| `diagnostic_complete` | Baseline Set | milestone | Complete diagnostic |
| `readiness_high` | Exam Ready | milestone | Reach high readiness in all subtests |

**New Endpoints:**
- `GET /v1/gamification/summary` -- streak, XP, level, recent achievements
- `GET /v1/gamification/achievements` -- all achievements with unlock status
- `GET /v1/gamification/leaderboard?period={weekly|monthly}&examType={code}` -- top 50
- `POST /v1/gamification/leaderboard/opt-in` -- opt in/out of leaderboard
- `POST /v1/gamification/streak/freeze` -- use a streak freeze

**Frontend:**
- Dashboard hero: streak flame + day count, XP bar showing progress to next level
- Sidebar: persistent streak counter and level badge
- `/app/achievements/page.tsx` -- achievement gallery (earned vs locked, with progress)
- `/app/leaderboard/page.tsx` -- weekly/monthly leaderboard, opt-in toggle
- Achievement unlock toast notification (non-intrusive, bottom-right, auto-dismiss)
- Level-up celebration (subtle confetti animation, 2 seconds, professional)

### 3.5 Vocabulary Builder

**Purpose:** Build exam-relevant vocabulary with spaced repetition. OET focuses on medical terminology; IELTS on academic word list; PTE on mixed academic/colloquial.

**New Entities:**

```csharp
public class VocabularyTerm
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Term { get; set; } = default!;

    [MaxLength(1024)]
    public string Definition { get; set; } = default!;

    [MaxLength(2048)]
    public string ExampleSentence { get; set; } = default!;

    [MaxLength(1024)]
    public string? ContextNotes { get; set; }                 // Usage notes (formal, clinical, etc.)

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string? ProfessionId { get; set; }                 // null = general

    [MaxLength(32)]
    public string Category { get; set; } = default!;          // "medical", "academic", "general", "clinical_communication"

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    [MaxLength(256)]
    public string? AudioUrl { get; set; }                     // Pronunciation audio

    [MaxLength(256)]
    public string? ImageUrl { get; set; }                     // Visual aid

    public string SynonymsJson { get; set; } = "[]";
    public string CollocationsJson { get; set; } = "[]";      // Common word pairings
    public string RelatedTermsJson { get; set; } = "[]";

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

public class LearnerVocabulary
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string TermId { get; set; } = default!;

    [MaxLength(16)]
    public string Mastery { get; set; } = "new";             // "new", "learning", "reviewing", "mastered"

    public double EaseFactor { get; set; } = 2.5;
    public int IntervalDays { get; set; } = 1;
    public int ReviewCount { get; set; }
    public int CorrectCount { get; set; }
    public DateOnly? NextReviewDate { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

public class VocabularyQuizResult
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int TermsQuizzed { get; set; }
    public int CorrectCount { get; set; }
    public int DurationSeconds { get; set; }
    public string ResultsJson { get; set; } = "[]";
    public DateTimeOffset CompletedAt { get; set; }
}
```

**Seed Data (Initial Term Sets):**
- **OET Medical Terms:** 500 terms covering anatomy, symptoms, procedures, medications, clinical communication
- **OET Profession-Specific:** 100 terms per profession (nursing, medicine, etc.)
- **IELTS Academic Word List:** 570 terms from the Academic Word List (AWL) by Averil Coxhead
- **General Academic:** 200 high-frequency academic collocations

**Quiz Formats:**
1. **Definition Match** -- show term, pick correct definition from 4 options
2. **Fill the Blank** -- sentence with blank, type the correct term
3. **Synonym Match** -- match term with its synonym
4. **Context Usage** -- select the sentence that uses the term correctly
5. **Audio Recognition** -- hear the pronunciation, type the term (speaking-oriented)

**New Endpoints:**
- `GET /v1/vocabulary/terms?examType={code}&profession={id}&category={cat}&page={n}` -- browse terms
- `GET /v1/vocabulary/daily-set?count={n}` -- today's terms to learn (mix of new + due review)
- `GET /v1/vocabulary/stats` -- mastery stats (new, learning, reviewing, mastered counts)
- `POST /v1/vocabulary/{termId}/learn` -- mark a term as seen/added to learning
- `POST /v1/vocabulary/{termId}/review` -- submit review response (quality rating)
- `POST /v1/vocabulary/quiz/start` -- start a quiz session
- `POST /v1/vocabulary/quiz/{quizId}/submit` -- submit quiz answers
- `GET /v1/vocabulary/quiz/history` -- past quiz results

**Frontend:**
- `app/vocabulary/page.tsx` -- Vocabulary hub: stats overview, daily set, "Start Quiz" button, browse all terms
- `app/vocabulary/flashcards/page.tsx` -- Flashcard practice (card flip animation, swipe gestures on mobile)
- `app/vocabulary/quiz/page.tsx` -- Interactive quiz with multiple question formats
- `app/vocabulary/browse/page.tsx` -- Full term catalogue with search, filter by category/profession/mastery
- Integration with study plan: study plan items can include "Learn 10 new vocabulary terms"

### 3.6 AI-Powered Content Generation (Admin Tool)

**Purpose:** Enable admins to generate practice content using AI, dramatically increasing content volume. Generated content enters Draft status for human review.

**New Background Job Type:** Add `ContentGeneration` to `JobType` enum.

**New Entities:**

```csharp
public class ContentGenerationJob
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string RequestedBy { get; set; } = default!;       // Admin user ID

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(64)]
    public string? TaskTypeId { get; set; }

    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    public int RequestedCount { get; set; } = 1;
    public int GeneratedCount { get; set; }

    public string PromptConfigJson { get; set; } = "{}";      // Custom generation parameters
    public string GeneratedContentIdsJson { get; set; } = "[]";

    [MaxLength(32)]
    public string State { get; set; } = "queued";             // "queued", "generating", "completed", "failed"

    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

**Generation Templates by Task Type:**

- **OET Writing (Referral Letter):** Generate patient scenario, case notes, specific task requirements, model answer
- **OET Speaking (Roleplay):** Generate role card, patient history, interlocutor script, expected communication points
- **Reading Passage:** Generate healthcare/academic passage, then generate 6-8 questions with answer keys and explanations
- **Listening:** Generate transcript, then generate questions (harder -- may need audio generation service integration)

**Admin CMS Changes:**
- Add "Generate Content" button to content library toolbar
- Generation dialog: select exam type, subtest, task type, profession, difficulty, count (1-10)
- Show generation progress in content list (status badge)
- Generated content appears as Draft with "[AI Generated]" tag
- Admin reviews, edits, then publishes normally

**New Admin Endpoints:**
- `POST /v1/admin/content/generate` -- queue content generation
- `GET /v1/admin/content/generation-jobs` -- list generation jobs
- `GET /v1/admin/content/generation-jobs/{jobId}` -- job detail and results

**Integration:** Uses the existing `AIConfigVersion` entity for model and prompt configuration. The admin AI config page at `/admin/ai-config` manages generation prompts.

---

## SECTION 4: PHASE 2 -- AI-FIRST FEATURES

> **Timeline:** Weeks 7-14
> **Goal:** Build the immersive AI-powered features that differentiate this platform from all competitors.

### 4.1 AI Conversation Practice (Speaking Partner)

**Purpose:** The flagship feature. Real-time AI roleplay partner for OET speaking practice, AI interview simulation for IELTS speaking, and AI describe-image/re-tell partner for PTE speaking.

**This is the single most important new feature. It transforms passive practice into active, immersive preparation.**

#### Architecture

```
Browser (MediaRecorder API)
    ↓ audio chunks (WebSocket/SignalR)
Backend (ConversationHub - SignalR)
    ↓ audio → Speech-to-Text service (Deepgram/Whisper)
    ↓ transcript → AI Response Generator (Gemini/OpenAI)
    ↓ AI text → Text-to-Speech service (optional, or use pre-recorded)
    ↓ response text + optional audio
Browser (displays AI response, plays audio)
```

#### New Entities:

```csharp
public class ConversationSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? ContentId { get; set; }                    // Related speaking task

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = "speaking";

    [MaxLength(64)]
    public string TaskTypeCode { get; set; } = default!;      // "oet-roleplay", "ielts-part1", "ielts-part2", etc.

    public string ScenarioJson { get; set; } = "{}";          // Scenario card / interview topic / image URL

    [MaxLength(32)]
    public string State { get; set; } = "preparing";          // "preparing", "active", "completed", "abandoned", "evaluating", "evaluated"

    public int TurnCount { get; set; }
    public int DurationSeconds { get; set; }
    public string TranscriptJson { get; set; } = "[]";        // Full conversation transcript

    [MaxLength(64)]
    public string? EvaluationId { get; set; }                 // Link to post-conversation evaluation

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class ConversationTurn
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    public int TurnNumber { get; set; }

    [MaxLength(16)]
    public string Role { get; set; } = default!;              // "learner", "ai", "system"

    public string Content { get; set; } = default!;           // Transcript text

    [MaxLength(256)]
    public string? AudioUrl { get; set; }                     // Audio file URL

    public int DurationMs { get; set; }
    public int TimestampMs { get; set; }                      // Offset from session start
    public double? ConfidenceScore { get; set; }              // STT confidence

    public string AnalysisJson { get; set; } = "{}";          // Per-turn analysis (pronunciation, fluency markers)
}
```

#### OET Roleplay Flow:

1. **Preparation Phase (2 min):**
   - Show role card (e.g., "You are a nurse. The patient, Mr. Smith, 67, is being discharged after hip replacement surgery. Explain discharge instructions.")
   - Countdown timer
   - Learner reads and prepares

2. **Active Conversation (5 min):**
   - AI plays the patient/relative/colleague role
   - Learner speaks -> audio captured -> real-time transcription
   - AI generates contextually appropriate responses based on:
     - The scenario card
     - The conversation history
     - OET interlocutor guidelines (prompts, redirects, clarifying questions)
   - AI responses displayed as text + optional TTS audio
   - Waveform visualization for learner's speech
   - Turn counter and timer visible

3. **Post-Conversation:**
   - Full transcript review
   - AI evaluation queued (ConversationEvaluation background job)
   - Evaluation covers: clinical communication, fluency, appropriateness, grammar, pronunciation
   - Results page with criterion scores, specific feedback points, transcript annotations

#### IELTS Interview Flow:

1. **Part 1 (4-5 min):** AI asks introduction questions on familiar topics
2. **Part 2 (3-4 min):** Cue card shown, 1-min preparation, 2-min monologue, AI asks 1-2 rounding-off questions
3. **Part 3 (4-5 min):** AI asks abstract discussion questions related to Part 2 topic

#### New Background Job Type: `ConversationEvaluation`

Evaluates the conversation transcript against exam-specific criteria. Generates:
- Overall score per criterion
- Per-turn annotations (good phrases, errors, missed opportunities)
- Improvement suggestions
- Comparison with model responses

#### New SignalR Hub: `ConversationHub`

Methods:
- `StartSession(sessionId)` -- join the conversation room
- `SendAudio(sessionId, audioChunk)` -- stream audio from learner
- `ReceiveTranscript(text, confidence)` -- real-time transcript to client
- `ReceiveAIResponse(text, audioUrl?)` -- AI's response
- `EndSession(sessionId)` -- complete the conversation

#### New Endpoints:
- `POST /v1/conversations` -- create new conversation session
- `GET /v1/conversations/{sessionId}` -- get session state
- `POST /v1/conversations/{sessionId}/complete` -- mark as complete, trigger evaluation
- `GET /v1/conversations/{sessionId}/evaluation` -- get evaluation results
- `GET /v1/conversations/history?page={n}` -- past sessions list

#### Frontend:
- `app/conversation/page.tsx` -- Conversation practice hub (select scenario type, view history)
- `app/conversation/[sessionId]/page.tsx` -- Active conversation interface:
  - Full-screen immersive mode (minimal chrome)
  - Split view: scenario card (left), conversation (right)
  - Real-time transcript display (speech bubbles)
  - Waveform visualizer during recording
  - AI response with typing indicator
  - Timer and turn counter
  - "End Conversation" button
- `app/conversation/[sessionId]/results/page.tsx` -- Post-conversation evaluation display

### 4.2 AI Writing Coach (Real-Time Assistance)

**Purpose:** Provide real-time writing suggestions while the learner types, similar to Grammarly but specialized for exam writing (OET letter format, IELTS essay structure, PTE conciseness).

#### Architecture:

```
Learner types in WritingEditor
    ↓ debounce (2 seconds after last keystroke)
    ↓ POST current paragraph to backend
Backend → AI service (Gemini/OpenAI)
    ↓ returns suggestions array
    ↓ SignalR push to client
Frontend displays inline suggestions (underlines, tooltips)
```

#### New Entities:

```csharp
public class WritingCoachSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int SuggestionsGenerated { get; set; }
    public int SuggestionsAccepted { get; set; }
    public int SuggestionsDismissed { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}

public class WritingCoachSuggestion
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(32)]
    public string SuggestionType { get; set; } = default!;   // "grammar", "vocabulary", "structure", "tone", "conciseness", "format"

    public string OriginalText { get; set; } = default!;
    public string SuggestedText { get; set; } = default!;

    [MaxLength(512)]
    public string Explanation { get; set; } = default!;

    public int StartOffset { get; set; }                      // Character offset in document
    public int EndOffset { get; set; }

    [MaxLength(16)]
    public string? Resolution { get; set; }                   // "accepted", "dismissed", null (pending)

    public DateTimeOffset CreatedAt { get; set; }
}
```

#### Suggestion Categories:

| Type | Example (OET) | Example (IELTS) |
|------|---------------|------------------|
| grammar | "Subject-verb agreement: 'The patient were' → 'The patient was'" | Same |
| vocabulary | "'stomach ache' → 'epigastric pain' (more clinical)" | "'good' → 'beneficial' (more academic)" |
| structure | "Start with patient identification and reason for referral" | "Add a thesis statement to your introduction" |
| tone | "'Hey doc' → 'Dear Dr. Smith' (formal register)" | "'I think maybe' → 'It is evident that' (academic register)" |
| conciseness | "Remove redundant clause: 'He is a patient who is...' → 'He is...'" | "Reduce word count: this paragraph exceeds recommended length" |
| format | "Missing closing: add 'Yours sincerely, [Name]'" | "Missing word count: approximately 230/250 words" |

#### New Endpoints:
- `POST /v1/writing/attempts/{attemptId}/coach-check` -- submit current text, get suggestions
- `POST /v1/writing/coach-suggestions/{suggestionId}/resolve` -- accept or dismiss suggestion
- `GET /v1/writing/attempts/{attemptId}/coach-stats` -- acceptance rate and patterns

#### Frontend Changes:
- Extend existing writing editor component with coach overlay
- Underline suggestions (wavy blue for grammar, green for vocabulary, yellow for structure)
- Click underline -> tooltip with suggestion, explanation, accept/dismiss buttons
- Toggle coach on/off (some learners want to practice without assistance)
- Coach stats panel: suggestion count, acceptance rate
- Coach DISABLED during timed mock exams (to simulate real exam conditions)

### 4.3 Pronunciation Analysis

**Purpose:** Provide phoneme-level pronunciation feedback for speaking practice. Identify specific sounds that need work and provide targeted drills.

#### Integration: Azure Cognitive Services Speech SDK Pronunciation Assessment

The Azure SDK provides:
- Accuracy score (per phoneme, per word, per sentence)
- Fluency score (speech rate, pause patterns)
- Completeness score (missing words)
- Prosody score (intonation, stress patterns)

#### New Entities:

```csharp
public class PronunciationAssessment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? AttemptId { get; set; }                    // Speaking attempt or conversation session

    [MaxLength(64)]
    public string? ConversationSessionId { get; set; }

    public double AccuracyScore { get; set; }                 // 0-100
    public double FluencyScore { get; set; }                  // 0-100
    public double CompletenessScore { get; set; }             // 0-100
    public double ProsodyScore { get; set; }                  // 0-100
    public double OverallScore { get; set; }                  // 0-100

    public string WordScoresJson { get; set; } = "[]";        // Per-word scores with phoneme breakdown
    public string ProblematicPhonemesJson { get; set; } = "[]"; // Phonemes with low scores
    public string FluencyMarkersJson { get; set; } = "{}";    // Pause locations, speech rate segments

    public DateTimeOffset CreatedAt { get; set; }
}

public class PronunciationDrill
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string TargetPhoneme { get; set; } = default!;     // IPA symbol

    [MaxLength(128)]
    public string Label { get; set; } = default!;             // "th (as in 'think')"

    public string ExampleWordsJson { get; set; } = "[]";      // Words containing this phoneme
    public string MinimalPairsJson { get; set; } = "[]";      // Minimal pair exercises (ship/sheep)
    public string SentencesJson { get; set; } = "[]";         // Practice sentences

    [MaxLength(256)]
    public string? AudioModelUrl { get; set; }                // Model pronunciation audio

    [MaxLength(512)]
    public string TipsHtml { get; set; } = default!;          // Articulation tips

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

public class LearnerPronunciationProgress
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(32)]
    public string PhonemeCode { get; set; } = default!;

    public double AverageScore { get; set; }                  // Rolling average
    public int AttemptCount { get; set; }
    public string ScoreHistoryJson { get; set; } = "[]";      // Last 20 scores
    public DateTimeOffset LastPracticedAt { get; set; }
}
```

#### New Endpoints:
- `GET /v1/pronunciation/assessment/{assessmentId}` -- get pronunciation assessment detail
- `GET /v1/pronunciation/profile` -- get pronunciation profile (weak phonemes, progress)
- `GET /v1/pronunciation/drills` -- list available drills
- `GET /v1/pronunciation/drills/{drillId}` -- get drill detail
- `POST /v1/pronunciation/drills/{drillId}/attempt` -- submit drill audio for assessment
- `GET /v1/pronunciation/drills/{drillId}/attempt/{attemptId}/result` -- get drill result

#### Frontend:
- `app/pronunciation/page.tsx` -- Pronunciation hub: overall score, weak phonemes, recommended drills
- `app/pronunciation/[drillId]/page.tsx` -- Drill practice:
  - Model audio playback
  - Record button
  - Side-by-side comparison (model vs learner waveforms)
  - Per-word score visualization (green/amber/red)
  - Phoneme-level heatmap
  - Tips for improvement
- In speaking results page: add pronunciation analysis section:
  - Word-by-word pronunciation score visualization
  - Problem sounds highlighted
  - Link to relevant drills

### 4.4 Performance Prediction (Score Forecasting)

**Purpose:** Predict the learner's likely exam score based on practice performance, study patterns, and historical data. "Based on your practice, your predicted OET Writing score is 380-420."

#### Algorithm (Heuristic-Based, Not ML):

```
For each subtest:
1. Collect last 20 evaluation scores (weighted by recency: recent scores count more)
2. Apply difficulty adjustment (hard tasks weigh higher)
3. Compute weighted moving average
4. Apply consistency bonus/penalty (low variance = higher confidence)
5. Apply study pattern factor (regular practice = slight positive adjustment)
6. Compute confidence interval based on sample size and variance

Confidence Levels:
- < 5 evaluations: "Insufficient data"
- 5-10 evaluations: "Low confidence" (wide range)
- 10-20 evaluations: "Moderate confidence"
- 20+ evaluations: "Good confidence"
```

#### New Entities:

```csharp
public class PredictionSnapshot
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    public int PredictedScoreLow { get; set; }
    public int PredictedScoreHigh { get; set; }
    public int PredictedScoreMid { get; set; }

    [MaxLength(32)]
    public string ConfidenceLevel { get; set; } = default!;   // "insufficient", "low", "moderate", "good"

    public string FactorsJson { get; set; } = "{}";           // Contributing factors breakdown
    public string TrendJson { get; set; } = "{}";             // Trend direction (improving, stable, declining)

    public int EvaluationCount { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}
```

#### New Service: `PredictionService`

Methods:
- `ComputePredictionAsync(userId, examTypeCode, subtestCode)` -- compute and store prediction
- `GetLatestPredictionsAsync(userId, examTypeCode)` -- get latest per-subtest predictions
- `RefreshAllPredictionsAsync(userId, examTypeCode)` -- recompute all subtests (called after each evaluation)

**New Background Job Type:** `PredictionComputation`

Triggered after:
- Any evaluation completion
- Mock exam completion
- Weekly scheduled refresh

#### New Endpoints:
- `GET /v1/predictions` -- latest predictions for all subtests
- `GET /v1/predictions/{subtestCode}` -- prediction detail with factors
- `POST /v1/predictions/refresh` -- force refresh

#### Frontend:
- Add to `app/readiness/page.tsx`:
  - "Predicted Score" card per subtest alongside existing readiness meter
  - Score range visualization (range bar with midpoint marker)
  - Confidence indicator (icon + label)
  - Trend arrow (improving/stable/declining)
  - Disclaimer text: "Predictions are estimates based on practice performance and should not be treated as guaranteed exam scores."
- Add prediction summary to dashboard hero

---

## SECTION 5: PHASE 3 -- COMPLETE ECOSYSTEM

> **Timeline:** Weeks 15-24
> **Goal:** Fill every gap so the platform is a complete, one-stop preparation system.

### 5.1 Grammar Lessons Module

**Purpose:** Structured grammar instruction organized by exam relevance, integrated with the spaced repetition system.

#### New Entities:

```csharp
public class GrammarLesson
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(128)]
    public string Title { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    [MaxLength(32)]
    public string Category { get; set; } = default!;          // "tenses", "articles", "prepositions", "passive_voice", "conditionals", "modals", "formal_register"

    [MaxLength(16)]
    public string Level { get; set; } = "intermediate";       // "beginner", "intermediate", "advanced"

    public string ContentHtml { get; set; } = default!;       // Lesson content (rich text)
    public string ExercisesJson { get; set; } = "[]";         // Interactive exercises

    public int EstimatedMinutes { get; set; }
    public int SortOrder { get; set; }

    [MaxLength(32)]
    public string? PrerequisiteLessonId { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

public class LearnerGrammarProgress
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string LessonId { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "not_started";       // "not_started", "in_progress", "completed"

    public int? ExerciseScore { get; set; }                   // Percentage score on exercises
    public string AnswersJson { get; set; } = "{}";           // Saved exercise answers
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

**Exercise Types in ExercisesJson:**
1. **Gap Fill** -- sentence with blank, select/type correct answer
2. **Error Correction** -- find and fix the grammar error
3. **Sentence Transformation** -- rewrite sentence using target structure
4. **Multiple Choice** -- select grammatically correct option
5. **Matching** -- match sentence halves

**New Endpoints:**
- `GET /v1/grammar/lessons?examType={code}&category={cat}&level={level}` -- list lessons
- `GET /v1/grammar/lessons/{lessonId}` -- lesson content with exercises
- `POST /v1/grammar/lessons/{lessonId}/start` -- start a lesson
- `POST /v1/grammar/lessons/{lessonId}/submit` -- submit exercise answers
- `GET /v1/grammar/progress` -- overall grammar progress

**Frontend:**
- `app/grammar/page.tsx` -- Grammar hub: lesson catalogue organized by category, progress overview
- `app/grammar/[lessonId]/page.tsx` -- Lesson viewer: instructional content + interactive exercises
- Integration: when writing evaluation identifies grammar issues, link to relevant grammar lesson

### 5.2 Video Lessons

**Purpose:** High-quality video instruction for strategies, techniques, and exam walkthroughs.

#### New Entities:

```csharp
public class VideoLesson
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string? SubtestCode { get; set; }                  // null = general/overview

    [MaxLength(128)]
    public string Title { get; set; } = default!;

    [MaxLength(1024)]
    public string Description { get; set; } = default!;

    [MaxLength(512)]
    public string VideoUrl { get; set; } = default!;          // CDN URL or streaming service URL

    [MaxLength(512)]
    public string? ThumbnailUrl { get; set; }

    public int DurationSeconds { get; set; }

    [MaxLength(32)]
    public string Category { get; set; } = default!;          // "strategy", "technique", "walkthrough", "masterclass", "tips"

    [MaxLength(32)]
    public string? InstructorName { get; set; }

    public string ChaptersJson { get; set; } = "[]";          // Video chapters with timestamps
    public string ResourcesJson { get; set; } = "[]";         // Downloadable resources (PDFs, worksheets)

    public int SortOrder { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset PublishedAt { get; set; }
}

public class LearnerVideoProgress
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string VideoLessonId { get; set; } = default!;

    public int WatchedSeconds { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset LastWatchedAt { get; set; }
}
```

**Video Content Categories:**

| Category | Examples |
|----------|---------|
| Strategy | "How OET Writing is Scored", "IELTS Task 2: Essay Structure Strategy" |
| Technique | "Clinical Communication Techniques for OET Speaking", "Skimming and Scanning for Reading" |
| Walkthrough | "Complete OET Writing Task Walkthrough", "IELTS Speaking Part 2: Start to Finish" |
| Masterclass | "Advanced Referral Letter Writing", "Band 8+ Speaking Strategies" |
| Tips | "Top 10 OET Mistakes to Avoid", "5-Minute Exam Day Preparation" |

**New Endpoints:**
- `GET /v1/lessons?examType={code}&subtest={code}&category={cat}` -- list video lessons
- `GET /v1/lessons/{lessonId}` -- lesson detail
- `PATCH /v1/lessons/{lessonId}/progress` -- update watch progress
- `GET /v1/lessons/continue` -- lessons in progress

**Frontend:**
- `app/lessons/page.tsx` -- Video lesson catalogue with filters, progress tracking, "Continue Watching" section
- `app/lessons/[id]/page.tsx` -- Video player page with chapters, resources sidebar, related lessons

### 5.3 Exam Strategy Guides

**Purpose:** Written strategy content for each exam type and subtest.

#### New Entity:

```csharp
public class StrategyGuide
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string? SubtestCode { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(512)]
    public string Summary { get; set; } = default!;

    public string ContentHtml { get; set; } = default!;       // Full article content

    [MaxLength(32)]
    public string Category { get; set; } = default!;          // "exam_overview", "subtest_strategy", "time_management", "scoring_guide", "common_mistakes", "exam_day"

    public int ReadingTimeMinutes { get; set; }
    public int SortOrder { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset PublishedAt { get; set; }
}
```

**New Endpoints:**
- `GET /v1/strategies?examType={code}&subtest={code}&category={cat}` -- list guides
- `GET /v1/strategies/{guideId}` -- full guide content

**Frontend:**
- `app/strategies/page.tsx` -- Strategy guide library
- `app/strategies/[id]/page.tsx` -- Guide reader (clean article layout)

### 5.4 Community Features

**Purpose:** Peer interaction through discussion forums and study groups. Builds retention, creates social proof, and reduces churn.

#### New Entities:

```csharp
public class ForumCategory
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string? ExamTypeCode { get; set; }                 // null = general

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    public int SortOrder { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

public class ForumThread
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string CategoryId { get; set; } = default!;

    [MaxLength(64)]
    public string AuthorUserId { get; set; } = default!;

    [MaxLength(128)]
    public string AuthorDisplayName { get; set; } = default!;

    [MaxLength(32)]
    public string AuthorRole { get; set; } = default!;        // "learner", "expert", "admin"

    [MaxLength(256)]
    public string Title { get; set; } = default!;

    public string Body { get; set; } = default!;

    public bool IsPinned { get; set; }
    public bool IsLocked { get; set; }
    public int ReplyCount { get; set; }
    public int ViewCount { get; set; }
    public int LikeCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
}

public class ForumReply
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ThreadId { get; set; } = default!;

    [MaxLength(64)]
    public string AuthorUserId { get; set; } = default!;

    [MaxLength(128)]
    public string AuthorDisplayName { get; set; } = default!;

    [MaxLength(32)]
    public string AuthorRole { get; set; } = default!;

    public string Body { get; set; } = default!;

    public bool IsExpertVerified { get; set; }                // Expert-verified answer badge
    public int LikeCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
}

public class StudyGroup
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(64)]
    public string CreatorUserId { get; set; } = default!;

    public int MaxMembers { get; set; } = 20;
    public int MemberCount { get; set; }
    public bool IsPublic { get; set; } = true;

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
}

public class StudyGroupMember
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string GroupId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(32)]
    public string Role { get; set; } = "member";             // "owner", "moderator", "member"

    public DateTimeOffset JoinedAt { get; set; }
}
```

**Forum Categories (seed data):**
- General Discussion
- OET Writing Tips & Strategies
- OET Speaking Practice Partners
- OET Reading & Listening
- IELTS Preparation
- PTE Preparation
- Exam Experiences & Results
- Study Motivation & Support

**Moderation:**
- Admin can pin, lock, delete threads and replies
- Expert replies get a "Verified Expert" badge
- Automated spam detection (link limits, duplicate content, rate limiting)
- Report button on threads and replies

**New Endpoints:**
- `GET /v1/community/categories` -- list forum categories
- `GET /v1/community/threads?categoryId={id}&page={n}&sort={recent|popular}` -- list threads
- `GET /v1/community/threads/{threadId}` -- thread with replies
- `POST /v1/community/threads` -- create thread
- `POST /v1/community/threads/{threadId}/replies` -- reply to thread
- `POST /v1/community/threads/{threadId}/like` -- like a thread
- `POST /v1/community/replies/{replyId}/like` -- like a reply
- `POST /v1/community/threads/{threadId}/report` -- report thread
- `GET /v1/community/groups?examType={code}` -- list study groups
- `POST /v1/community/groups` -- create study group
- `POST /v1/community/groups/{groupId}/join` -- join group
- `POST /v1/community/groups/{groupId}/leave` -- leave group
- `GET /v1/community/groups/{groupId}` -- group detail with members

**Frontend:**
- `app/community/page.tsx` -- Community hub: forum categories, popular threads, study groups
- `app/community/threads/[threadId]/page.tsx` -- Thread view with replies
- `app/community/new/page.tsx` -- Create new thread
- `app/community/groups/page.tsx` -- Study group browser
- `app/community/groups/[groupId]/page.tsx` -- Study group detail

### 5.5 Live Tutoring Integration

**Purpose:** One-on-one video tutoring sessions with certified OET/IELTS tutors.

#### Architecture:
- Use third-party video SDK (Daily.co recommended -- simple API, good quality, reasonable pricing)
- Backend creates room via Daily.co API, generates tokens for learner and expert
- Frontend embeds Daily.co iframe or React SDK

#### New Entities:

```csharp
public class TutoringSession
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerUserId { get; set; } = default!;

    [MaxLength(64)]
    public string ExpertUserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string? SubtestFocus { get; set; }                 // Specific subtest focus

    public DateTimeOffset ScheduledAt { get; set; }
    public int DurationMinutes { get; set; } = 30;

    [MaxLength(32)]
    public string State { get; set; } = "booked";            // "booked", "confirmed", "in_progress", "completed", "cancelled", "no_show"

    [MaxLength(512)]
    public string? RoomUrl { get; set; }                      // Video call room URL

    [MaxLength(2048)]
    public string? LearnerNotes { get; set; }                 // What the learner wants to focus on

    [MaxLength(2048)]
    public string? ExpertNotes { get; set; }                  // Expert's session notes

    public decimal Price { get; set; }

    [MaxLength(32)]
    public string? PaymentSource { get; set; }                // "credits", "direct"

    public int? LearnerRating { get; set; }                   // 1-5 star rating
    public string? LearnerFeedback { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class TutoringAvailability
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ExpertUserId { get; set; } = default!;

    public int DayOfWeek { get; set; }                        // 0=Sunday, 6=Saturday

    [MaxLength(8)]
    public string StartTime { get; set; } = default!;         // "09:00"

    [MaxLength(8)]
    public string EndTime { get; set; } = default!;           // "17:00"

    [MaxLength(64)]
    public string Timezone { get; set; } = "UTC";

    public bool IsActive { get; set; } = true;
}
```

**Pricing:**
- 30-minute session: $40 AUD (20 credits)
- 60-minute session: $70 AUD (35 credits)
- Package of 5 sessions: $175 AUD (10% discount)

**New Endpoints:**
- `GET /v1/tutoring/tutors?examType={code}&subtest={code}` -- browse available tutors
- `GET /v1/tutoring/tutors/{expertId}/availability?week={date}` -- get available slots
- `POST /v1/tutoring/sessions` -- book a session
- `GET /v1/tutoring/sessions` -- list learner's sessions
- `GET /v1/tutoring/sessions/{sessionId}` -- session detail with room URL
- `POST /v1/tutoring/sessions/{sessionId}/cancel` -- cancel session
- `POST /v1/tutoring/sessions/{sessionId}/rate` -- rate session
- `GET /v1/expert/tutoring/sessions` -- expert's tutoring schedule
- `POST /v1/expert/tutoring/sessions/{sessionId}/notes` -- save expert notes

**Frontend:**
- `app/tutoring/page.tsx` -- Tutoring hub: browse tutors, upcoming sessions, past sessions
- `app/tutoring/book/[expertId]/page.tsx` -- Tutor profile + calendar slot picker + booking form
- `app/tutoring/session/[sessionId]/page.tsx` -- Pre-session lobby + embedded video call
- Expert side: extend expert schedule page with tutoring calendar view

### 5.6 Certificate & Badge System

**Purpose:** Generate downloadable PDF certificates for milestones and achievements.

#### New Entity:

```csharp
public class Certificate
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(128)]
    public string UserDisplayName { get; set; } = default!;

    [MaxLength(32)]
    public string Type { get; set; } = default!;              // "course_completion", "mock_achievement", "streak_milestone", "level_milestone"

    [MaxLength(256)]
    public string Title { get; set; } = default!;             // "OET Writing Practice Completion"

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    public string DataJson { get; set; } = "{}";              // Scores, dates, specific achievements

    [MaxLength(512)]
    public string? PdfUrl { get; set; }                       // Generated PDF storage URL

    [MaxLength(64)]
    public string VerificationCode { get; set; } = default!;  // Unique verification code

    public DateTimeOffset IssuedAt { get; set; }
}
```

**Certificate Types:**
- "Completed OET Writing Intensive" (50+ writing tasks completed)
- "Achieved OET Grade B in Mock Exam" (mock score >= 350)
- "100-Day Study Streak"
- "Level 25 Achievement"
- "Course Completion: [Course Name]"

**New Background Job Type:** `CertificateGeneration` -- generates PDF using a template engine (QuestPDF or similar .NET library).

**New Endpoints:**
- `GET /v1/certificates` -- list earned certificates
- `GET /v1/certificates/{certificateId}` -- certificate detail
- `GET /v1/certificates/{certificateId}/download` -- download PDF
- `GET /v1/certificates/verify/{code}` -- public verification endpoint

**Frontend:**
- `app/achievements/page.tsx` -- extend with certificates section (gallery view with download buttons)

### 5.7 Referral Program

#### New Entities:

```csharp
public class ReferralCode
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string Code { get; set; } = default!;              // Short unique code (e.g., "FAISAL2026")

    public int TotalReferrals { get; set; }
    public int ConvertedReferrals { get; set; }
    public decimal TotalCreditsEarned { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class Referral
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ReferrerUserId { get; set; } = default!;

    [MaxLength(64)]
    public string? ReferredUserId { get; set; }

    [MaxLength(256)]
    public string ReferredEmail { get; set; } = default!;

    [MaxLength(32)]
    public string Status { get; set; } = "pending";           // "pending", "registered", "converted", "credited"

    public decimal CreditAmount { get; set; } = 10;           // AUD

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
    public DateTimeOffset? CreditedAt { get; set; }
}
```

**New Background Job Type:** `ReferralConversion` -- processes credit awards when referred user subscribes.

**New Endpoints:**
- `GET /v1/referral/code` -- get or create referral code
- `GET /v1/referral/stats` -- referral statistics
- `GET /v1/referral/history` -- list referrals
- `POST /v1/referral/invite` -- send invitation email

**Frontend:**
- Add referral section to billing page or settings
- Share referral link/code
- View referral history and earned credits

### 5.8 Sponsor Dashboard (Parent/Employer/Institution)

#### New Entities:

```csharp
public class SponsorAccount
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AuthAccountId { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(32)]
    public string Type { get; set; } = default!;              // "parent", "employer", "institution"

    [MaxLength(256)]
    public string ContactEmail { get; set; } = default!;

    [MaxLength(256)]
    public string? OrganizationName { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
}

public class SponsorLearnerLink
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string SponsorId { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerId { get; set; } = default!;

    public bool LearnerConsented { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? ConsentedAt { get; set; }
}
```

**Sponsor Dashboard Shows (privacy-respecting):**
- Linked learner's name and active exam type
- Overall readiness level (High/Medium/Low) -- no specific scores
- Study activity summary (active days this week/month)
- Study plan completion rate (%)
- Mock exam count and pass/fail status
- Subscription status

**New Endpoints:**
- `POST /v1/sponsor/register` -- register sponsor account
- `POST /v1/sponsor/link` -- request link to learner (requires learner consent)
- `GET /v1/sponsor/dashboard` -- sponsor dashboard data
- `GET /v1/sponsor/learners` -- linked learners list
- `POST /v1/learner/sponsor-consent/{linkId}` -- learner approves/denies sponsor link

**Frontend:**
- `app/sponsor/page.tsx` -- Sponsor dashboard
- `app/sponsor/learners/page.tsx` -- Linked learners list

### 5.9 Cohort/Batch Management

#### New Entities:

```csharp
public class Cohort
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SponsorId { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;              // "2026 Q2 Nursing Batch"

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public int MaxSeats { get; set; }
    public int EnrolledCount { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";            // "draft", "active", "completed", "archived"

    public DateTimeOffset CreatedAt { get; set; }
}

public class CohortMember
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string CohortId { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerId { get; set; } = default!;

    [MaxLength(16)]
    public string Status { get; set; } = "active";            // "active", "completed", "withdrawn"

    public DateTimeOffset EnrolledAt { get; set; }
}
```

**New Background Job Type:** `CohortProgressReport` -- generates aggregated progress report for all cohort members.

**New Endpoints:**
- `POST /v1/sponsor/cohorts` -- create cohort
- `GET /v1/sponsor/cohorts` -- list cohorts
- `GET /v1/sponsor/cohorts/{cohortId}` -- cohort detail with member progress
- `POST /v1/sponsor/cohorts/{cohortId}/members` -- add member (by email invitation)
- `GET /v1/sponsor/cohorts/{cohortId}/report` -- aggregated progress report

**Frontend:**
- `app/sponsor/cohorts/page.tsx` -- Cohort list
- `app/sponsor/cohorts/[cohortId]/page.tsx` -- Cohort detail with member table and aggregate stats

---

## SECTION 6: PHASE 4 -- SCALE & POLISH

> **Timeline:** Weeks 25-36
> **Goal:** Internationalization, accessibility, offline, and marketplace features.

### 6.1 Multi-Language Interface (i18n)

**Languages (priority order):** English (default), Arabic, Hindi, Tagalog, Mandarin, Urdu

**Implementation:**
- Use Next.js i18n routing with `app/[locale]/` prefix
- Extract all UI strings to JSON locale files in `locales/{locale}/common.json`
- API responses remain English (exams are in English)
- Only UI chrome, navigation, help text, and error messages are translated
- RTL support for Arabic

**Frontend Changes:**
- Add `lib/i18n.ts` -- locale detection, dictionary loading
- Add language selector to settings and footer
- Wrap all UI strings with `t('key')` translation function

### 6.2 WCAG 2.2 AA Accessibility

**Audit Scope:** All 77+ existing pages plus all new pages.

**Requirements:**
- All interactive elements keyboard-navigable
- Focus indicators visible and consistent
- Color contrast ratio >= 4.5:1 for normal text, >= 3:1 for large text
- All images have alt text
- All charts have data table alternatives
- Skip-to-content links on every page
- Modal focus trapping
- Audio player with keyboard controls and time display for screen readers
- High-contrast mode toggle in accessibility settings
- Text size adjustment (100%, 125%, 150%)
- Reduced motion mode (respects `prefers-reduced-motion`)

**Testing:** Every new page must pass `@axe-core/playwright` automated checks in E2E tests.

### 6.3 Offline Practice (Mobile)

**Implementation:**
- Use Capacitor Filesystem and Preferences plugins (already in dependencies)
- New service: `lib/mobile/offline-sync.ts`
- Cache vocabulary flashcards, grammar lessons, strategy guides for offline access
- Cache reading passages and listening audio for offline practice
- Queue attempts offline, sync when connection returns
- Display "offline" indicator in top nav

### 6.4 Exam Booking Integration

**Partner with official exam booking systems:**
- OET: Link to oet.com booking portal
- IELTS: Link to British Council / IDP booking
- PTE: Link to Pearson booking

**New Entity:**

```csharp
public class ExamBooking
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    public DateOnly ExamDate { get; set; }

    [MaxLength(128)]
    public string? BookingReference { get; set; }

    [MaxLength(512)]
    public string? ExternalUrl { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "planned";           // "planned", "booked", "confirmed", "completed", "cancelled"

    [MaxLength(128)]
    public string? TestCenter { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
```

**Frontend:**
- `app/exam-booking/page.tsx` -- Exam booking hub with date selection and redirect to partner site
- Integration with goals: auto-update target exam date when booking is confirmed

### 6.5 Content Marketplace

**Purpose:** Allow verified educators to submit content for review and publication.

#### New Entities:

```csharp
public class ContentContributor
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string AuthAccountId { get; set; } = default!;

    [MaxLength(128)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(1024)]
    public string? Bio { get; set; }

    public bool IsVerified { get; set; }
    public int PublishedCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class ContentSubmission
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ContributorId { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(64)]
    public string? TaskTypeId { get; set; }

    public string ContentPayloadJson { get; set; } = "{}";

    [MaxLength(32)]
    public string Status { get; set; } = "submitted";         // "submitted", "reviewing", "revisions_requested", "approved", "rejected", "published"

    public string? ReviewNotesJson { get; set; }

    [MaxLength(64)]
    public string? PublishedContentId { get; set; }            // Link to ContentItem if published

    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
```

**Revenue Model:** 70/30 split -- contributor gets 70% of revenue attributed to their content.

**Admin Integration:** Submissions appear in admin content review queue.

---

## SECTION 7: DATA MODEL REFERENCE

All new entities are defined inline in their respective feature sections above (Sections 3-6). Below is the complete registry of new entities grouped by feature:

### Multi-Exam Foundation
- `ExamType` -- exam type reference table
- `TaskType` -- content task type taxonomy

### Adaptive Difficulty
- `LearnerSkillProfile` -- per-user, per-criterion skill rating

### Spaced Repetition
- `ReviewItem` -- items due for review with SM-2 scheduling

### Gamification
- `LearnerStreak` -- streak tracking
- `LearnerXP` -- XP and level tracking
- `Achievement` -- achievement definitions (seed 30+)
- `LearnerAchievement` -- unlocked achievements per learner
- `LeaderboardEntry` -- computed leaderboard entries

### Vocabulary
- `VocabularyTerm` -- term definitions with audio, examples, collocations
- `LearnerVocabulary` -- per-learner mastery tracking
- `VocabularyQuizResult` -- quiz result history

### AI Content Generation
- `ContentGenerationJob` -- admin content generation requests

### AI Conversation Practice
- `ConversationSession` -- conversation session tracking
- `ConversationTurn` -- individual conversation turns

### AI Writing Coach
- `WritingCoachSession` -- writing coach session tracking
- `WritingCoachSuggestion` -- individual suggestions with accept/dismiss tracking

### Pronunciation
- `PronunciationAssessment` -- per-attempt pronunciation analysis
- `PronunciationDrill` -- pronunciation drill definitions
- `LearnerPronunciationProgress` -- per-phoneme progress tracking

### Performance Prediction
- `PredictionSnapshot` -- per-subtest score predictions

### Grammar Lessons
- `GrammarLesson` -- grammar lesson content and exercises
- `LearnerGrammarProgress` -- per-lesson progress

### Video Lessons
- `VideoLesson` -- video lesson metadata
- `LearnerVideoProgress` -- watch progress tracking

### Strategy Guides
- `StrategyGuide` -- written strategy article content

### Community
- `ForumCategory` -- forum category definitions
- `ForumThread` -- discussion threads
- `ForumReply` -- thread replies
- `StudyGroup` -- study group definitions
- `StudyGroupMember` -- group membership

### Live Tutoring
- `TutoringSession` -- tutoring session booking and state
- `TutoringAvailability` -- expert tutoring schedule

### Certificates
- `Certificate` -- issued certificates with PDF generation

### Referrals
- `ReferralCode` -- per-user referral codes
- `Referral` -- referral tracking with conversion state

### Sponsor & Cohorts
- `SponsorAccount` -- sponsor accounts (parent/employer/institution)
- `SponsorLearnerLink` -- sponsor-learner relationship with consent
- `Cohort` -- batch/cohort definitions
- `CohortMember` -- cohort membership

### Exam Booking
- `ExamBooking` -- exam booking tracking

### Content Marketplace
- `ContentContributor` -- educator contributor profiles
- `ContentSubmission` -- submitted content for review

**Total: 42 new entities.**

---

## SECTION 8: API ENDPOINT REFERENCE

### New Endpoint Groups to Add to Program.cs

```csharp
// In Program.cs, add after existing endpoint mappings:

app.MapGroup("/v1").MapExamTypeEndpoints();         // ExamType reference data
app.MapGroup("/v1").MapAdaptiveEndpoints();          // Skill profile & recommendations
app.MapGroup("/v1").MapReviewItemEndpoints();        // Spaced repetition
app.MapGroup("/v1").MapGamificationEndpoints();      // Streaks, XP, achievements, leaderboard
app.MapGroup("/v1").MapVocabularyEndpoints();        // Vocabulary terms, flashcards, quiz
app.MapGroup("/v1").MapConversationEndpoints();      // AI conversation practice
app.MapGroup("/v1").MapPronunciationEndpoints();     // Pronunciation assessment & drills
app.MapGroup("/v1").MapPredictionEndpoints();        // Score predictions
app.MapGroup("/v1").MapGrammarEndpoints();           // Grammar lessons
app.MapGroup("/v1").MapVideoLessonEndpoints();       // Video lessons
app.MapGroup("/v1").MapStrategyEndpoints();          // Strategy guides
app.MapGroup("/v1").MapCommunityEndpoints();         // Forums & study groups
app.MapGroup("/v1").MapTutoringEndpoints();          // Live tutoring
app.MapGroup("/v1").MapCertificateEndpoints();       // Certificates
app.MapGroup("/v1").MapReferralEndpoints();          // Referral program
app.MapGroup("/v1").MapSponsorEndpoints();           // Sponsor dashboard
app.MapGroup("/v1/admin").MapAdminContentGenEndpoints(); // Admin content generation
```

### Complete Endpoint List

All endpoints use existing authorization policies (`LearnerOnly`, `ExpertOnly`, `AdminOnly`) and rate limiting (`PerUserRead`, `PerUserWrite`).

See individual feature sections (3-6) for detailed endpoint specifications per feature.

---

## SECTION 9: FRONTEND ROUTE MAP

### New Routes

```
app/
  # Phase 1
  review/                          -- Spaced repetition hub
    session/                       -- Review session (card-based)
  achievements/                    -- Achievement gallery + certificates
  leaderboard/                     -- Weekly/monthly leaderboard
  vocabulary/                      -- Vocabulary builder hub
    flashcards/                    -- Flashcard practice
    quiz/                          -- Vocabulary quiz
    browse/                        -- Full term catalogue

  # Phase 2
  conversation/                    -- AI conversation practice hub
    [sessionId]/                   -- Active conversation
      results/                     -- Post-conversation evaluation
  pronunciation/                   -- Pronunciation hub
    [drillId]/                     -- Pronunciation drill practice

  # Phase 3
  grammar/                         -- Grammar lessons hub
    [lessonId]/                    -- Lesson viewer + exercises
  lessons/                         -- Video lesson catalogue
    [id]/                          -- Video player
  strategies/                      -- Strategy guide library
    [id]/                          -- Guide reader
  community/                       -- Community hub (forums + groups)
    threads/
      [threadId]/                  -- Thread view with replies
    new/                           -- Create new thread
    groups/                        -- Study group browser
      [groupId]/                   -- Group detail
  tutoring/                        -- Tutoring hub
    book/[expertId]/               -- Booking flow
    session/[sessionId]/           -- Live session (video embed)

  # Phase 4
  exam-booking/                    -- Exam booking integration
  sponsor/                         -- Sponsor dashboard
    learners/                      -- Linked learners
    cohorts/                       -- Cohort management
      [cohortId]/                  -- Cohort detail

  # Admin Extensions
  admin/
    content-generation/            -- AI content generation management
    community/                     -- Community moderation
    tutoring/                      -- Tutoring management
    marketplace/                   -- Content marketplace review
    sponsors/                      -- Sponsor account management
```

---

## SECTION 10: BACKGROUND JOB EXTENSIONS

Add to the `JobType` enum in `Enums.cs`:

```csharp
public enum JobType
{
    // Existing
    WritingEvaluation,
    SpeakingTranscription,
    SpeakingEvaluation,
    StudyPlanRegeneration,
    MockReportGeneration,
    ReviewCompletion,
    NotificationFanout,
    NotificationDigestDispatch,

    // New -- Phase 1
    ContentGeneration,              // AI content generation for admin
    SkillProfileUpdate,             // Update learner skill profile after evaluation
    AchievementCheck,               // Check and award achievements after XP-granting events

    // New -- Phase 2
    ConversationEvaluation,         // Evaluate completed AI conversation
    PronunciationAnalysis,          // Analyze pronunciation from speaking audio
    PredictionComputation,          // Compute score predictions after evaluation

    // New -- Phase 3
    CertificateGeneration,          // Generate PDF certificates
    ReferralConversion,             // Process referral credit awards
    CohortProgressReport,           // Generate cohort progress reports
    VocabularyDailySet,             // Prepare daily vocabulary set per learner

    // New -- Phase 4
    LeaderboardComputation          // Weekly/monthly leaderboard recalculation
}
```

Each new job type follows the existing pattern in `BackgroundJobProcessor`:
1. Enqueue as `BackgroundJobItem` with `JobType` and `PayloadJson`
2. Processor picks up job, dispatches to handler by `JobType`
3. Max 3 retries with exponential backoff (2^attempt * 5 seconds)
4. Failed jobs emit admin alert notifications

---

## SECTION 11: QUALITY & TESTING REQUIREMENTS

### Per-Feature Quality Gates

| Requirement | Standard |
|------------|----------|
| Backend unit tests | Every new service method must have at least 1 test in `backend/tests/` |
| Frontend render tests | Every new page must have a basic render test via Vitest |
| E2E smoke tests | Critical flows: conversation practice, vocabulary quiz, community posting, tutoring booking |
| API response time | Read endpoints < 200ms, write endpoints < 500ms |
| Accessibility | Every new page must pass `@axe-core/playwright` automated checks |
| Responsive design | Test at 375px (mobile), 768px (tablet), 1024px (laptop), 1440px (desktop) |
| Feature flags | Every feature must ship behind a flag. Test both enabled and disabled states |
| Error handling | All API calls must use `ApiError` pattern. All failures must show user-friendly messages |
| Loading states | Every async operation must show a loading indicator (skeleton or spinner) |
| Empty states | Every list/collection view must handle the empty state gracefully |

### Test File Locations

| Test Type | Location | Runner |
|-----------|----------|--------|
| Backend unit/integration | `backend/tests/OetLearner.Api.Tests/` | xUnit via `dotnet test` |
| Frontend unit | `lib/__tests__/`, `components/**/__tests__/` | Vitest via `npm test` |
| E2E | `tests/e2e/` | Playwright via `npx playwright test` |

---

## SECTION 12: MIGRATION & DEPLOYMENT SEQUENCE

All migrations are backward-compatible. No dropping columns or tables. Run in order.

| # | Migration Name | Tables Added/Modified | Phase |
|---|---------------|----------------------|-------|
| 1 | AddExamTypeFoundation | Add `ExamType`, `TaskType` tables. Add `ExamTypeCode` to `ContentItem`, `Attempt`, `Evaluation`, `StudyPlan`, `DiagnosticSession`, `MockAttempt`, `LearnerGoal`, `CriterionReference`. Add `ActiveExamTypeCode` to `LearnerUser`. | 1 |
| 2 | AddAdaptiveDifficulty | Add `LearnerSkillProfile` | 1 |
| 3 | AddSpacedRepetition | Add `ReviewItem` | 1 |
| 4 | AddGamification | Add `LearnerStreak`, `LearnerXP`, `Achievement`, `LearnerAchievement`, `LeaderboardEntry` | 1 |
| 5 | AddVocabulary | Add `VocabularyTerm`, `LearnerVocabulary`, `VocabularyQuizResult` | 1 |
| 6 | AddContentGeneration | Add `ContentGenerationJob` | 1 |
| 7 | AddConversationPractice | Add `ConversationSession`, `ConversationTurn` | 2 |
| 8 | AddWritingCoach | Add `WritingCoachSession`, `WritingCoachSuggestion` | 2 |
| 9 | AddPronunciation | Add `PronunciationAssessment`, `PronunciationDrill`, `LearnerPronunciationProgress` | 2 |
| 10 | AddPredictions | Add `PredictionSnapshot` | 2 |
| 11 | AddGrammarLessons | Add `GrammarLesson`, `LearnerGrammarProgress` | 3 |
| 12 | AddVideoLessons | Add `VideoLesson`, `LearnerVideoProgress` | 3 |
| 13 | AddStrategyGuides | Add `StrategyGuide` | 3 |
| 14 | AddCommunity | Add `ForumCategory`, `ForumThread`, `ForumReply`, `StudyGroup`, `StudyGroupMember` | 3 |
| 15 | AddTutoring | Add `TutoringSession`, `TutoringAvailability` | 3 |
| 16 | AddCertificates | Add `Certificate` | 3 |
| 17 | AddReferrals | Add `ReferralCode`, `Referral` | 3 |
| 18 | AddSponsorsAndCohorts | Add `SponsorAccount`, `SponsorLearnerLink`, `Cohort`, `CohortMember` | 3 |
| 19 | AddExamBooking | Add `ExamBooking` | 4 |
| 20 | AddContentMarketplace | Add `ContentContributor`, `ContentSubmission` | 4 |

---

## SECTION 13: INTEGRATION ARCHITECTURE

### External Services

| Service | Purpose | Provider Options | Integration Point |
|---------|---------|-----------------|-------------------|
| AI Text Generation | Conversation responses, content generation, writing coach, evaluations | Google Gemini (existing), OpenAI GPT-4o, Anthropic Claude | Backend service via HTTP API |
| Speech-to-Text | Real-time transcription for conversation practice | Deepgram (recommended -- fast, WebSocket API), Whisper (self-hosted), Azure Speech | Backend SignalR hub |
| Text-to-Speech | AI partner voice in conversation practice | Azure Speech, Google Cloud TTS, ElevenLabs (premium voices) | Backend service, audio URL returned to client |
| Pronunciation Assessment | Phoneme-level pronunciation scoring | Azure Cognitive Services Speech SDK (recommended -- best phoneme accuracy) | Backend service |
| Video Calls | Live tutoring sessions | Daily.co (recommended -- simple API), Twilio Video, Agora | Backend creates rooms, frontend embeds SDK |
| PDF Generation | Certificate PDFs | QuestPDF (.NET library, no external service needed) | Backend background job |
| Email | Transactional emails | Brevo (existing), SendGrid, AWS SES | Existing `BrevoEmailSender` / `SmtpEmailSender` |
| Payments | Subscription billing, credit purchases | Stripe (existing external checkout pattern) | Existing billing endpoints |
| CDN | Video lesson delivery, audio file delivery | Cloudflare R2, AWS CloudFront, Azure CDN | Static file hosting |
| Push Notifications | Mobile push | Capacitor Push Notifications (existing), Firebase FCM | Existing `WebPushDispatcher` |

### Configuration (add to Options pattern):

```csharp
public class AIConversationOptions
{
    public string SttProvider { get; set; } = "deepgram";     // "deepgram", "whisper", "azure"
    public string SttApiKey { get; set; } = default!;
    public string TtsProvider { get; set; } = "azure";
    public string TtsApiKey { get; set; } = default!;
}

public class PronunciationOptions
{
    public string AzureSpeechKey { get; set; } = default!;
    public string AzureSpeechRegion { get; set; } = default!;
}

public class VideoCallOptions
{
    public string Provider { get; set; } = "daily";
    public string ApiKey { get; set; } = default!;
}

public class CdnOptions
{
    public string BaseUrl { get; set; } = default!;
}
```

---

## SECTION 14: BUSINESS RULES & ENTITLEMENTS

### Feature Entitlement Matrix

Store in `BillingPlan.EntitlementsJson`. Check in service methods before allowing access.

```json
{
  "free": {
    "diagnosticAssessmentsPerExam": 1,
    "practiceTasksPerWeek": 2,
    "aiEvaluationLevel": "basic",
    "studyPlanLevel": "basic",
    "aiConversationSessionsPerMonth": 0,
    "aiWritingCoach": false,
    "pronunciationAnalysisLevel": "none",
    "expertReviewsPerMonth": 0,
    "mockExamsPerMonth": 0,
    "vocabularyTermLimit": 50,
    "grammarLessons": 5,
    "videoLessonsAccess": "preview",
    "communityAccess": "read_only",
    "liveTutoring": false,
    "scorePrediction": false,
    "certificates": false,
    "leaderboard": false,
    "offlineMode": false,
    "examTypes": ["oet"]
  },
  "standard": {
    "diagnosticAssessmentsPerExam": -1,
    "practiceTasksPerWeek": -1,
    "aiEvaluationLevel": "full",
    "studyPlanLevel": "ai_personalized",
    "aiConversationSessionsPerMonth": 0,
    "aiWritingCoach": false,
    "pronunciationAnalysisLevel": "basic",
    "expertReviewsPerMonth": 0,
    "mockExamsPerMonth": 2,
    "vocabularyTermLimit": -1,
    "grammarLessons": -1,
    "videoLessonsAccess": "full",
    "communityAccess": "full",
    "liveTutoring": false,
    "scorePrediction": true,
    "certificates": true,
    "leaderboard": true,
    "offlineMode": false,
    "examTypes": ["oet"]
  },
  "premium": {
    "diagnosticAssessmentsPerExam": -1,
    "practiceTasksPerWeek": -1,
    "aiEvaluationLevel": "full",
    "studyPlanLevel": "ai_personalized",
    "aiConversationSessionsPerMonth": 10,
    "aiWritingCoach": true,
    "pronunciationAnalysisLevel": "full",
    "expertReviewsPerMonth": 3,
    "mockExamsPerMonth": -1,
    "vocabularyTermLimit": -1,
    "grammarLessons": -1,
    "videoLessonsAccess": "full",
    "communityAccess": "full",
    "liveTutoring": true,
    "scorePrediction": true,
    "certificates": true,
    "leaderboard": true,
    "offlineMode": true,
    "examTypes": ["oet", "ielts", "pte", "cambridge", "toefl"]
  },
  "institutional": {
    "diagnosticAssessmentsPerExam": -1,
    "practiceTasksPerWeek": -1,
    "aiEvaluationLevel": "full",
    "studyPlanLevel": "ai_personalized",
    "aiConversationSessionsPerMonth": -1,
    "aiWritingCoach": true,
    "pronunciationAnalysisLevel": "full",
    "expertReviewsPerMonth": 5,
    "mockExamsPerMonth": -1,
    "vocabularyTermLimit": -1,
    "grammarLessons": -1,
    "videoLessonsAccess": "full",
    "communityAccess": "full",
    "liveTutoring": true,
    "scorePrediction": true,
    "certificates": true,
    "leaderboard": true,
    "offlineMode": true,
    "examTypes": ["oet", "ielts", "pte", "cambridge", "toefl"],
    "cohortManagement": true,
    "sponsorDashboard": true,
    "customBranding": true
  }
}
```

**Note:** `-1` means unlimited.

### Entitlement Check Pattern

```csharp
// In service methods, before feature access:
var entitlements = await GetUserEntitlementsAsync(userId);
if (!entitlements.AiWritingCoach)
    throw new ApiException("Writing coach requires Premium subscription", "PLAN_UPGRADE_REQUIRED");
```

### Credit Economy

| Action | Credit Cost |
|--------|------------|
| Expert review (standard, 72h turnaround) | 15 credits |
| Expert review (express, 24h turnaround) | 25 credits |
| Live tutoring (30 min) | 20 credits |
| Live tutoring (60 min) | 35 credits |
| Premium certificate PDF | 5 credits |

**Credit Pricing:**
- 10 credits: $10 AUD
- 25 credits: $22 AUD (12% discount)
- 50 credits: $40 AUD (20% discount)
- 100 credits: $70 AUD (30% discount)

---

## SECTION 15: INCREMENTAL IMPLEMENTATION PROTOCOL

### For EVERY Feature, Follow This Exact Sequence:

```
1. DATABASE FIRST
   ├── Add entities to Domain/ (new file or extend existing)
   ├── Add enums to Enums.cs if needed
   ├── Register DbSet in LearnerDbContext
   ├── Configure indexes in OnModelCreating
   └── Create EF Core migration

2. SEED DATA
   ├── Add reference data to SeedData.cs
   └── Add feature flag to SeedData.cs

3. SERVICE LAYER
   ├── Create or extend service in Services/
   ├── Register service in Program.cs (AddScoped)
   └── Follow existing method naming conventions

4. CONTRACTS
   └── Add request/response DTOs to Contracts/

5. ENDPOINTS
   ├── Create endpoint group class in Endpoints/
   ├── Map in Program.cs
   ├── Apply authorization policies
   └── Apply rate limiting

6. BACKGROUND JOBS (if applicable)
   ├── Add JobType enum value
   └── Add handler case in BackgroundJobProcessor

7. BACKEND TESTS
   └── Add tests in backend/tests/

8. BACKEND VALIDATION
   ├── dotnet build backend/OetLearner.sln
   └── dotnet test backend/OetLearner.sln

9. FRONTEND TYPES
   └── Add TypeScript interfaces to lib/types/

10. FRONTEND API
    └── Add API functions to lib/api.ts

11. FRONTEND PAGES
    ├── Create app/{feature}/page.tsx
    └── Add to navigation (sidebar, relevant hub pages)

12. FRONTEND COMPONENTS
    └── Create domain components in components/domain/

13. FRONTEND TESTS
    └── Add render tests via Vitest

14. FRONTEND VALIDATION
    ├── npm run lint
    ├── npm test
    └── npm run build
```

**Never skip a step. Never implement steps out of order.**

### Feature Implementation Priority

Within each phase, implement features in this order:

**Phase 1:**
1. Multi-exam database foundation (prerequisite for everything)
2. Adaptive difficulty engine
3. Gamification system (streaks + XP -- high engagement impact)
4. Vocabulary builder
5. Spaced repetition system
6. AI content generation (admin tool)

**Phase 2:**
1. AI conversation practice (flagship feature)
2. Pronunciation analysis
3. AI writing coach
4. Performance prediction

**Phase 3:**
1. Grammar lessons
2. Video lessons
3. Strategy guides
4. Community features
5. Certificates & badges
6. Referral program
7. Live tutoring
8. Sponsor dashboard & cohort management

**Phase 4:**
1. WCAG 2.2 accessibility
2. Multi-language support
3. Offline mode
4. Exam booking integration
5. Content marketplace

---

## APPENDIX A: EXISTING CRITICAL FILE PATHS

| File | Purpose |
|------|---------|
| `backend/src/OetLearner.Api/Program.cs` | Application bootstrap, all service registration, endpoint mapping |
| `backend/src/OetLearner.Api/Domain/Entities.cs` | Core learner entities -- PATTERN REFERENCE |
| `backend/src/OetLearner.Api/Domain/BillingEntities.cs` | Billing entities |
| `backend/src/OetLearner.Api/Domain/AdminEntities.cs` | Admin CMS entities |
| `backend/src/OetLearner.Api/Domain/ExpertEntities.cs` | Expert review entities |
| `backend/src/OetLearner.Api/Domain/SignupEntities.cs` | Signup catalog with ExamType entries |
| `backend/src/OetLearner.Api/Domain/Enums.cs` | All enums (states, types) |
| `backend/src/OetLearner.Api/Data/LearnerDbContext.cs` | EF Core context with all DbSets |
| `backend/src/OetLearner.Api/Services/LearnerService.cs` | Core learner service (~2600 lines) |
| `backend/src/OetLearner.Api/Services/ExpertService.cs` | Expert review service |
| `backend/src/OetLearner.Api/Services/AdminService.cs` | Admin CMS service |
| `backend/src/OetLearner.Api/Services/BackgroundJobProcessor.cs` | Background job processing |
| `backend/src/OetLearner.Api/Services/SeedData.cs` | Reference data + demo data seeding |
| `lib/api.ts` | Frontend API client |
| `lib/types/auth.ts` | Auth TypeScript types |
| `lib/types/expert.ts` | Expert TypeScript types |
| `contexts/auth-context.tsx` | Auth context provider |
| `contexts/notification-center-context.tsx` | Notification context |
| `components/layout/app-shell.tsx` | Main app layout |
| `components/layout/sidebar.tsx` | Navigation sidebar |
| `app/layout.tsx` | Root layout |

---

## APPENDIX B: DESIGN GUIDELINES

### Visual Design Principles

1. **Professional, not playful.** Clean lines, generous whitespace, muted accent colors.
2. **Information hierarchy.** Most important information (scores, due items, predictions) prominent. Secondary information accessible but not distracting.
3. **Consistent component usage.** Use existing UI primitives from `components/ui/`. Only create new primitives if none exist.
4. **Progress visualization.** Use progress bars, ring charts, and trend lines (Recharts). Avoid pie charts.
5. **Achievement celebration.** Subtle, professional -- a brief animation (< 2s), a toast notification, a badge update. Not confetti explosions.
6. **Color coding.** Green = on track / good. Amber = needs attention. Red = behind / critical. Blue = informational. Purple = premium feature.
7. **Typography.** Inter font (already configured). Use the existing heading/body hierarchy.
8. **Responsive.** Every page must work from 375px to 1440px+ width. Use Tailwind responsive utilities.

### UX Patterns

1. **2-click rule.** Any practice task should be reachable within 2 clicks from the dashboard.
2. **Session time estimates.** Always show estimated time for tasks, lessons, and reviews.
3. **Auto-save.** Writing drafts, quiz progress, and settings changes save automatically.
4. **Graceful degradation.** If an AI service is unavailable, show a clear message and offer alternative practice.
5. **Loading states.** Every async operation shows a skeleton or spinner. Never show a blank page.
6. **Empty states.** Every list view has a helpful empty state with a call-to-action.
7. **Error states.** User-friendly error messages with retry buttons. Never show stack traces.

---

## APPENDIX C: SEED DATA CHECKLIST

After implementing each phase, ensure seed data includes:

### Phase 1 Seed Data
- [ ] `ExamType` records for OET and IELTS (minimum)
- [ ] `TaskType` records for all OET task types (writing: referral, discharge, transfer, case notes; speaking: roleplay; reading: part A, part B; listening: part A, B, C)
- [ ] `TaskType` records for all IELTS task types
- [ ] 30+ `Achievement` definitions covering all categories
- [ ] 500+ `VocabularyTerm` entries for OET medical English
- [ ] 300+ `VocabularyTerm` entries for IELTS academic vocabulary
- [ ] Feature flags for each new feature (all disabled by default in production, enabled in dev)
- [ ] Updated demo user with gamification data (streak, XP, some achievements)

### Phase 2 Seed Data
- [ ] Sample `ConversationSession` in demo data
- [ ] `PronunciationDrill` records for common English phonemes (20+)
- [ ] Demo `PredictionSnapshot` data

### Phase 3 Seed Data
- [ ] 20+ `GrammarLesson` records covering essential categories
- [ ] 10+ `VideoLesson` records (placeholder URLs)
- [ ] 20+ `StrategyGuide` records
- [ ] `ForumCategory` records for each exam type
- [ ] Demo forum threads and replies

---

**END OF MEGA MASTER PROMPT**

*This document is the single source of truth for all platform enhancements. Implement features phase by phase, feature by feature, following the incremental protocol in Section 15. When in doubt, read the existing code first.*
