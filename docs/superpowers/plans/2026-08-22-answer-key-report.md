# Answer Key Report Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Reading and Listening candidates report a potentially incorrect official answer after submit, and give content admins a dedicated queue to review those reports and deep-link into existing key-edit / re-mark tools.

**Architecture:** New `AssessmentAnswerKeyReport` table plus a focused service. Learner POST/GET hang off existing Reading/Listening attempt groups. Admin list/detail/patch live at `/v1/admin/answer-key-reports`. Candidate UI is a shared `ReportAnswerControl` on official results/review cards. Admin UI clones the leak-report queue. Flag, Escalations, and Ask AI stay unchanged.

**Tech Stack:** ASP.NET Core Minimal APIs, EF Core + hand-authored migration, Next.js App Router, Vitest + Testing Library, xUnit + `TestWebApplicationFactory`.

## File map

Create:

- `backend/src/OetLearner.Api/Domain/AssessmentAnswerKeyReportEntities.cs`
- `backend/src/OetLearner.Api/Contracts/AnswerKeyReportContracts.cs`
- `backend/src/OetLearner.Api/Services/AnswerKeyReportService.cs`
- `backend/src/OetLearner.Api/Endpoints/AnswerKeyReportAdminEndpoints.cs`
- `backend/src/OetLearner.Api/Data/Migrations/20260904140000_AddAssessmentAnswerKeyReports.cs`
- `backend/tests/OetLearner.Api.Tests/AnswerKeyReportEndpointTests.cs`
- `components/domain/results/report-answer-control.tsx`
- `components/domain/results/report-answer-control.test.tsx`
- `app/admin/content/answer-reports/page.tsx`
- `app/admin/content/answer-reports/page.test.tsx`

Modify:

- `backend/src/OetLearner.Api/Data/LearnerDbContext.cs` — DbSet + indexes
- `backend/src/OetLearner.Api/Data/Migrations/LearnerDbContextModelSnapshot.cs`
- `backend/src/OetLearner.Api/Program.cs` — scoped service + Map*
- `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`
- `backend/src/OetLearner.Api/Endpoints/ListeningLearnerEndpoints.cs`
- `lib/reading-authoring-api.ts`
- `lib/listening-api.ts`
- `lib/api.ts` — admin list/get/update
- `lib/admin-navigation.tsx`
- `lib/admin-permissions.ts`
- `lib/__tests__/admin-navigation.test.tsx`
- `lib/__tests__/admin-permissions.test.ts`
- `app/reading/paper/[paperId]/results/page.tsx`
- `app/listening/results/[id]/page.tsx`
- `app/listening/review/[id]/page.tsx`
- `PROGRESS.md`

Do not touch exam Flag, Escalations, mock results, or scoring-system internals.

---

### Task 1: Failing backend endpoint tests

**Files:**
- Create: `backend/tests/OetLearner.Api.Tests/AnswerKeyReportEndpointTests.cs`

- [ ] **Step 1: Write the failing tests** covering:
  - Learner POST on a submitted own Reading attempt creates a report and writes `AnswerKeyReport.Created`.
  - Learner GET returns that report for the attempt.
  - Duplicate pending report → 409 `answer_key_report_already_open`.
  - Other user's attempt → 404.
  - In-progress attempt → 400 `answer_key_report_unavailable`.
  - Unknown question → 404.
  - Listening POST works the same on a submitted Listening attempt.
  - Admin GET list with ContentRead returns the seeded row, display name, no email, editor URL.
  - Admin PATCH with ContentWrite updates status + resolution note + `AnswerKeyReport.Updated`.
  - Terminal status locked → 400 `answer_key_report_status_locked`.
  - PATCH without ContentWrite → 403.
- [ ] **Step 2: Run the filter and confirm the tests fail because the routes/types do not exist.**

```powershell
dotnet test "backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj" --filter "FullyQualifiedName~AnswerKeyReport"
```

---

### Task 2: Entity, migration, snapshot, DbSet

**Files:**
- Create: `backend/src/OetLearner.Api/Domain/AssessmentAnswerKeyReportEntities.cs`
- Create: `backend/src/OetLearner.Api/Data/Migrations/20260904140000_AddAssessmentAnswerKeyReports.cs`
- Modify: `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`
- Modify: `backend/src/OetLearner.Api/Data/Migrations/LearnerDbContextModelSnapshot.cs`

- [ ] **Step 1: Add the entity + status/reason constants** matching the spec.
- [ ] **Step 2: Add `DbSet<AssessmentAnswerKeyReport>` next to `CustomerSupportCases` and fluent indexes in `OnModelCreating`.**
- [ ] **Step 3: Hand-author the migration** (`[Migration("20260904140000_AddAssessmentAnswerKeyReports")]`) and insert the snapshot block after `CustomerSupportCase`.

---

### Task 3: Service + contracts + endpoints

**Files:**
- Create: `backend/src/OetLearner.Api/Contracts/AnswerKeyReportContracts.cs`
- Create: `backend/src/OetLearner.Api/Services/AnswerKeyReportService.cs`
- Create: `backend/src/OetLearner.Api/Endpoints/AnswerKeyReportAdminEndpoints.cs`
- Modify: `backend/src/OetLearner.Api/Endpoints/ReadingLearnerEndpoints.cs`
- Modify: `backend/src/OetLearner.Api/Endpoints/ListeningLearnerEndpoints.cs`
- Modify: `backend/src/OetLearner.Api/Program.cs`

- [ ] **Step 1: Add request/response records** (`AnswerKeyReportCreateRequest`, learner summary, admin summary with `editorUrl` + `scoringSystemUrl`).
- [ ] **Step 2: Implement `AnswerKeyReportService`** — create/list-for-attempt, admin list/get/update, snapshot truncation, pending uniqueness, terminal lock, privacy (display name only), audit events.
- [ ] **Step 3: Map learner POST/GET** on both paper groups. Map admin GET/GET-by-id/PATCH. Register scoped service next to `CustomerSupportCaseService`.
- [ ] **Step 4: Re-run the backend filter and make it green.**

---

### Task 4: Failing frontend control test, then the control

**Files:**
- Create: `components/domain/results/report-answer-control.test.tsx`
- Create: `components/domain/results/report-answer-control.tsx`
- Modify: `lib/reading-authoring-api.ts`
- Modify: `lib/listening-api.ts`

- [ ] **Step 1: Write the failing RTL test** — opens modal, requires a reason, submits, shows Reported, surfaces the “does not change your mark” copy.
- [ ] **Step 2: Add API helpers** `list/create` on reading + listening attempt paths.
- [ ] **Step 3: Implement `ReportAnswerControl`** with Modal + reason chips + details + toast.
- [ ] **Step 4: Run `pnpm exec vitest run components/domain/results/report-answer-control.test.tsx` and make it green.**

---

### Task 5: Wire candidate results pages

**Files:**
- Modify: `app/reading/paper/[paperId]/results/page.tsx`
- Modify: `app/listening/results/[id]/page.tsx`
- Modify: `app/listening/review/[id]/page.tsx`

- [ ] **Step 1: Insert `ReportAnswerControl`** into Reading `ReviewItemDetails`, Listening official expanded panel, and Listening review `AnswerComparisonCard` children.
- [ ] **Step 2: Load existing reports for the attempt** so already-reported questions show Reported.

---

### Task 6: Admin queue + nav + permissions

**Files:**
- Create: `app/admin/content/answer-reports/page.tsx`
- Create: `app/admin/content/answer-reports/page.test.tsx`
- Modify: `lib/api.ts`
- Modify: `lib/admin-navigation.tsx`
- Modify: `lib/admin-permissions.ts`
- Modify: `lib/__tests__/admin-navigation.test.tsx`
- Modify: `lib/__tests__/admin-permissions.test.ts`

- [ ] **Step 1: Add admin API helpers** mirroring leak reports.
- [ ] **Step 2: Clone leak-report page** with assessment filter, snapshots, editor + scoring-system links.
- [ ] **Step 3: Register nav item, title, `hubSubRoutes` `answer-reports`, and both permission maps (`content:read`).**
- [ ] **Step 4: Update nav/permission tests and add a page test for the loaded-filter count pattern.**
- [ ] **Step 5: Run focused vitest for the new page + nav/permission files.**

---

### Task 7: Verify, document, ship

**Files:**
- Modify: `PROGRESS.md`
- Modify: `.github/agent-state.local.md` only if the current handoff should mention this feature (keep Reading-upload next-step intact).

- [ ] **Step 1: Re-run the two focused checks** (dotnet AnswerKeyReport + vitest report-answer-control / answer-reports).
- [ ] **Step 2: Compact PROGRESS note.**
- [ ] **Step 3: Commit explicit paths and push `main`.**
