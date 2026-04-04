# PRACTICAL IMPLEMENTATION DEVELOPMENT PLAN

> **Version:** 1.0 | **Date:** 2026-04-04 | **Based On:** `docs/mega-master-prompt.md`
> **Reality-Checked Against:** Actual codebase state as of April 4, 2026

---

## CURRENT STATE BASELINE (What We Actually Have)

Before planning, here is what **already exists** and must NOT be duplicated:

| Area | Current State | Implication |
|------|--------------|-------------|
| **ExamFamily** entity | Already in DbContext | Use this instead of creating new `ExamType` entity |
| **EngagementService** | Streak calculation, weekly activity, risk assessment | Extend for XP/achievements, don't rebuild streaks |
| **WalletService** | Full credit ledger, tiered top-ups (10/25/50/100 credits) | Already handles credit economy; extend for new purchase types |
| **PaymentGatewayService** | Stripe + PayPal, webhook verification, refunds | Fully operational; no payment gateway work needed |
| **WalletTransaction** | Types: credit_purchase, plan_grant, review_deduction, refund, expiration, manual_adjustment | Add new types (tutoring_deduction, certificate_purchase) |
| **PaymentTransaction** | Stripe/PayPal, status tracking, product types | Extend product types for tutoring, credits |
| **5 Billing Plans** | basic($19.99), premium($49.99), premium-yearly($399.99), intensive($79.99), legacy-trial($0) | Align entitlements matrix to existing tiers |
| **3 Add-Ons** | credits-3, credits-5, priority-review | Extend with new add-on types |
| **2 Coupons** | WELCOME10, REVIEW5 | Existing coupon system works |
| **4 Feature Flags** | ai_scoring_v2, mock_exam_timer, expert_double_review, maintenance_banner | Add new flags per feature |
| **SignupExamTypeCatalog** | OET + IELTS entries | Registration already supports multi-exam |
| **4 AI Config Versions** | gpt-4o (writing/speaking), claude-3.5-sonnet (beta), gpt-3.5-turbo (deprecated) | Extend for conversation, pronunciation |
| **11 Scoped Services** | Including EngagementService, WalletService | Register new services alongside |
| **6 Endpoint Groups** | Auth, Analytics, Notification, Learner, Expert, Admin | Add new endpoint groups |
| **22 Enums** | Including JobType (8 types), AttemptState, ReviewRequestState | Extend enums, don't replace |
| **65+ DB Indexes** | Carefully designed composite indexes | Follow indexing patterns |
| **Attempt.ExamFamilyCode** | Already has ExamFamilyCode column | Multi-exam pivot partially done |

---

## PHASE 1: FOUNDATION ENHANCEMENTS

### Sprint 1.1: Multi-Exam Architecture Completion (Week 1-2)

> **Goal:** Complete the multi-exam data foundation that's partially started.

#### Task 1.1.1: Audit ExamFamily Usage [Day 1]

**Pre-work (no code changes):**
- [ ] Read `ExamFamily` entity fully -- document all fields
- [ ] Grep all usages of `ExamFamilyCode` across backend services
- [ ] Identify which entities already have `ExamFamilyCode` vs which don't
- [ ] Document the gap between current `ExamFamily` and the `ExamType` spec in the mega prompt
- [ ] Decision: extend `ExamFamily` or create parallel `ExamType`? (likely extend)

**Deliverable:** Gap analysis document (comment in code or internal note)

#### Task 1.1.2: TaskType Entity + Migration [Day 2-3]

**Backend:**
- [ ] Create `TaskType` entity in `Domain/ExamTypeEntities.cs` (new file):
  ```
  Id [MaxLength(64)], ExamFamilyCode [MaxLength(16)], SubtestCode [MaxLength(32)],
  Code [MaxLength(64)], Label [MaxLength(128)], Description [MaxLength(512)],
  ConfigJson, CriteriaIdsJson, Status [MaxLength(16)], SortOrder
  ```
- [ ] Register `DbSet<TaskType>` in `LearnerDbContext`
- [ ] Add indexes: `(ExamFamilyCode, SubtestCode, Status)`
- [ ] Create EF Core migration: `AddTaskTypeEntity`
- [ ] Add seed data in `SeedData.cs`:
  - OET Writing: referral-letter, discharge-summary, transfer-letter, case-notes
  - OET Speaking: roleplay
  - OET Reading: part-a-expeditious, part-b-careful
  - OET Listening: part-a-consultation, part-b-workplace, part-c-presentation
  - IELTS Writing: task-1-graph, task-2-essay
  - IELTS Speaking: part-1-intro, part-2-long-turn, part-3-discussion
  - IELTS Reading: section-1, section-2, section-3
  - IELTS Listening: section-1, section-2, section-3, section-4
- [ ] Add `TaskTypeId [MaxLength(64)]` nullable column to `ContentItem` entity
- [ ] Run migration, verify build + tests pass

**Frontend:**
- [ ] Add `TaskType` interface to `lib/types/admin.ts`
- [ ] No UI changes yet (admin can use existing content editor)

**Validation:** `dotnet build` + `dotnet test` + `npm run build`

#### Task 1.1.3: Exam Scoring System Configuration [Day 4]

**Backend:**
- [ ] Add `ScoringSystemJson` and `SubtestDefinitionsJson` fields to `ExamFamily` entity (if not present)
- [ ] Seed scoring configurations:
  - OET: criterion-referenced, 0-500, grade A-E, pass mark 350
  - IELTS: band score, 0-9 in 0.5 increments, descriptors
- [ ] Create a `ScoringService` (lightweight) that resolves score display format based on exam family code
- [ ] Register as scoped service

**Frontend:**
- [ ] Add `ExamType` interface to `lib/types/auth.ts`
- [ ] Add exam type badge component `components/domain/exam-type-badge.tsx`
- [ ] No routing changes yet

**Validation:** All gates pass

#### Task 1.1.4: Exam Switcher UI [Day 5]

**Backend:**
- [ ] Add `GET /v1/reference/exam-families` endpoint (public, cached)
- [ ] Update `GetBootstrapAsync` to include active exam family info

**Frontend:**
- [ ] Add exam type selector to `app/settings/[section]/page.tsx` (Goals section)
- [ ] Add exam type badge to `components/layout/top-nav.tsx`
- [ ] Update dashboard hook to filter by active exam type
- [ ] Add feature flag: `multi_exam_switcher`

**Validation:** All gates pass + manual test of exam switching flow

---

### Sprint 1.2: Adaptive Difficulty Engine (Week 3)

#### Task 1.2.1: Skill Profile Entity + Migration [Day 1]

**Backend:**
- [ ] Create `LearnerSkillProfile` entity in `Domain/Entities.cs`:
  ```
  Id (Guid), UserId [MaxLength(64)], ExamFamilyCode [MaxLength(16)],
  SubtestCode [MaxLength(32)], CriterionCode [MaxLength(32)],
  CurrentRating (double, default 1500), ConfidenceLevel (int, 0-100),
  EvidenceCount (int), RecentScoresJson, LastUpdatedAt
  ```
- [ ] Register DbSet, add indexes: `(UserId, ExamFamilyCode, SubtestCode)`
- [ ] Create migration: `AddLearnerSkillProfile`
- [ ] Add feature flag: `adaptive_difficulty`

#### Task 1.2.2: AdaptiveDifficultyService [Day 2-3]

**Backend:**
- [ ] Create `Services/AdaptiveDifficultyService.cs` (scoped)
- [ ] Implement `UpdateSkillProfileAsync(userId, evaluationId)`:
  - Load evaluation with criterion scores
  - For each criterion score, apply Elo update to matching skill profile
  - K-factor: 32 for <10 evidence, 24 for 10-20, 16 for 20+
  - Expected = 1 / (1 + 10^((difficulty - rating) / 400))
  - New rating = rating + K * (actual - expected)
  - Cap rating range: 800-2400
- [ ] Implement `GetRecommendedContentAsync(userId, examFamilyCode, subtestCode, count)`:
  - Load user's skill profile for the subtest
  - Compute average rating across criteria
  - Select content where `abs(contentDifficultyRating - avgRating) < 200`
  - Order by closest match, then by freshness (not recently attempted)
  - Exclude already-completed content
- [ ] Implement `GetSkillProfileAsync(userId, examFamilyCode)`:
  - Return per-subtest, per-criterion ratings
- [ ] Register in `Program.cs`

#### Task 1.2.3: Difficulty Rating on Content [Day 3]

**Backend:**
- [ ] Add `DifficultyRating` (int, default 1500) column to `ContentItem`
- [ ] Migration: `AddContentDifficultyRating`
- [ ] Map existing difficulty labels to ratings in seed data:
  - "easy" -> 1200, "medium" -> 1500, "hard" -> 1800, "expert" -> 2100

#### Task 1.2.4: Wire Adaptive Engine to Evaluation Flow [Day 4]

**Backend:**
- [ ] In `BackgroundJobProcessor`, after `WritingEvaluation` completes:
  - Call `adaptiveDifficultyService.UpdateSkillProfileAsync(userId, evaluationId)`
- [ ] Same for `SpeakingEvaluation`
- [ ] Add new background job type: `SkillProfileUpdate` to `JobType` enum
  - Alternative: inline call (simpler, acceptable for V1)

#### Task 1.2.5: Frontend Integration [Day 5]

**Backend Endpoints:**
- [ ] `GET /v1/content/recommended?examFamily={code}&subtest={code}&count={n}` (LearnerOnly)
- [ ] `GET /v1/skill-profile` (LearnerOnly)

**Frontend:**
- [ ] Add `SkillProfile` type to `lib/types/auth.ts`
- [ ] Add API functions to `lib/api.ts`
- [ ] Add difficulty badge to existing `TaskCard` component (color-coded)
- [ ] Add "Recommended for You" section to each subtest home page (`app/writing/page.tsx`, `app/speaking/page.tsx`, etc.)
- [ ] Add skill radar chart to `app/progress/page.tsx` using Recharts

**Validation:** All gates pass + verify recommendations change as user practices

---

### Sprint 1.3: Gamification System (Week 4)

> **Note:** `EngagementService` already handles streaks. We extend it rather than replace.

#### Task 1.3.1: Gamification Entities + Migration [Day 1]

**Backend:**
- [ ] Extend or verify `LearnerStreak` fields in existing `EngagementService` data model
  - If streak data is inline in `EngagementService`, consider if a dedicated entity is needed
  - If `EngagementService` already writes streak data to a table, use that
- [ ] Create new entities in `Domain/GamificationEntities.cs`:
  ```
  LearnerXP: UserId [MaxLength(64)] PK, TotalXP (long), WeeklyXP (long),
    MonthlyXP (long), Level (int, default 1), WeekStartDate (DateOnly), MonthStartDate (DateOnly)

  Achievement: Id [MaxLength(64)] PK, Code [MaxLength(64)], Label [MaxLength(128)],
    Description [MaxLength(512)], Category [MaxLength(32)], IconUrl [MaxLength(256)],
    XPReward (int), CriteriaJson, SortOrder, Status [MaxLength(16)]

  LearnerAchievement: Id (Guid) PK, UserId [MaxLength(64)], AchievementId [MaxLength(64)],
    UnlockedAt (DateTimeOffset), Notified (bool)

  LeaderboardEntry: Id (Guid) PK, UserId [MaxLength(64)], DisplayName [MaxLength(128)],
    ExamFamilyCode [MaxLength(16)], Period [MaxLength(16)], PeriodStart (DateOnly),
    XP (long), Rank (int), OptedIn (bool, default true)
  ```
- [ ] Register DbSets, add indexes
- [ ] Migration: `AddGamificationEntities`
- [ ] Seed 30+ achievements (see mega prompt Section 3.4 for full list)
- [ ] Add feature flags: `gamification_xp`, `gamification_leaderboard`

#### Task 1.3.2: XP & Achievement Service [Day 2-3]

**Backend:**
- [ ] Create `Services/GamificationService.cs` (scoped):
  - `AwardXPAsync(userId, action, metadata)` -- award XP, check level-up, check achievements
  - `GetSummaryAsync(userId)` -- streak, XP, level, recent achievements
  - `GetAchievementsAsync(userId)` -- all achievements with unlock status
  - `GetLeaderboardAsync(examFamilyCode, period, page)` -- top 50
  - `OptInLeaderboardAsync(userId, optIn)` -- toggle leaderboard visibility
  - `UseStreakFreezeAsync(userId)` -- use a freeze
  - `CheckAchievementsAsync(userId)` -- evaluate all achievement criteria, unlock new ones
- [ ] Level formula: `Level = floor(sqrt(TotalXP / 100))`
- [ ] XP reset: `WeeklyXP` resets Monday 00:00 UTC, `MonthlyXP` resets 1st of month
- [ ] Register in `Program.cs`

#### Task 1.3.3: Wire XP Awards to Existing Flows [Day 3-4]

**Backend:**
- [ ] After writing evaluation completes: `AwardXPAsync(userId, "writing_evaluation", 25)`
- [ ] After speaking evaluation: `AwardXPAsync(userId, "speaking_evaluation", 25)`
- [ ] After reading attempt submit: `AwardXPAsync(userId, "reading_complete", 15)`
- [ ] After listening attempt submit: `AwardXPAsync(userId, "listening_complete", 15)`
- [ ] After mock exam submit: `AwardXPAsync(userId, "mock_complete", 50)`
- [ ] After diagnostic submit: `AwardXPAsync(userId, "diagnostic_complete", 100)`
- [ ] After expert review received: `AwardXPAsync(userId, "expert_review_received", 25)`
- [ ] On daily streak increment (in `EngagementService`): award streak bonuses
- [ ] Add `AchievementCheck` job type for async achievement evaluation

#### Task 1.3.4: Gamification Endpoints + Frontend [Day 4-5]

**Backend Endpoints:**
- [ ] `GET /v1/gamification/summary` (LearnerOnly)
- [ ] `GET /v1/gamification/achievements` (LearnerOnly)
- [ ] `GET /v1/gamification/leaderboard?period={weekly|monthly}&examFamily={code}` (LearnerOnly)
- [ ] `POST /v1/gamification/leaderboard/opt-in` (LearnerOnly)
- [ ] `POST /v1/gamification/streak/freeze` (LearnerOnly)

**Frontend:**
- [ ] Add types: `GamificationSummary`, `Achievement`, `LeaderboardEntry` to `lib/types/`
- [ ] Add API functions to `lib/api.ts`
- [ ] Dashboard hero: add streak flame icon + day count, XP bar with level
- [ ] Sidebar: persistent streak counter badge
- [ ] `app/achievements/page.tsx` -- achievement gallery (earned highlighted, locked grayed)
- [ ] `app/leaderboard/page.tsx` -- weekly/monthly tabs, opt-in toggle, rank display
- [ ] Achievement unlock toast: `components/domain/achievement-toast.tsx` (subtle, auto-dismiss 3s)
- [ ] Add gamification summary to `GetBootstrapAsync` response

**Validation:** All gates pass + verify XP awards after practice tasks

---

### Sprint 1.4: Vocabulary Builder (Week 5)

#### Task 1.4.1: Vocabulary Entities + Migration [Day 1]

**Backend:**
- [ ] Create entities in `Domain/VocabularyEntities.cs`:
  ```
  VocabularyTerm, LearnerVocabulary, VocabularyQuizResult
  ```
  (See mega prompt Section 3.5 for full field definitions)
- [ ] Register DbSets, indexes on `(ExamFamilyCode, Category, Status)` and `(UserId, Mastery)`
- [ ] Migration: `AddVocabularyEntities`
- [ ] Feature flag: `vocabulary_builder`

#### Task 1.4.2: Seed Vocabulary Data [Day 1-2]

**Backend:**
- [ ] Create seed method `SeedVocabularyTerms` in `SeedData.cs`
- [ ] Seed 100 essential OET medical terms (V1 -- expand to 500 later via AI content generation)
  - Categories: anatomy, symptoms, procedures, medications, clinical_communication
  - Include: term, definition, example sentence, category, difficulty
- [ ] Seed 50 IELTS academic terms (V1 -- expand to 300 later)
  - Source: Academic Word List (AWL) subsets

#### Task 1.4.3: VocabularyService [Day 2-3]

**Backend:**
- [ ] Create `Services/VocabularyService.cs` (scoped):
  - `GetTermsAsync(examFamilyCode, profession, category, page)` -- paginated browse
  - `GetDailySetAsync(userId, count)` -- mix of new terms + due reviews
  - `GetStatsAsync(userId)` -- mastery breakdown (new/learning/reviewing/mastered)
  - `LearnTermAsync(userId, termId)` -- mark term as learning
  - `ReviewTermAsync(userId, termId, quality)` -- SM-2 update (same algorithm as ReviewItem)
  - `StartQuizAsync(userId, quizConfig)` -- generate quiz (10 questions, mixed formats)
  - `SubmitQuizAsync(userId, quizId, answers)` -- score quiz, update mastery
  - `GetQuizHistoryAsync(userId, page)` -- past quiz results
- [ ] Register in `Program.cs`

#### Task 1.4.4: Vocabulary Endpoints + Frontend [Day 3-5]

**Backend Endpoints:**
- [ ] `GET /v1/vocabulary/terms?examFamily={code}&profession={id}&category={cat}&page={n}`
- [ ] `GET /v1/vocabulary/daily-set?count={n}`
- [ ] `GET /v1/vocabulary/stats`
- [ ] `POST /v1/vocabulary/{termId}/learn`
- [ ] `POST /v1/vocabulary/{termId}/review`
- [ ] `POST /v1/vocabulary/quiz/start`
- [ ] `POST /v1/vocabulary/quiz/{quizId}/submit`
- [ ] `GET /v1/vocabulary/quiz/history`

**Frontend:**
- [ ] Add types to `lib/types/vocabulary.ts`
- [ ] Add API functions to `lib/api.ts`
- [ ] `app/vocabulary/page.tsx` -- hub: stats cards (new/learning/mastered), daily set CTA, quiz CTA, browse link
- [ ] `app/vocabulary/flashcards/page.tsx` -- card flip interface (click/tap to reveal), rate buttons (Again/Hard/Good/Easy)
- [ ] `app/vocabulary/quiz/page.tsx` -- quiz with 4 question formats (definition match, fill blank, synonym, context usage)
- [ ] `app/vocabulary/browse/page.tsx` -- full catalogue with search, category filter, mastery filter
- [ ] Add "Vocabulary" link to sidebar navigation
- [ ] Wire XP award: `AwardXPAsync(userId, "vocabulary_quiz", 10)` on quiz completion

**Validation:** All gates pass

---

### Sprint 1.5: Spaced Repetition System (Week 5-6)

#### Task 1.5.1: ReviewItem Entity + Migration [Day 1]

**Backend:**
- [ ] Create `ReviewItem` entity in `Domain/Entities.cs` (see mega prompt Section 3.3)
- [ ] Register DbSet, indexes: `(UserId, DueDate, Status)`, `(UserId, ExamFamilyCode, SubtestCode)`
- [ ] Migration: `AddReviewItemEntity`
- [ ] Feature flag: `spaced_repetition`

#### Task 1.5.2: SpacedRepetitionService [Day 2-3]

**Backend:**
- [ ] Create `Services/SpacedRepetitionService.cs` (scoped):
  - `GetDueItemsAsync(userId, limit)` -- items where DueDate <= today
  - `GetStatsAsync(userId)` -- due today, overdue, mastered total
  - `ReviewItemAsync(userId, itemId, quality)` -- SM-2 update
  - `CreateFromEvaluationAsync(userId, evaluationId)` -- auto-create from evaluation issues
  - `CreateFromVocabularyAsync(userId, termId, question, answer)` -- from failed vocab quiz
- [ ] SM-2 implementation (see mega prompt Section 3.3 for exact algorithm)
- [ ] Register in `Program.cs`

#### Task 1.5.3: Wire Auto-Creation Triggers [Day 3]

**Backend:**
- [ ] After `WritingEvaluation` completes: scan `IssuesJson`, create `ReviewItem` per issue
- [ ] After `SpeakingEvaluation` completes: scan pronunciation/grammar issues
- [ ] After vocabulary quiz: create `ReviewItem` for incorrect answers
- [ ] After grammar exercise: create `ReviewItem` for failed exercises

#### Task 1.5.4: Endpoints + Frontend [Day 4-5]

**Backend Endpoints:**
- [ ] `GET /v1/review-items/due?limit={n}` (LearnerOnly)
- [ ] `GET /v1/review-items/stats` (LearnerOnly)
- [ ] `POST /v1/review-items/{id}/review` (LearnerOnly)

**Frontend:**
- [ ] `app/review/page.tsx` -- review hub: due count badge, stats, "Start Review" CTA
- [ ] `app/review/session/page.tsx` -- card-based review (show question -> flip -> rate)
- [ ] Add "Due for Review" badge to sidebar (count of overdue items)
- [ ] Wire XP: `AwardXPAsync(userId, "review_session", 20)` for 10+ items reviewed

---

### Sprint 1.6: AI Content Generation - Admin Tool (Week 6)

#### Task 1.6.1: ContentGenerationJob Entity [Day 1]

**Backend:**
- [ ] Create `ContentGenerationJob` entity (see mega prompt Section 3.6)
- [ ] Add `ContentGeneration` to `JobType` enum
- [ ] Migration: `AddContentGenerationJob`
- [ ] Feature flag: `ai_content_generation`

#### Task 1.6.2: Content Generation Service [Day 2-3]

**Backend:**
- [ ] Create `Services/ContentGenerationService.cs`:
  - `QueueGenerationAsync(adminUserId, request)` -- create job, enqueue background job
  - `GetJobsAsync(page)` -- list generation jobs
  - `GetJobDetailAsync(jobId)` -- job with generated content IDs
- [ ] Add handler in `BackgroundJobProcessor` for `ContentGeneration`:
  - Load AI config for the task type
  - Call AI service (Gemini/OpenAI) with generation prompt
  - Parse response into `ContentItem` fields
  - Create ContentItem in Draft status with `[AI Generated]` tag
  - Update job with generated content IDs

#### Task 1.6.3: Admin Endpoints + Frontend [Day 4-5]

**Backend Endpoints:**
- [ ] `POST /v1/admin/content/generate` (AdminOnly)
- [ ] `GET /v1/admin/content/generation-jobs` (AdminOnly)
- [ ] `GET /v1/admin/content/generation-jobs/{jobId}` (AdminOnly)

**Frontend:**
- [ ] Add "Generate Content" button to admin content library page
- [ ] Generation dialog: exam type, subtest, task type, profession, difficulty, count (1-10)
- [ ] Show generation job status in content list

---

## PHASE 2: AI-FIRST FEATURES

### Sprint 2.1: AI Conversation Practice (Week 7-9)

> **This is the flagship feature. Allocate 3 weeks.**

#### Task 2.1.1: Entities + Migration [Day 1-2 of Week 7]

**Backend:**
- [ ] Create `ConversationSession` and `ConversationTurn` entities in `Domain/ConversationEntities.cs`
- [ ] Add `ConversationEvaluation` to `JobType` enum
- [ ] Register DbSets, indexes
- [ ] Migration: `AddConversationEntities`
- [ ] Feature flag: `ai_conversation_practice`

#### Task 2.1.2: ConversationHub (SignalR) [Day 3-5 of Week 7]

**Backend:**
- [ ] Create `Hubs/ConversationHub.cs` (SignalR hub):
  - `StartSession(sessionId)` -- join room, initialize AI context
  - `SendAudio(sessionId, audioBase64)` -- receive audio chunk from client
  - `EndSession(sessionId)` -- finalize conversation
- [ ] Map hub: `app.MapHub<ConversationHub>("/v1/conversations/hub").RequireAuthorization()`
- [ ] Implement STT integration:
  - Create `Services/SpeechToTextService.cs`
  - Support Deepgram WebSocket API (primary) or Azure Speech (fallback)
  - Configuration: `AIConversationOptions` in `Configuration/`
- [ ] Implement AI response generation:
  - Use existing AI config infrastructure
  - System prompt: exam-specific roleplay instructions + scenario context + conversation history
  - Generate contextual patient/interlocutor responses
  - Return text response to client via SignalR: `ReceiveAIResponse(text)`

#### Task 2.1.3: Conversation Service [Week 8, Day 1-3]

**Backend:**
- [ ] Create `Services/ConversationService.cs` (scoped):
  - `CreateSessionAsync(userId, contentId, examFamilyCode, taskTypeCode)` -- create session with scenario
  - `GetSessionAsync(userId, sessionId)` -- get session state
  - `CompleteSessionAsync(userId, sessionId)` -- mark complete, queue evaluation
  - `GetEvaluationAsync(userId, sessionId)` -- get post-conversation evaluation
  - `GetHistoryAsync(userId, page)` -- past sessions
- [ ] Register in `Program.cs`
- [ ] Add `ConversationEvaluation` handler in `BackgroundJobProcessor`:
  - Load full conversation transcript
  - Send to AI for evaluation against exam criteria
  - Generate per-turn annotations + overall scores + improvement suggestions
  - Create `Evaluation` entity linked to conversation

#### Task 2.1.4: REST Endpoints [Week 8, Day 3-4]

**Backend Endpoints:**
- [ ] `POST /v1/conversations` (LearnerOnly)
- [ ] `GET /v1/conversations/{sessionId}` (LearnerOnly)
- [ ] `POST /v1/conversations/{sessionId}/complete` (LearnerOnly)
- [ ] `GET /v1/conversations/{sessionId}/evaluation` (LearnerOnly)
- [ ] `GET /v1/conversations/history?page={n}` (LearnerOnly)

#### Task 2.1.5: Frontend - Conversation UI [Week 8 Day 5 - Week 9]

**Frontend:**
- [ ] Add types: `ConversationSession`, `ConversationTurn` to `lib/types/conversation.ts`
- [ ] Add API functions + SignalR connection logic to `lib/api.ts`
- [ ] `app/conversation/page.tsx` -- hub: select scenario, view history, feature explanation
- [ ] `app/conversation/[sessionId]/page.tsx` -- **full-screen immersive conversation**:
  - Split layout: scenario card (left sidebar) + conversation area (right)
  - Speech bubbles for learner and AI turns
  - Waveform visualizer (wavesurfer.js -- already in dependencies) during recording
  - Real-time transcript display as learner speaks
  - AI response with typing indicator animation
  - Timer (5-minute roleplay) and turn counter
  - "End Conversation" button (prominent)
  - Preparation phase (2 min countdown before conversation starts)
- [ ] `app/conversation/[sessionId]/results/page.tsx` -- evaluation display:
  - Overall scores per criterion
  - Per-turn annotations (highlight good phrases, flag errors)
  - Improvement suggestions
  - "Practice Again" CTA
- [ ] Add "AI Conversation" to speaking hub page + sidebar navigation
- [ ] Wire XP: 25 XP on conversation completion

**Validation:** Full end-to-end test with audio recording and AI response

---

### Sprint 2.2: Pronunciation Analysis (Week 10)

#### Task 2.2.1: Entities + Azure Integration [Day 1-2]

**Backend:**
- [ ] Create entities: `PronunciationAssessment`, `PronunciationDrill`, `LearnerPronunciationProgress`
- [ ] Migration: `AddPronunciationEntities`
- [ ] Create `Services/PronunciationService.cs`:
  - Integrate Azure Cognitive Services Speech SDK
  - `AnalyzeAudioAsync(audioUrl, referenceText)` -- phoneme-level assessment
  - `GetProfileAsync(userId)` -- weak phonemes, progress
  - `GetDrillsAsync()` -- available drills
  - `SubmitDrillAttemptAsync(userId, drillId, audioUrl)` -- assess drill audio
- [ ] Configuration: `PronunciationOptions` (AzureSpeechKey, AzureSpeechRegion)
- [ ] Add `PronunciationAnalysis` to `JobType` enum
- [ ] Wire into existing speaking evaluation: after `SpeakingEvaluation` job, queue `PronunciationAnalysis`
- [ ] Seed 20+ pronunciation drills (th, r/l, short/long vowels, consonant clusters, word stress)
- [ ] Feature flag: `pronunciation_analysis`

#### Task 2.2.2: Endpoints + Frontend [Day 3-5]

**Backend Endpoints:**
- [ ] `GET /v1/pronunciation/profile` (LearnerOnly)
- [ ] `GET /v1/pronunciation/drills` (LearnerOnly)
- [ ] `GET /v1/pronunciation/drills/{drillId}` (LearnerOnly)
- [ ] `POST /v1/pronunciation/drills/{drillId}/attempt` (LearnerOnly)
- [ ] `GET /v1/pronunciation/assessment/{id}` (LearnerOnly)

**Frontend:**
- [ ] `app/pronunciation/page.tsx` -- hub: overall score, weak phonemes list, drill recommendations
- [ ] `app/pronunciation/[drillId]/page.tsx` -- drill practice (model audio, record, compare, per-word scores)
- [ ] Extend `app/speaking/results/[id]/page.tsx` -- add pronunciation section with word heatmap
- [ ] Add to sidebar under Speaking section

---

### Sprint 2.3: AI Writing Coach (Week 11)

#### Task 2.3.1: Entities + Service [Day 1-3]

**Backend:**
- [ ] Create `WritingCoachSession`, `WritingCoachSuggestion` entities
- [ ] Migration: `AddWritingCoachEntities`
- [ ] Create `Services/WritingCoachService.cs`:
  - `CheckTextAsync(attemptId, currentText, cursorPosition)` -- send to AI, return suggestions
  - `ResolveSuggestionAsync(suggestionId, resolution)` -- accept/dismiss
  - `GetStatsAsync(attemptId)` -- acceptance rate, suggestion breakdown
- [ ] Feature flag: `ai_writing_coach`

#### Task 2.3.2: Endpoints + Frontend [Day 4-5]

**Backend Endpoints:**
- [ ] `POST /v1/writing/attempts/{attemptId}/coach-check` (LearnerOnly)
- [ ] `POST /v1/writing/coach-suggestions/{id}/resolve` (LearnerOnly)

**Frontend:**
- [ ] Extend existing writing editor with suggestion overlay:
  - Debounced text check (2-second pause after typing)
  - Underline suggestions (wavy: blue=grammar, green=vocabulary, yellow=structure)
  - Click underline -> tooltip with suggestion + explanation + accept/dismiss
  - Toggle coach on/off button in editor toolbar
  - Coach disabled during mock exams (feature flag check)
- [ ] Coach stats panel in writing results page

---

### Sprint 2.4: Performance Prediction (Week 12)

#### Task 2.4.1: PredictionSnapshot Entity + Service [Day 1-3]

**Backend:**
- [ ] Create `PredictionSnapshot` entity
- [ ] Migration: `AddPredictionSnapshot`
- [ ] Create `Services/PredictionService.cs`:
  - Weighted moving average of last 20 evaluation scores
  - Difficulty adjustment, consistency bonus/penalty
  - Confidence levels: <5 evals = "insufficient", 5-10 = "low", 10-20 = "moderate", 20+ = "good"
  - `ComputePredictionAsync(userId, examFamilyCode, subtestCode)`
  - `GetPredictionsAsync(userId, examFamilyCode)`
- [ ] Add `PredictionComputation` to `JobType` enum
- [ ] Trigger after each evaluation completion
- [ ] Feature flag: `score_prediction`

#### Task 2.4.2: Endpoints + Frontend [Day 4-5]

**Backend Endpoints:**
- [ ] `GET /v1/predictions` (LearnerOnly)
- [ ] `POST /v1/predictions/refresh` (LearnerOnly)

**Frontend:**
- [ ] Add "Predicted Score" card to `app/readiness/page.tsx`:
  - Score range bar visualization (low-mid-high)
  - Confidence indicator
  - Trend arrow (improving/stable/declining)
  - Disclaimer text
- [ ] Add prediction summary to dashboard hero
- [ ] Add to `app/progress/page.tsx`

---

## PHASE 3: COMPLETE ECOSYSTEM

### Sprint 3.1: Grammar Lessons (Week 13-14)

- [ ] Entities: `GrammarLesson`, `LearnerGrammarProgress`
- [ ] `Services/GrammarService.cs` with lesson browse, start, submit, progress
- [ ] Endpoints: 5 endpoints under `/v1/grammar/`
- [ ] Seed 20+ lessons (tenses, articles, prepositions, passive voice, conditionals, modals, formal register)
- [ ] Frontend: `app/grammar/page.tsx` (catalogue), `app/grammar/[lessonId]/page.tsx` (viewer + exercises)
- [ ] Integration: evaluation grammar issues link to relevant lessons

### Sprint 3.2: Video Lessons + Strategy Guides (Week 15-16)

- [ ] Entities: `VideoLesson`, `LearnerVideoProgress`, `StrategyGuide`
- [ ] `Services/LessonService.cs` (covers both video and strategy)
- [ ] Endpoints: 7 endpoints under `/v1/lessons/` and `/v1/strategies/`
- [ ] Seed placeholder video lessons (10+) and strategy guides (20+)
- [ ] Frontend: `app/lessons/page.tsx`, `app/lessons/[id]/page.tsx`, `app/strategies/page.tsx`, `app/strategies/[id]/page.tsx`

### Sprint 3.3: Community Features (Week 17-18)

- [ ] Entities: `ForumCategory`, `ForumThread`, `ForumReply`, `StudyGroup`, `StudyGroupMember`
- [ ] `Services/CommunityService.cs` (threads, replies, groups, moderation)
- [ ] Endpoints: 13 endpoints under `/v1/community/`
- [ ] Seed forum categories for each exam type
- [ ] Frontend: `app/community/page.tsx`, thread view, new thread, group browser, group detail
- [ ] Admin: moderation tools (pin, lock, delete)

### Sprint 3.4: Live Tutoring (Week 19-20)

- [ ] Entities: `TutoringSession`, `TutoringAvailability`
- [ ] `Services/TutoringService.cs` with Daily.co API integration
- [ ] Configuration: `VideoCallOptions` (Daily.co API key)
- [ ] Endpoints: 9 endpoints under `/v1/tutoring/`
- [ ] Frontend: tutor browser, calendar booking, video session page
- [ ] Extend `WalletService` with `tutoring_deduction` transaction type

### Sprint 3.5: Certificates, Referrals, Sponsors (Week 21-24)

- [ ] **Certificates (Week 21):** `Certificate` entity, QuestPDF generation, `CertificateGeneration` job
- [ ] **Referrals (Week 22):** `ReferralCode`, `Referral` entities, `ReferralConversion` job
- [ ] **Sponsor Dashboard (Week 23):** `SponsorAccount`, `SponsorLearnerLink` entities, privacy-respecting dashboard
- [ ] **Cohort Management (Week 24):** `Cohort`, `CohortMember` entities, aggregated progress reports

---

## PHASE 4: SCALE & POLISH

### Sprint 4.1: Accessibility + i18n (Week 25-28)

- [ ] WCAG 2.2 AA audit across all pages
- [ ] Fix keyboard navigation, focus indicators, color contrast
- [ ] High-contrast and reduced-motion modes
- [ ] Extract UI strings to locale files
- [ ] Add language selector
- [ ] Arabic RTL support

### Sprint 4.2: Offline Mode + Exam Booking (Week 29-32)

- [ ] Capacitor offline sync service
- [ ] Cache vocabulary, grammar, strategy content offline
- [ ] Exam booking entity + partner integration
- [ ] Offline attempt queue with sync-on-reconnect

### Sprint 4.3: Content Marketplace (Week 33-36)

- [ ] `ContentContributor`, `ContentSubmission` entities
- [ ] Educator submission portal
- [ ] Admin review queue integration
- [ ] Revenue share tracking

---

## DEPENDENCY GRAPH

```
Week 1-2:  Multi-Exam Foundation ─────────────────────────────┐
                  │                                            │
Week 3:    Adaptive Difficulty ←── depends on ExamFamily       │
                  │                                            │
Week 4:    Gamification (extends EngagementService) ──────┐    │
                  │                                       │    │
Week 5:    Vocabulary Builder ─────────────────────────┐  │    │
           Spaced Repetition ←── triggered by evals   │  │    │
                  │                                    │  │    │
Week 6:    AI Content Generation (Admin)               │  │    │
                  │                                    │  │    │
Week 7-9:  AI Conversation Practice (FLAGSHIP) ←───────┼──┼────┘
                  │                                    │  │
Week 10:   Pronunciation Analysis ←── uses STT from ──┘  │
                  │                                       │
Week 11:   AI Writing Coach                               │
                  │                                       │
Week 12:   Performance Prediction ←── needs eval data ────┘
                  │
Week 13+:  Phase 3 features (independent of each other)
```

**Critical Path:** Multi-Exam Foundation -> Adaptive Difficulty -> AI Conversation Practice

**Parallelizable:**
- Gamification + Vocabulary Builder (no dependencies on each other)
- Grammar Lessons + Video Lessons + Strategy Guides (independent)
- Community + Live Tutoring (independent)
- Certificates + Referrals + Sponsors (independent)

---

## RISK REGISTER

| Risk | Impact | Mitigation |
|------|--------|-----------|
| STT (Deepgram) latency too high for real-time conversation | Conversation feature feels laggy | Pre-buffer audio chunks, use WebSocket streaming, fallback to turn-based mode |
| AI response quality inconsistent for roleplay | Bad learner experience | Extensive prompt engineering, use AI config versioning, human QA review of scenarios |
| Vocabulary seed data quality | Incorrect medical terms | Medical professional review before production launch |
| Pronunciation analysis accuracy | Misleading feedback | Azure Speech SDK confidence thresholds -- only show high-confidence assessments |
| Feature bloat overwhelming learners | Information overload | Feature flags control visibility, onboarding guides per feature, progressive disclosure |
| Database migration on production data | Data corruption risk | All migrations additive (no drops), test on staging with production data snapshot |
| Payment integration edge cases | Lost revenue | WalletService already handles concurrency with optimistic locking -- extend carefully |

---

## DEFINITION OF DONE (PER FEATURE)

- [ ] All entity fields match `[MaxLength]` + `*Json` conventions
- [ ] DbSets registered with appropriate indexes
- [ ] Migration runs clean on fresh + existing databases
- [ ] Service methods follow `Get*Async`/`Create*Async` naming
- [ ] Errors use `ApiException` with typed `ErrorCode`
- [ ] Endpoints have `RequireAuthorization` + `RequireRateLimiting`
- [ ] Feature flag seeded and checked in service + frontend
- [ ] Entitlement check for premium features
- [ ] Backend tests: `dotnet build` + `dotnet test` pass
- [ ] Frontend types, API functions, and pages created
- [ ] Frontend tests: `npm run lint` + `npm test` + `npm run build` pass
- [ ] Loading states, empty states, error states handled
- [ ] Responsive at 375px, 768px, 1024px, 1440px
- [ ] XP awards wired for user-facing actions
- [ ] Analytics events tracked for new user actions

---

## SPRINT CALENDAR SUMMARY

| Week | Sprint | Features | New Entities | New Endpoints |
|------|--------|----------|-------------|---------------|
| 1-2 | 1.1 | Multi-Exam Architecture | TaskType + schema updates | 2 |
| 3 | 1.2 | Adaptive Difficulty | LearnerSkillProfile | 2 |
| 4 | 1.3 | Gamification | LearnerXP, Achievement, LearnerAchievement, LeaderboardEntry | 5 |
| 5 | 1.4+1.5 | Vocabulary + Spaced Repetition | VocabularyTerm, LearnerVocabulary, VocabularyQuizResult, ReviewItem | 11 |
| 6 | 1.6 | AI Content Generation | ContentGenerationJob | 3 |
| 7-9 | 2.1 | AI Conversation Practice | ConversationSession, ConversationTurn | 5 + SignalR hub |
| 10 | 2.2 | Pronunciation Analysis | PronunciationAssessment, PronunciationDrill, LearnerPronunciationProgress | 5 |
| 11 | 2.3 | AI Writing Coach | WritingCoachSession, WritingCoachSuggestion | 2 |
| 12 | 2.4 | Performance Prediction | PredictionSnapshot | 2 |
| 13-14 | 3.1 | Grammar Lessons | GrammarLesson, LearnerGrammarProgress | 5 |
| 15-16 | 3.2 | Video + Strategy | VideoLesson, LearnerVideoProgress, StrategyGuide | 7 |
| 17-18 | 3.3 | Community | ForumCategory, ForumThread, ForumReply, StudyGroup, StudyGroupMember | 13 |
| 19-20 | 3.4 | Live Tutoring | TutoringSession, TutoringAvailability | 9 |
| 21-24 | 3.5 | Certificates, Referrals, Sponsors, Cohorts | 8 entities | 15+ |
| 25-36 | Phase 4 | i18n, a11y, offline, booking, marketplace | 3 entities | 10+ |

**Totals: 36 weeks | 42 new entities | 96+ new endpoints | 25+ new frontend pages**

---

**END OF IMPLEMENTATION PLAN**

*This plan is reality-checked against the actual codebase state as of April 4, 2026. It accounts for existing services (EngagementService, WalletService, PaymentGatewayService), existing entities (ExamFamily, WalletTransaction, PaymentTransaction), and existing billing infrastructure (5 plans, Stripe + PayPal). Every task is actionable and testable.*
