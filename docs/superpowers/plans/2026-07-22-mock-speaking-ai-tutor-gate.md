# Full Mock Speaking — AI/Tutor 7-Day Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Inside every mock that includes a Speaking section (Full, Diagnostic, Final Readiness, and standalone Sub/Part Speaking mocks — every mock type the owner confirmed should be covered, "literally all"), gate the Speaking section on the candidate's `LearnerGoal.TargetExamDate` (made mandatory by the companion plan `2026-07-22-mandatory-exam-date.md`, which MUST ship first): if the exam is **less than 7 days away**, the candidate only sees "Start AI Speaking Exam" — no tutor-booking option. If **7 or more days away**, they see both "Start AI Speaking Exam" and "Book a Tutor" (subject to the tutor system's existing availability). Regular (non-mock) Speaking Practice on `/speaking` is untouched — it already offers a free AI-vs-tutor choice with no exam-date gate.

**Architecture:** Today, a mock's Speaking section always launches straight to `/speaking/task/{paperId}` (self-record audio, no AI, no forced booking — `MockService.BuildLaunchRoute`), and the two-card AI Speaking exam system (`SpeakingExamService`/`SpeakingExamSession`) has an unconditional guard (`SPEAKING_MOCK_REQUIRES_LIVE_TUTOR`) that is never actually reached from the live UI (nothing calls it with a `MockSetId`). We insert a new **Mock Speaking Gateway** page between the mock and both existing completion paths: it fetches a small policy payload (days until exam → AI-only or free choice), then routes to either the AI exam (now taught to carry `mockAttemptId`/`mockSectionId` through to completion) or the existing tutor-booking flow (now taught to attach its booking to the same mock attempt). The mock's existing evidence gate (`RequireProductiveSectionEvidenceAsync`) already accepts any non-empty evidence payload for Speaking, so no change is needed there — only the mock report's AI-vs-human disclaimer needs to tell the two paths apart.

**Tech Stack:** ASP.NET Core Minimal API + EF Core (PostgreSQL) backend; Next.js 16 App Router frontend.

## Global Constraints

- **Ship `2026-07-22-mandatory-exam-date.md` first.** This plan reads `LearnerGoal.TargetExamDate`/`TargetExamDateSetByUser` as a reliable signal; without it, "days until exam" falls back to a meaningless placeholder.
- EF migrations are hand-authored — one future-dated `YYYYMMDD090000_Name.cs` file, inline `[Migration("...")]` attribute, no `.Designer.cs`, don't touch `LearnerDbContextModelSnapshot.cs`.
- Backend `dotnet build`/`dotnet test` locally only for the filtered test class relevant to the task; heavier runs go through CI.
- Frontend local `vitest run` is currently broken — verify with `pnpm exec tsc --noEmit` + manual browser check via the preview tools.
- Never use `git add -A`; stage explicit paths.
- Writing mock sections are **out of scope** — they stay human-marked-only, unchanged.

---

### Task B1: Schema — link `SpeakingExamSession` to a mock attempt/section

**Files:**
- Modify: `backend/src/OetLearner.Api/Domain/SpeakingExamSessionEntities.cs` (add two properties to `SpeakingExamSession`, after `MockSetId` at line 69)
- Create: `backend/src/OetLearner.Api/Data/Migrations/20260723090000_AddSpeakingExamMockAttemptLink.cs`

**Interfaces:**
- Produces: `SpeakingExamSession.MockAttemptId` (`string?`, MaxLength 64), `SpeakingExamSession.MockSectionId` (`string?`, MaxLength 64) — consumed by Task B2 (persist on create) and Task B6 (read on completion).

- [ ] **Step 1: Add the properties**

```csharp
    /// <summary>Provenance when the exam was started from a curated
    /// `SpeakingMockSet` pair. Null for random/profession-picked pairs.</summary>
    [MaxLength(64)]
    public string? MockSetId { get; set; }

    /// <summary>Set when this AI exam was launched from the Mock Center's
    /// Speaking Gateway (2026-07 7-day AI/tutor rule) instead of the
    /// standalone Speaking hub. Links back to the specific
    /// <c>MockAttempt</c>/<c>MockSectionAttempt</c> so completion can write
    /// the mock section's evidence and the mock report can tell an
    /// AI-graded Speaking section apart from a human-marked one.</summary>
    [MaxLength(64)]
    public string? MockAttemptId { get; set; }

    [MaxLength(64)]
    public string? MockSectionId { get; set; }
```

- [ ] **Step 2: Write the migration**

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [Migration("20260723090000_AddSpeakingExamMockAttemptLink")]
    public partial class AddSpeakingExamMockAttemptLink : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MockAttemptId",
                table: "SpeakingExamSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MockSectionId",
                table: "SpeakingExamSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingExamSessions_MockAttemptId",
                table: "SpeakingExamSessions",
                column: "MockAttemptId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SpeakingExamSessions_MockAttemptId",
                table: "SpeakingExamSessions");

            migrationBuilder.DropColumn(
                name: "MockSectionId",
                table: "SpeakingExamSessions");

            migrationBuilder.DropColumn(
                name: "MockAttemptId",
                table: "SpeakingExamSessions");
        }
    }
}
```

Before finalizing, grep `DbSet<SpeakingExamSession>` in `LearnerDbContext.cs` to confirm the table name is `SpeakingExamSessions` (matches the DbSet property name; there's no `.ToTable` override pattern used elsewhere in this file for similar entities, but verify rather than assume).

- [ ] **Step 3: Build to verify**

Run: `pnpm run backend:build`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/OetLearner.Api/Domain/SpeakingExamSessionEntities.cs backend/src/OetLearner.Api/Data/Migrations/20260723090000_AddSpeakingExamMockAttemptLink.cs
git commit -m "feat(speaking): link SpeakingExamSession to a mock attempt/section"
```

---

### Task B2: Backend — 7-day AI/tutor gate replaces the unconditional mock-Speaking block

**Files:**
- Modify: `backend/src/OetLearner.Api/Contracts/SpeakingExamContracts.cs:16-20` (`CreateSpeakingExamRequest`), `:41-50` (`SpeakingExamDetail`)
- Modify: `backend/src/OetLearner.Api/Services/Speaking/SpeakingExamService.cs:47-133` (`CreateExamAsync`), `ProjectAsync` (wherever `SpeakingExamDetail` is constructed — search the file for `new SpeakingExamDetail(`)
- Test: `backend/tests/OetLearner.Api.Tests/Services/SpeakingExamServiceTests.cs:319-360` (existing mock-launch tests — update, don't just add)

**Interfaces:**
- Consumes: `LearnerGoal.TargetExamDate` (via `db.Goals`, already available on `LearnerDbContext db` injected into `SpeakingExamService`).
- Produces: `CreateSpeakingExamRequest.MockAttemptId`/`MockSectionId` (new optional fields), `SpeakingExamDetail.MockAttemptId`/`MockSectionId` — consumed by Task B6's frontend completion handler.

- [ ] **Step 1: Read the existing mock-launch tests first**

Open `backend/tests/OetLearner.Api.Tests/Services/SpeakingExamServiceTests.cs:319-360` before writing anything — these tests currently assert the OLD unconditional behavior (AI-mode mock launch always throws `SPEAKING_MOCK_REQUIRES_LIVE_TUTOR`). They must be rewritten, not left in place, or they will contradict the new behavior and fail for the right reasons but confuse the diff.

- [ ] **Step 2: Write the updated/new tests**

Replace the existing mock-launch test(s) in that range with:

```csharp
[Fact]
public async Task CreateExamAsync_MockLaunch_RequiresAi_WhenExamLessThan7DaysAway()
{
    var userId = await SeedLearnerWithGoalAsync(TimeProvider, DateOnly.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime.AddDays(3)));
    var request = new CreateSpeakingExamRequest("live_tutor", MockAttemptId: "mock_attempt_1", ProfessionId: "medicine", BookingId: "booking_1");

    var ex = await Assert.ThrowsAsync<ApiException>(() => Service.CreateExamAsync(userId, request, CancellationToken.None));
    Assert.Equal("SPEAKING_MOCK_REQUIRES_AI", ex.Code);
}

[Fact]
public async Task CreateExamAsync_MockLaunch_AllowsAi_WhenExamLessThan7DaysAway()
{
    var userId = await SeedLearnerWithGoalAsync(TimeProvider, DateOnly.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime.AddDays(3)));
    var request = new CreateSpeakingExamRequest("ai", MockAttemptId: "mock_attempt_1", MockSectionId: "mock_section_1", ProfessionId: "medicine");

    var detail = await Service.CreateExamAsync(userId, request, CancellationToken.None);
    Assert.Equal("ai", detail.Mode);
}

[Fact]
public async Task CreateExamAsync_MockLaunch_AllowsEitherMode_WhenExam7OrMoreDaysAway()
{
    var userId = await SeedLearnerWithGoalAsync(TimeProvider, DateOnly.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime.AddDays(10)));
    var aiRequest = new CreateSpeakingExamRequest("ai", MockAttemptId: "mock_attempt_2", ProfessionId: "medicine");
    var aiDetail = await Service.CreateExamAsync(userId, aiRequest, CancellationToken.None);
    Assert.Equal("ai", aiDetail.Mode);

    var tutorRequest = new CreateSpeakingExamRequest("live_tutor", MockAttemptId: "mock_attempt_3", ProfessionId: "medicine", BookingId: "booking_2");
    var tutorDetail = await Service.CreateExamAsync(userId, tutorRequest, CancellationToken.None);
    Assert.Equal("live_tutor", tutorDetail.Mode);
}

[Fact]
public async Task CreateExamAsync_MockLaunch_PersistsMockAttemptAndSectionId()
{
    var userId = await SeedLearnerWithGoalAsync(TimeProvider, DateOnly.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime.AddDays(10)));
    var request = new CreateSpeakingExamRequest("ai", MockAttemptId: "mock_attempt_9", MockSectionId: "mock_section_9", ProfessionId: "medicine");

    var detail = await Service.CreateExamAsync(userId, request, CancellationToken.None);
    var stored = await Db.SpeakingExamSessions.SingleAsync(e => e.Id == detail.ExamId);
    Assert.Equal("mock_attempt_9", stored.MockAttemptId);
    Assert.Equal("mock_section_9", stored.MockSectionId);
}
```

(`SeedLearnerWithGoalAsync(TimeProvider, DateOnly targetExamDate)` — a small test helper inserting a `LearnerUser` + `LearnerGoal` row with `TargetExamDateSetByUser = true`; copy whatever seeding helper this test file's existing tests already use for a valid learner + wallet setup, and add the goal row alongside it.)

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~SpeakingExamServiceTests"`
Expected: FAIL — `CreateSpeakingExamRequest` has no `MockAttemptId` param yet, and/or the old unconditional-throw behavior contradicts the new assertions.

- [ ] **Step 4: Implement**

`SpeakingExamContracts.cs` — extend the request and detail records:

```csharp
public record CreateSpeakingExamRequest(
    string Mode,
    string? MockSetId = null,
    string? ProfessionId = null,
    string? BookingId = null,
    string? MockAttemptId = null,
    string? MockSectionId = null);
```

```csharp
public record SpeakingExamDetail(
    string ExamId,
    string Mode,
    string State,
    string ProfessionId,
    int CurrentCardNumber,
    string? CurrentSessionId,
    object? CurrentCard,
    SpeakingExamClock Clock,
    DateTimeOffset? CompletedAt,
    string? MockAttemptId,
    string? MockSectionId);
```

`SpeakingExamService.cs` — replace the unconditional block (lines 64-76) with:

```csharp
        var isMockLaunch = !string.IsNullOrWhiteSpace(req.MockSetId) || !string.IsNullOrWhiteSpace(req.MockAttemptId);
        if (isMockLaunch)
        {
            // ── Full Mock Speaking 7-day AI/tutor gate (2026-07-22 owner rule) ──
            // Inside ANY mock with a Speaking section: if the candidate's target
            // exam is under 7 days away, they may only take it as an AI exam (a
            // live-tutor booking can't reliably be arranged on that notice). At
            // 7+ days out, either mode is allowed — the candidate's choice.
            var targetExamDate = await db.Goals.AsNoTracking()
                .Where(g => g.UserId == userId)
                .Select(g => (DateOnly?)g.TargetExamDate)
                .SingleOrDefaultAsync(ct);
            var daysUntilExam = targetExamDate is null
                ? (int?)null
                : targetExamDate.Value.DayNumber - DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).DayNumber;

            if (daysUntilExam is not null && daysUntilExam.Value < 7 && mode != SpeakingExamMode.Ai)
            {
                throw ApiException.Validation("SPEAKING_MOCK_REQUIRES_AI",
                    "Your exam is less than 7 days away — this mock's Speaking section must be completed as an AI exam.");
            }
        }
```

Then in the `exam` construction (lines 112-127), add the two new fields:

```csharp
        var exam = new SpeakingExamSession
        {
            Id = $"spx_{Guid.NewGuid():N}",
            UserId = userId,
            ProfessionId = professionId,
            Mode = mode,
            State = SpeakingExamState.Intro,
            MockSetId = string.IsNullOrWhiteSpace(req.MockSetId) ? null : req.MockSetId,
            MockAttemptId = string.IsNullOrWhiteSpace(req.MockAttemptId) ? null : req.MockAttemptId,
            MockSectionId = string.IsNullOrWhiteSpace(req.MockSectionId) ? null : req.MockSectionId,
            CardAId = cardA.Id,
            CardBId = cardB.Id,
            BookingId = string.IsNullOrWhiteSpace(req.BookingId) ? null : req.BookingId,
            IntroStartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
```

Finally, find `ProjectAsync` in the same file (search `private async Task<SpeakingExamDetail> ProjectAsync`) and add `MockAttemptId: exam.MockAttemptId, MockSectionId: exam.MockSectionId` to whatever `new SpeakingExamDetail(...)` construction it returns — read that method fully before editing since its exact parameter order/positional-vs-named style needs to match.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~SpeakingExamServiceTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add backend/src/OetLearner.Api/Contracts/SpeakingExamContracts.cs backend/src/OetLearner.Api/Services/Speaking/SpeakingExamService.cs backend/tests/OetLearner.Api.Tests/Services/SpeakingExamServiceTests.cs
git commit -m "feat(speaking): replace forced-live-tutor mock rule with a 7-day AI/tutor gate"
```

---

### Task B3: Backend — mock Speaking access policy endpoint

**Files:**
- Create: `backend/src/OetLearner.Api/Endpoints/MockSpeakingGatewayEndpoints.cs`
- Modify: `backend/src/OetLearner.Api/Program.cs` (register the new endpoint group — search for where sibling `Map...Endpoints(app)` calls already live and add one alongside them)
- Test: `backend/tests/OetLearner.Api.Tests` — create `Endpoints/MockSpeakingGatewayEndpointsTests.cs` or add to whichever existing test file already covers a similarly small read-only endpoint group, for style consistency.

**Interfaces:**
- Consumes: `LearnerGoal.TargetExamDate`.
- Produces: `GET /v1/mocks/speaking-access` → `{ requiresAiOnly: bool, daysUntilExam: int | null }` — consumed by Task B5's Gateway page.

- [ ] **Step 1: Write the failing test**

```csharp
public class MockSpeakingGatewayEndpointsTests : IClassFixture<ApiTestFixture> // match whichever base fixture class sibling endpoint tests use
{
    [Fact]
    public async Task SpeakingAccess_ReturnsRequiresAiOnlyTrue_WhenExamUnder7Days()
    {
        var client = await AuthenticatedLearnerClientAsync(examDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
        var response = await client.GetAsync("/v1/mocks/speaking-access");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("requiresAiOnly").GetBoolean());
    }

    [Fact]
    public async Task SpeakingAccess_ReturnsRequiresAiOnlyFalse_WhenExam7OrMoreDaysAway()
    {
        var client = await AuthenticatedLearnerClientAsync(examDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));
        var response = await client.GetAsync("/v1/mocks/speaking-access");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("requiresAiOnly").GetBoolean());
    }
}
```

(`AuthenticatedLearnerClientAsync(DateOnly examDate)` — adapt from whatever existing integration-test helper already spins up an authenticated learner `HttpClient`; search `backend/tests/OetLearner.Api.Tests` for `IClassFixture` usage against a similar minimal-API endpoint test to copy the exact fixture/auth pattern used in this repo.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~MockSpeakingGatewayEndpointsTests"`
Expected: FAIL — 404, route doesn't exist yet.

- [ ] **Step 3: Implement**

```csharp
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Endpoints;

public static class MockSpeakingGatewayEndpoints
{
    public static void MapMockSpeakingGatewayEndpoints(this WebApplication app)
    {
        var v1 = app.MapGroup("/v1/mocks").RequireAuthorization();

        v1.MapGet("/speaking-access", async (HttpContext http, LearnerDbContext db, CancellationToken ct) =>
        {
            var userId = http.UserId();
            var targetExamDate = await db.Goals.AsNoTracking()
                .Where(g => g.UserId == userId)
                .Select(g => (DateOnly?)g.TargetExamDate)
                .SingleOrDefaultAsync(ct);

            int? daysUntilExam = targetExamDate is null
                ? null
                : targetExamDate.Value.DayNumber - DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).DayNumber;

            var requiresAiOnly = daysUntilExam is not null && daysUntilExam.Value < 7;

            return Results.Ok(new { requiresAiOnly, daysUntilExam });
        });
    }
}
```

Register it in `Program.cs` next to the other `Map...Endpoints(app)` calls (search for e.g. `app.MapPrivateSpeakingEndpoints();` or similar and add `app.MapMockSpeakingGatewayEndpoints();` right after it).

Confirm `http.UserId()` is the correct extension-method name used elsewhere in this codebase for pulling the authenticated user id off `HttpContext` (it's used exactly this way in `LearnerEndpoints.cs` per Task A7's research — e.g. `http.UserId()` at `LearnerEndpoints.cs:48`).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~MockSpeakingGatewayEndpointsTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Endpoints/MockSpeakingGatewayEndpoints.cs backend/src/OetLearner.Api/Program.cs backend/tests/OetLearner.Api.Tests/Endpoints/MockSpeakingGatewayEndpointsTests.cs
git commit -m "feat(mocks): add GET /v1/mocks/speaking-access policy endpoint"
```

---

### Task B4: Backend — route Speaking to the new Gateway; disclaimer tells AI apart from human-marked

**Files:**
- Modify: `backend/src/OetLearner.Api/Services/MockService.cs:3242-3274` (`BuildLaunchRoute`)
- Modify: `backend/src/OetLearner.Api/Services/MockService.cs:1196-1215` (`GetMockReportAsync` disclaimer)
- Test: search `backend/tests/OetLearner.Api.Tests` for an existing `MockService`/`GetMockReportAsync` or `BuildLaunchRoute` test and add to it.

**Interfaces:**
- Consumes: `SpeakingExamSession.MockAttemptId`, `SpeakingExamSession.Mode`, `SpeakingExamSession.State` (Task B1/B2).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void BuildLaunchRoute_Speaking_RoutesToGateway()
{
    var route = InvokeBuildLaunchRoute(subtestCode: "speaking"); // reflection or a test-only internal-visible wrapper, matching however this private static method is already tested elsewhere (if it isn't tested elsewhere, make it `internal` and add `[assembly: InternalsVisibleTo("OetLearner.Api.Tests")]` if that attribute isn't already present — check AssemblyInfo/csproj first)
    Assert.StartsWith("/mocks/speaking/", route);
}

[Fact]
public async Task GetMockReportAsync_Disclaimer_ReflectsAiGradedSpeaking()
{
    var (userId, attempt) = await SeedCompletedMockWithAiSpeakingAsync();
    dynamic report = await Service.GetMockReportAsync(userId, attempt.Id, CancellationToken.None);
    string disclaimer = report.aiTrustBoundary.disclaimer;
    Assert.Contains("AI", disclaimer);
    Assert.DoesNotContain("marked by a human examiner", disclaimer);
}

[Fact]
public async Task GetMockReportAsync_Disclaimer_StaysHumanMarked_WhenSpeakingWasBookedOrSelfRecorded()
{
    var (userId, attempt) = await SeedCompletedMockWithBookedSpeakingAsync();
    dynamic report = await Service.GetMockReportAsync(userId, attempt.Id, CancellationToken.None);
    string disclaimer = report.aiTrustBoundary.disclaimer;
    Assert.Contains("marked by a human examiner", disclaimer);
}
```

(Write `SeedCompletedMockWithAiSpeakingAsync`/`SeedCompletedMockWithBookedSpeakingAsync` as small helpers: insert a `MockAttempt` + a `MockSectionAttempt` with `SubtestCode == "speaking"`, and either (a) a completed `SpeakingExamSession` with `MockAttemptId == attempt.Id && Mode == SpeakingExamMode.Ai && State == SpeakingExamState.Completed`, or (b) a `MockBookings` row with `MockAttemptId == attempt.Id`. Copy the surrounding seeding pattern from whatever existing test already builds a `MockAttempt` + sections in this test class.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~MockServiceTests"`
Expected: FAIL

- [ ] **Step 3: Implement**

`BuildLaunchRoute` — change only the `"speaking"` arm (line 3271):

```csharp
        return section.SubtestCode switch
        {
            "reading" => $"/reading/paper/{Uri.EscapeDataString(section.ContentPaperId)}?{query}",
            "listening" => $"/listening/paper/{Uri.EscapeDataString(section.ContentPaperId)}?{query}",
            "writing" => $"/mocks/writing/{Uri.EscapeDataString(sectionAttemptId ?? section.ContentPaperId)}?{query}",
            "speaking" => $"/mocks/speaking/{Uri.EscapeDataString(section.ContentPaperId)}?{query}",
            _ => $"/mocks/player/{Uri.EscapeDataString(attemptId)}"
        };
```

(The `query` string built just above this switch already carries `mockAttemptId`, `mockSectionId`, `paperId`, `mockMode`, `strictness`, `deliveryMode`, `strictTimer` — nothing else needs to change here.)

`GetMockReportAsync` disclaimer (lines 1196-1215) — replace the two-way `hasHumanMarkedSection` check with a three-way check that additionally asks whether Speaking specifically was AI-graded:

```csharp
        var hasWritingSection = await db.MockSectionAttempts.AsNoTracking()
            .AnyAsync(s => s.MockAttemptId == row.attempt.Id && s.SubtestCode == "writing", ct);
        var hasSpeakingSection = await db.MockSectionAttempts.AsNoTracking()
            .AnyAsync(s => s.MockAttemptId == row.attempt.Id && s.SubtestCode == "speaking", ct);
        var speakingWasAiGraded = hasSpeakingSection && await db.SpeakingExamSessions.AsNoTracking()
            .AnyAsync(e => e.MockAttemptId == row.attempt.Id
                && e.Mode == SpeakingExamMode.Ai
                && e.State == SpeakingExamState.Completed, ct);

        if (hasWritingSection || (hasSpeakingSection && !speakingWasAiGraded))
        {
            payload["aiTrustBoundary"] = new
            {
                disclaimer = "Reading & Listening are auto-scored against the official answer key; Speaking & Writing are marked by a human examiner, not AI. Treat this as practice guidance, not an official exam result.",
                provenanceLabel = "Human-marked Speaking/Writing · auto-scored Reading/Listening",
                methodLabel = "Examiner-marked mock exam"
            };
        }
        else if (hasSpeakingSection && speakingWasAiGraded)
        {
            payload["aiTrustBoundary"] = new
            {
                disclaimer = "Reading & Listening are auto-scored against the official answer key; Speaking was scored by AI (your exam was inside the 7-day window, or you chose AI). Treat this as practice guidance, not an official exam result.",
                provenanceLabel = "AI-scored Speaking · auto-scored Reading/Listening",
                methodLabel = "AI-assisted mock exam"
            };
        }
        else
        {
            payload["aiTrustBoundary"] = new
            {
                disclaimer = "Reading & Listening mock scores are auto-scored against the official answer key and should be treated as practice guidance, not official exam results.",
                provenanceLabel = "Auto-scored mock estimate",
                methodLabel = "Auto-scored mock exam"
            };
        }
```

Remove the old `hasHumanMarkedSection` variable and its single `AnyAsync` call (now superseded by the two more specific queries above) — read lines 1196-1215 in full before editing to replace the whole block cleanly rather than leaving dead code.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~MockServiceTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Services/MockService.cs backend/tests/OetLearner.Api.Tests/Services/MockServiceTests.cs
git commit -m "feat(mocks): route Speaking through the AI/tutor gateway; disclaimer reflects AI-graded Speaking"
```

---

### Task B5: Frontend — Mock Speaking Gateway page

**Files:**
- Create: `app/mocks/speaking/[id]/page.tsx`
- Modify: `lib/api.ts` (add a `fetchMockSpeakingAccess()` client function next to `fetchOnboardingState`)

**Interfaces:**
- Consumes: `GET /v1/mocks/speaking-access` (Task B3).
- Produces: links into `/speaking/exam?{query}` (Task B6) and the tutor-booking flow (Task B7), both forwarding `mockAttemptId`/`mockSectionId`/`paperId`/`mockMode`/`strictness`/`deliveryMode`/`strictTimer` untouched from its own query string.

- [ ] **Step 1: Add the API client function**

`lib/api.ts`, next to `fetchOnboardingState` (~line 1349):

```typescript
export async function fetchMockSpeakingAccess(): Promise<{ requiresAiOnly: boolean; daysUntilExam: number | null }> {
  const data = await apiRequest<ApiRecord>('/v1/mocks/speaking-access');
  return {
    requiresAiOnly: Boolean(data.requiresAiOnly),
    daysUntilExam: data.daysUntilExam === null || data.daysUntilExam === undefined ? null : Number(data.daysUntilExam),
  };
}
```

- [ ] **Step 2: Build the Gateway page**

```tsx
'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { GraduationCap, Users } from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { LearnerSurfaceCard } from '@/components/domain/learner-surface';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';
import { fetchMockSpeakingAccess } from '@/lib/api';

export default function MockSpeakingGatewayPage() {
  const params = useParams();
  const router = useRouter();
  const searchParams = useSearchParams();
  const rawId = params?.id;
  const paperId = typeof rawId === 'string' ? rawId : '';

  const [access, setAccess] = useState<{ requiresAiOnly: boolean; daysUntilExam: number | null } | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchMockSpeakingAccess()
      .then((result) => {
        if (!cancelled) setAccess(result);
      })
      .catch(() => {
        if (!cancelled) setLoadError('Could not check your Speaking options. Please try again.');
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (loadError) {
    return (
      <AppShell>
        <div className="mx-auto max-w-2xl p-6 text-center text-muted">{loadError}</div>
      </AppShell>
    );
  }

  if (!access) {
    return (
      <AppShell>
        <div className="mx-auto max-w-2xl p-6 text-center text-muted">Checking your Speaking options…</div>
      </AppShell>
    );
  }

  const forwardedQuery = searchParams?.toString() ?? '';
  const aiHref = `/speaking/exam?${forwardedQuery}`;
  const tutorHref = `/mocks/bookings/new?${forwardedQuery}`;

  const aiCard: LearnerSurfaceCardModel = {
    eyebrow: 'AI Exam',
    eyebrowIcon: GraduationCap,
    title: 'Start AI Speaking Exam',
    description: access.requiresAiOnly
      ? `Your exam is ${access.daysUntilExam ?? '< 7'} days away — this mock's Speaking section must be completed with the AI examiner.`
      : 'The AI plays the patient and marks your two-card exam instantly.',
    accent: 'primary',
    primaryAction: { label: 'Start AI Speaking Exam', href: aiHref },
  };

  const tutorCard: LearnerSurfaceCardModel = {
    eyebrow: 'Live Tutor',
    eyebrowIcon: Users,
    title: 'Book a Tutor',
    description: 'A human tutor plays the patient and marks your exam, based on available slots.',
    accent: 'navy',
    primaryAction: { label: 'Book a Tutor', href: tutorHref },
  };

  return (
    <AppShell>
      <div className="mx-auto max-w-4xl space-y-6 p-6">
        <div>
          <h1 className="text-xl font-bold text-navy">Mock Speaking</h1>
          <p className="mt-1 text-sm text-muted">
            {access.requiresAiOnly
              ? 'Your exam is under 7 days away, so this section is AI-only.'
              : 'Choose how you want to complete this mock’s Speaking section.'}
          </p>
        </div>
        <div className={access.requiresAiOnly ? 'max-w-md' : 'grid gap-4 sm:grid-cols-2'}>
          <LearnerSurfaceCard card={aiCard} />
          {access.requiresAiOnly ? null : <LearnerSurfaceCard card={tutorCard} />}
        </div>
      </div>
    </AppShell>
  );
}
```

Before finalizing, read `lib/learner-surface.ts` for the exact `LearnerSurfaceCardModel` field names/types (this plan inferred them from how `components/domain/learner-surface.tsx` *reads* the model, not from the type definition itself) and adjust field names if they differ. Also check whichever layout wrapper (`AppShell` vs `LearnerDashboardShell`) other `/mocks/**` pages actually use — `/speaking/page.tsx` uses `AppShell` per its existing import, but confirm the `/mocks/**` tree's convention before assuming it matches.

- [ ] **Step 3: Typecheck**

Run: `pnpm exec tsc --noEmit`
Expected: no new errors (fix any field-name mismatches found in Step 2's verification).

- [ ] **Step 4: Commit**

```bash
git add app/mocks/speaking/[id]/page.tsx lib/api.ts
git commit -m "feat(mocks): add the Speaking AI/tutor gateway page"
```

---

### Task B6: Frontend — AI exam launcher + runner carry mock linkage through to completion

**Files:**
- Modify: `app/speaking/exam/page.tsx` (forward `mockAttemptId`/`mockSectionId` into `createSpeakingExam`)
- Modify: `lib/api/speaking-exams.ts:74-79` (`CreateSpeakingExamInput`)
- Modify: `app/speaking/exam/[id]/page.tsx` (around the `state === 'completed'` check — call `completeMockSection` before/alongside the existing redirect to results)

**Interfaces:**
- Consumes: `SpeakingExamDetail.MockAttemptId`/`MockSectionId` (Task B2), `completeMockSection(sessionId, sectionId, payload)` (existing, `lib/api.ts:3115`).

- [ ] **Step 1: Extend the input type**

`lib/api/speaking-exams.ts:74-79`:

```typescript
export interface CreateSpeakingExamInput {
  mode: SpeakingExamMode;
  mockSetId?: string | null;
  professionId?: string | null;
  bookingId?: string | null;
  mockAttemptId?: string | null;
  mockSectionId?: string | null;
}
```

- [ ] **Step 2: Forward the params from the launcher**

`app/speaking/exam/page.tsx` — add `useSearchParams` and read the two params, forwarding them into `createSpeakingExam`:

```typescript
import { useCallback, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
// ...existing imports...

export default function SpeakingExamLauncherPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const mockAttemptId = searchParams?.get('mockAttemptId') ?? undefined;
  const mockSectionId = searchParams?.get('mockSectionId') ?? undefined;
  const [starting, setStarting] = useState(false);
  // ...existing state...

  const startAiExam = useCallback(async () => {
    if (starting) return;
    setStarting(true);
    setError(null);
    setCreditMessage(null);
    try {
      const exam = await createSpeakingExam({ mode: 'ai', mockAttemptId, mockSectionId });
      router.push(`/speaking/exam/${exam.examId}`);
    } catch (err) {
      // ...unchanged...
    }
  }, [router, starting, mockAttemptId, mockSectionId]);
```

(Read the full file first — this plan shows only the parts that change; keep every other line, including the rest of the `catch` block, identical.)

- [ ] **Step 3: Complete the mock section when the AI exam finishes**

`app/speaking/exam/[id]/page.tsx` — read the full polling function containing the `state === 'completed'` check (around line 72-78 per this plan's research) before editing. Extend it:

```typescript
      const detail = await getSpeakingExam(examId);
      setExam(detail);
      setFetchedAt(Date.now());
      setLoadError(null);
      if (detail.state === 'completed' || detail.state === 'expired' || detail.state === 'cancelled') {
        if (detail.state === 'completed' && detail.mockAttemptId && detail.mockSectionId) {
          try {
            await completeMockSection(detail.mockAttemptId, detail.mockSectionId, {
              contentAttemptId: detail.examId,
              rawScore: null,
              rawScoreMax: null,
              scaledScore: null,
              grade: null,
              evidence: { source: 'ai_speaking_exam', examId: detail.examId },
            });
          } catch (mockErr) {
            console.warn('Could not mark mock speaking section complete', mockErr);
          }
        }
        router.replace(`/speaking/exam/${examId}/results`);
      }
```

Add the `completeMockSection` import (from `@/lib/api`, matching how `app/speaking/task/[id]/page.tsx:16` already imports it) and confirm `SpeakingExamDetail`'s TypeScript type (wherever it's declared client-side, likely `lib/api/speaking-exams.ts`) has been updated to include `mockAttemptId`/`mockSectionId` — if the client-side type is hand-written rather than inferred from the backend response, add those two optional fields there too.

- [ ] **Step 4: Typecheck**

Run: `pnpm exec tsc --noEmit`
Expected: no new errors.

- [ ] **Step 5: Commit**

```bash
git add app/speaking/exam/page.tsx app/speaking/exam/[id]/page.tsx lib/api/speaking-exams.ts
git commit -m "feat(speaking): carry mock attempt/section through the AI exam to completion"
```

---

### Task B7: Thread mock attempt linkage through the existing tutor-booking flow

**Files:**
- Modify: `app/mocks/bookings/new/page.tsx`
- Modify: whichever backend endpoint/service backs `createMockBookingV2` (search `backend/src/OetLearner.Api/Endpoints/MockBookingEndpoints.cs` and `backend/src/OetLearner.Api/Services/MockBookingService.cs` for how a created `MockBookings` row's `MockAttemptId` column gets set today)

**Interfaces:**
- Consumes: `mockAttemptId`/`mockSectionId` query params forwarded from Task B5's Gateway page.
- Produces: a `MockBookings` row whose `MockAttemptId` matches the originating mock attempt — required so `RequireProductiveSectionEvidenceAsync`'s existing `hasBooking` check (`MockService.cs:868-872`) passes for this specific attempt.

- [ ] **Step 1: Read the current booking-creation flow fully**

This plan's research found `app/mocks/bookings/new/page.tsx` has **no** `mockAttemptId`/`mockSectionId` handling today — it's a standalone booking form, not one deep-linked from an in-progress mock attempt. Before writing code, read the full page plus `createMockBookingV2`'s request type (search `lib/api` for it) and the backend handler, to find out how the form currently identifies "which mock attempt is this booking for" (a dropdown/select of the learner's open attempts, most likely).

- [ ] **Step 2: Write a failing test for the backend linkage**

```csharp
[Fact]
public async Task CreateMockBookingV2Async_PersistsMockAttemptId_WhenProvided()
{
    var (userId, attempt) = await SeedOpenMockAttemptWithSpeakingSectionAsync();
    var request = /* whatever the existing CreateMockBookingV2 request DTO looks like, with its MockAttemptId field set to attempt.Id */;

    var booking = await Service.CreateMockBookingV2Async(userId, request, CancellationToken.None);

    var stored = await Db.MockBookings.SingleAsync(b => b.Id == booking.Id);
    Assert.Equal(attempt.Id, stored.MockAttemptId);
}
```

(Adapt method/type names to whatever Step 1 actually found — this test's exact shape depends on the real `CreateMockBookingV2` signature, which this plan could not verify without reading the file. If the booking service already accepts and persists a `MockAttemptId` today — i.e. the form's existing attempt-selector already writes it — this test should PASS immediately with no implementation change needed; in that case, skip straight to Step 5 and just wire the frontend query param through the existing selector instead of adding new backend plumbing.)

- [ ] **Step 3: Run the test**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~CreateMockBookingV2Async_PersistsMockAttemptId"`
Expected: tells you whether backend work is needed (see Step 2's note).

- [ ] **Step 4: Implement backend linkage if Step 3 failed**

Add `MockAttemptId`/`MockSectionId` to the booking-creation request DTO (if missing) and persist them onto the created `MockBookings` row, following whatever pattern the rest of that service already uses for its other FK-style fields.

- [ ] **Step 5: Wire the frontend query params into the existing attempt selector**

In `app/mocks/bookings/new/page.tsx`, read `mockAttemptId`/`mockSectionId` via `useSearchParams()`. If the page currently shows a dropdown/select letting the learner pick which open mock attempt this booking is for, and both params are present: pre-select that attempt and disable/hide the picker (the learner arrived here from that specific attempt's Speaking Gateway, so the choice is already made) rather than asking them to pick again. Include the ids in the `createMockBookingV2(...)` call's payload.

- [ ] **Step 6: Manual verification**

`preview_start`, walk a mock attempt with a Speaking section whose candidate has `daysUntilExam >= 7`, reach the Gateway, click "Book a Tutor", confirm the booking form opens pre-scoped to that attempt (not a bare picker), complete a booking, and confirm `GET /v1/mocks/{attemptId}` (or wherever section state is read) now shows the Speaking section satisfied via `hasBooking` — i.e. `RequireProductiveSectionEvidenceAsync` no longer blocks completing that section.

- [ ] **Step 7: Commit**

```bash
git add app/mocks/bookings/new/page.tsx backend/src/OetLearner.Api/Endpoints/MockBookingEndpoints.cs backend/src/OetLearner.Api/Services/MockBookingService.cs
git commit -m "feat(mocks): pre-scope tutor booking to the originating mock attempt from the Speaking gateway"
```

---

### Task B8: End-to-end manual verification

**Files:** none (verification only)

- [ ] **Step 1: <7-days case**

Seed/edit a test learner's `LearnerGoal.TargetExamDate` to 3 days from now (`TargetExamDateSetByUser = true`). Start a Full Mock, reach the Speaking section, confirm the Gateway shows **only** "Start AI Speaking Exam" (no tutor card). Complete the AI exam, confirm it redirects through results and the mock's Speaking section shows complete on `/mocks/player/{attemptId}`.

- [ ] **Step 2: >=7-days case**

Set the same learner's exam date to 14 days out. Start a new mock attempt, reach the Speaking section, confirm the Gateway shows **both** cards. Try each path once (AI on one attempt, tutor booking on another) and confirm both mark the section complete.

- [ ] **Step 3: Report disclaimer**

Open the mock report for the AI-graded attempt from Step 1/2 and confirm the disclaimer says Speaking was AI-scored, not "marked by a human examiner." Open the report for a booked-tutor attempt and confirm it still says "marked by a human examiner."

- [ ] **Step 4: Regression check — regular Speaking Practice untouched**

Navigate to `/speaking` (not via any mock) and confirm both "Start Speaking Exam" and "Book a Tutor" are still freely offered regardless of `TargetExamDate` — this hub has no gate and must not gain one.

- [ ] **Step 5: Commit**

No code changes in this task — if Step 1-4 surface a bug, fix it in the relevant earlier task's files and amend that task's commit description in your own working notes, then re-run this task's verification from Step 1.

---

## Self-Review Notes

- **Spec coverage:** 7-day AI/tutor gate (B2/B3), applies to every mock type with Speaking since `BuildLaunchRoute`/`RequireProductiveSectionEvidenceAsync` never discriminated by mock type in the first place (B4), report disclaimer distinguishes AI vs human-marked (B4), regular Speaking Practice explicitly verified untouched (B8 Step 4).
- **Placeholder scan:** Task B7 is the one place this plan couldn't give fully verified code (the tutor-booking service internals were never read) — it's written as a discovery-then-implement task with a concrete test and a concrete fallback ("if the test passes immediately, no backend change needed"), not a bare "add appropriate handling" placeholder.
- **Type consistency:** `MockAttemptId`/`MockSectionId` naming kept identical across `SpeakingExamSession` (B1), `CreateSpeakingExamRequest`/`SpeakingExamDetail` (B2), the frontend `CreateSpeakingExamInput` (B6), and the query-string params already used everywhere else in the Mocks module (`mockAttemptId`/`mockSectionId`, per `BuildLaunchRoute` and `/speaking/task/[id]/page.tsx`).
- **Dependency:** this plan assumes `2026-07-22-mandatory-exam-date.md` has already shipped — `TargetExamDate` is read defensively (null-safe) throughout in case it hasn't, but the whole feature is meaningless against placeholder dates.
