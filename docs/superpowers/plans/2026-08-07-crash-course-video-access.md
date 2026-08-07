# Critical Course Video Access Rule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the existing Crash Course Writing access rule to every full-course plan and immutable plan-version snapshot in production.

**Architecture:** Keep access decisions in `BillingPlan.ContentOverridesJson`, which is already resolved by `EffectiveEntitlementResolver` and evaluated by `VideoEntitlementService`. Add one idempotent data migration that removes blocked IDs from full-course includes and merges them into full-course excludes in both plan tables; do not add learner-specific exceptions or change video-library service code.

**Tech Stack:** ASP.NET Core Minimal API, EF Core 10 PostgreSQL migrations, xUnit, PowerShell host validation, GitHub Actions GHCR blue/green deployment.

## Global Constraints

- Preserve unrelated dirty worktree changes and the existing local `main` commit.
- Never edit secrets, `.env*`, credentials, or customer data.
- Do not run builds, tests, or source builds on the VPS; production only pulls prebuilt GHCR images and runs health gates.
- Preserve all non-Crash-Course content overrides and the existing Crash Course plan behavior.
- Update both mutable `BillingPlans` and immutable `BillingPlanVersions` so already-active subscriptions are covered.

---

### Task 1: Add entitlement regression coverage

**Files:**
- Create: `backend/tests/OetLearner.Api.Tests/CrashCourseVideoAccessRuleTests.cs`
- Read: `backend/src/OetLearner.Api/Services/VideoLibrary/VideoEntitlementService.cs`

**Interfaces:**
- Consumes: `VideoEntitlementService.Evaluate(VideoAccessContext, LibraryVideo)`.
- Produces: four focused xUnit tests that pin the package-mapping semantics used by the migration.

- [x] **Step 1: Add the full-course exclusion test**

Create a premium `VideoAccessContext` with `ProfessionId = "medicine"` and
`VideoExcludes = { "crash-writing" }`. Evaluate a premium Writing video with
that ID and assert `Allowed == false` and `Reason == "plan_excludes_video"`.

- [x] **Step 2: Add the unaffected Writing test**

Evaluate a different premium Writing video under the same unrestricted
full-course context with no exclusion for that ID and assert it is allowed via
`plan_grants_video_library`.

- [x] **Step 3: Add the Crash Course include/scope tests**

Use `AllSubtestsGranted = false`, `GrantedSubtests = { "listening", "reading", "speaking" }`, and `VideoIncludes = { "crash-writing" }`:

```csharp
var crashContext = new VideoAccessContext(
    IsAdmin: false,
    Authenticated: true,
    HasEligibleSubscription: true,
    Frozen: false,
    Expired: false,
    PlanGrantsPremium: true,
    AddOnGrantsPremium: false,
    CurrentTier: "premium",
    AllSubtestsGranted: false,
    GrantedSubtests: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "listening", "reading", "speaking" },
    VideoIncludes: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "crash-writing" });
```

Assert that the explicitly included Writing video is allowed, while a
different Writing video is denied with `plan_does_not_grant_subtest`.

- [ ] **Step 4: Run only the new focused tests**

Run:

```powershell
dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~CrashCourseVideoAccessRuleTests" --nologo
```

Expected: all four tests pass.

### Task 2: Add the corrective full-course data migration

**Files:**
- Create: `backend/src/OetLearner.Api/Data/Migrations/20260831090000_ApplyCrashCourseVideoExclusionsToFullCourses.cs`
- Test: `backend/tests/OetLearner.Api.Tests/CrashCourseVideoAccessMigrationTests.cs`

**Interfaces:**
- Consumes: the 18 canonical live Crash Course Writing video IDs from the existing `20260822090000_RestrictCrashCourseVideoAccess` migration.
- Produces: migration SQL that applies to every `Code LIKE 'full-%'` row in both plan tables.

- [x] **Step 1: Add migration-shape tests**

Derive a test-only migration wrapper, invoke the protected `Up` method with an
Npgsql `MigrationBuilder`, and inspect the two `SqlOperation` strings. Assert
that they target `BillingPlans` and `BillingPlanVersions`, use the `full-%`
predicate, update `videos.include` and `videos.exclude`, and contain exactly
18 distinct `vid_` IDs.

- [x] **Step 2: Add a PostgreSQL-only migration**

Use the next migration timestamp after the current
`20260830090000_BackfillDeviceVerificationExemptionEmail` migration. Guard
`Up` and `Down` with the Npgsql provider check.

For each table, use `jsonb_set` and `jsonb_array_elements` to:

```sql
-- Conceptual shape; the migration emits this once per table.
UPDATE "BillingPlans" AS p
SET "ContentOverridesJson" = CAST(
    jsonb_set(
        jsonb_set(base, '{videos,include}', filtered_includes, true),
        '{videos,exclude}', merged_excludes, true
    ) AS text),
    "UpdatedAt" = now()
FROM (
    SELECT "Id", CAST(COALESCE("ContentOverridesJson", '{}') AS jsonb) AS base
    FROM "BillingPlans"
    WHERE "Code" LIKE 'full-%'
) AS source
WHERE p."Id" = source."Id";
```

The actual SQL must filter all 18 blocked IDs out of `videos.include`, merge
them into `videos.exclude`, preserve unrelated overrides, and de-duplicate
the resulting JSON array. Use the same canonical ID list for both tables.

- [x] **Step 3: Keep rollback safe**

Leave `Down` as a documented no-op. This migration is additive/corrective and
must not remove an earlier rule or administrator-managed exclusions during a
rollback.

- [ ] **Step 4: Run migration-shape tests**

Run:

```powershell
dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~CrashCourseVideoAccessMigrationTests" --nologo
```

Expected: all migration SQL assertions pass.

### Task 3: Review and validate the release candidate

**Files:**
- Review: the new migration and both new test files.
- Review: `.github/agent-state.local.md`.

- [ ] **Step 1: Inspect the focused diff**

Run:

```powershell
git diff --check
git diff -- backend/src/OetLearner.Api/Data/Migrations/20260831090000_ApplyCrashCourseVideoExclusionsToFullCourses.cs backend/tests/OetLearner.Api.Tests/CrashCourseVideoAccessRuleTests.cs backend/tests/OetLearner.Api.Tests/CrashCourseVideoAccessMigrationTests.cs
```

Confirm no existing migration, plan, video, or unrelated user file was edited.

- [ ] **Step 2: Run the single lightweight backend check**

Run the focused migration and entitlement tests together:

```powershell
dotnet test backend/tests/OetLearner.Api.Tests/OetLearner.Api.Tests.csproj --filter "FullyQualifiedName~CrashCourseVideoAccessRuleTests|FullyQualifiedName~CrashCourseVideoAccessMigrationTests" --nologo
```

Do not run the full test/build marathon locally; GitHub Actions performs the
production image build and deployment gates.

- [ ] **Step 3: Update the continuity handoff**

Record the changed files, focused test command/result, current branch/commit
state, and the next step (commit/push and production verification) in
`.github/agent-state.local.md`.

### Task 4: Commit, push, deploy, and prove the live rule

**Files:**
- Stage explicitly: the design/plan docs, migration, tests, and handoff only.

- [ ] **Step 1: Commit the focused release**

Run:

```powershell
git add docs/superpowers/specs/2026-08-07-crash-course-video-access-design.md docs/superpowers/plans/2026-08-07-crash-course-video-access.md backend/src/OetLearner.Api/Data/Migrations/20260831090000_ApplyCrashCourseVideoExclusionsToFullCourses.cs backend/tests/OetLearner.Api.Tests/CrashCourseVideoAccessRuleTests.cs backend/tests/OetLearner.Api.Tests/CrashCourseVideoAccessMigrationTests.cs .github/agent-state.local.md
git commit -m "fix(video-library): protect crash course videos from full courses"
```

Do not stage `TrustedDeviceService.cs`, `TrustedDeviceServiceTests.cs`,
`lib/device-id.ts`, `lib/device-id.test.ts`, `.codex/config.toml`, or
`.superpowers/`.

- [ ] **Step 2: Push `main` and wait for the deploy workflow**

Run `git push origin main`, then monitor the workflow for the exact pushed SHA
with `gh run list --workflow deploy.yml --commit <sha>` and `gh run view <run-id> --log-failed` if needed.

- [ ] **Step 3: Verify the deployed VPS slot**

Over read-only SSH, verify `/opt/oetwebapp` is at the pushed SHA, the active
green/blue image labels match it, and `oet-web`, `oet-api`, and `oet-postgres`
are healthy. Never run a VPS build or destructive volume command.

- [ ] **Step 4: Verify database access mappings**

Read-only SQL must confirm:

- `20260831090000_ApplyCrashCourseVideoExclusionsToFullCourses` exists in
  `__EFMigrationsHistory`.
- Every `full-%` plan and plan-version row has 18 excluded IDs and zero of the
  blocked IDs in its include list.
- The three Crash Course plans still have 18 included IDs and the
  Listening/Reading/Speaking subtest scope.
- The live video table still contains all 18 blocked videos with the expected
  new/old Crash Course batch tags.

- [ ] **Step 5: Verify health endpoints**

Run:

```powershell
Invoke-WebRequest -UseBasicParsing https://api.oetwithdrhesham.co.uk/health/ready
Invoke-WebRequest -UseBasicParsing https://app.oetwithdrhesham.co.uk/api/health
```

Both must return HTTP 200 with readiness/status `ok`.
