# Mandatory Target Exam Date Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the candidate's target OET exam date (`LearnerGoal.TargetExamDate`) a genuinely mandatory piece of data — collected at registration, collected when an admin manually adds a user, and force-collected from any existing learner who doesn't have a real one — so that downstream features (starting with the Full Mock Speaking AI/tutor gate in the companion plan `2026-07-22-mock-speaking-ai-tutor-gate.md`) can trust it.

**Architecture:** `LearnerGoal.TargetExamDate` already exists but is populated lazily with a meaningless `+3 months` placeholder the first time any learner-facing endpoint touches the goal (`CreateDefaultGoal`, `LearnerService.cs`). There is currently no way to tell "the candidate really told us this date" apart from "the system guessed." We add one new boolean, `TargetExamDateSetByUser`, to `LearnerGoal`, and one new nullable field, `TargetExamDate`, to `LearnerRegistrationProfile` (the row already created at registration). Registration and admin-create now require the date and write it onto the registration profile; the existing lazy goal-creation code (already reads `CountryTarget` off the same registration profile — we mirror that exact pattern) picks it up the first time the goal row is created, setting `TargetExamDateSetByUser = true`. For learners who already have a goal row (existing users, or the lazy `+3 months` placeholder already fired), a new onboarding-state flag (`examDateRequired`) drives a hard client-side redirect to `/goals` until they submit a real date, which flips the same flag.

**Tech Stack:** ASP.NET Core Minimal API + EF Core (PostgreSQL) backend; Next.js 16 App Router + React Hook Form + Zod frontend.

## Global Constraints

- EF migrations in this repo are **hand-authored** — never run `dotnet ef migrations add`. Write one future-dated `YYYYMMDD090000_Name.cs` file with an inline `[Migration("...")]` attribute on the migration class itself (no paired `.Designer.cs`, do not touch `LearnerDbContextModelSnapshot.cs`).
- Backend `dotnet build`/`dotnet test` should be run locally only for the single filtered test class relevant to a task (per project convention); anything heavier goes through CI.
- Frontend local `vitest run` is currently broken (Node 24 / vitest 4 ESM issue) — verify frontend changes with `pnpm exec tsc --noEmit` plus a manual browser check via the preview tools, not local vitest.
- Never use `git add -A`; stage explicit paths.
- No `Co-Authored-By` trailer unless `.claude/settings.json` sets it.

---

### Task A1: Schema — `TargetExamDateSetByUser` on `LearnerGoal`, `TargetExamDate` on `LearnerRegistrationProfile`

**Files:**
- Modify: `backend/src/OetLearner.Api/Domain/Entities.cs:84` (LearnerGoal)
- Modify: `backend/src/OetLearner.Api/Domain/SignupEntities.cs` (LearnerRegistrationProfile, add near `CountryTarget`)
- Create: `backend/src/OetLearner.Api/Data/Migrations/20260722090000_AddTargetExamDateFields.cs`
- Test: `backend/tests/OetLearner.Api.Tests/Data/LearnerDbContextMigrationTests.cs` (only if this file already exists — otherwise skip an automated test for this task; verify via `backend:build` instead)

**Interfaces:**
- Produces: `LearnerGoal.TargetExamDateSetByUser` (bool, default `false`), `LearnerRegistrationProfile.TargetExamDate` (`DateOnly?`, default `null`) — both consumed by Task A2 onward.

- [ ] **Step 1: Add the two properties**

In `backend/src/OetLearner.Api/Domain/Entities.cs`, right after line 84 (`public DateOnly? TargetExamDate { get; set; }`) inside `LearnerGoal`:

```csharp
    public DateOnly? TargetExamDate { get; set; }

    /// <summary>True once a real candidate- or admin-supplied exam date has
    /// been recorded (registration, admin Add-User, or the /goals form).
    /// False for the lazy "+3 months" placeholder <see cref="CreateDefaultGoal"/>
    /// stamps on first touch — used to force the onboarding exam-date gate.</summary>
    public bool TargetExamDateSetByUser { get; set; }
```

In `backend/src/OetLearner.Api/Domain/SignupEntities.cs`, inside `LearnerRegistrationProfile`, add next to the existing `CountryTarget` property:

```csharp
    public string? CountryTarget { get; set; }

    /// <summary>Candidate's target OET exam date, collected at registration
    /// (or backfilled by an admin's Add-User form). Read once by
    /// <c>LearnerService.CreateDefaultGoal</c>'s callers the first time the
    /// learner's <see cref="LearnerGoal"/> row is lazily created.</summary>
    public DateOnly? TargetExamDate { get; set; }
```

(Match the exact surrounding property order/spacing already in the file — read it first before editing.)

- [ ] **Step 2: Write the migration**

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [Migration("20260722090000_AddTargetExamDateFields")]
    public partial class AddTargetExamDateFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TargetExamDateSetByUser",
                table: "Goals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TargetExamDate",
                table: "LearnerRegistrationProfiles",
                type: "date",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetExamDate",
                table: "LearnerRegistrationProfiles");

            migrationBuilder.DropColumn(
                name: "TargetExamDateSetByUser",
                table: "Goals");
        }
    }
}
```

Before writing this, confirm the actual table name EF uses for `LearnerRegistrationProfile` (verified for `LearnerGoal` → `"Goals"` via its `DbSet<LearnerGoal> Goals` declaration with no `.ToTable` override in `LearnerDbContext.cs:12`; do the same one-line check — grep `DbSet<LearnerRegistrationProfile>` in `LearnerDbContext.cs` — for `LearnerRegistrationProfile` before assuming `"LearnerRegistrationProfiles"`).

- [ ] **Step 3: Build to verify the migration and entity compile**

Run: `pnpm run backend:build`
Expected: build succeeds, no EF/Roslyn errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/OetLearner.Api/Domain/Entities.cs backend/src/OetLearner.Api/Domain/SignupEntities.cs backend/src/OetLearner.Api/Data/Migrations/20260722090000_AddTargetExamDateFields.cs
git commit -m "feat(goals): add TargetExamDateSetByUser + registration-profile TargetExamDate columns"
```

---

### Task A2: Backend plumbing — lazy goal creation honors the registered exam date

**Files:**
- Modify: `backend/src/OetLearner.Api/Services/LearnerService.cs:5934-5954` (`CreateDefaultGoal`)
- Modify: `backend/src/OetLearner.Api/Services/LearnerService.cs:5418-5474` (`EnsureLearnerProfileStateAsync`)
- Modify: `backend/src/OetLearner.Api/Services/LearnerService.cs:5717-5730` (`EnsureLearnerProfileAsync`)
- Modify: `backend/src/OetLearner.Api/Services/LearnerService.cs:5782-5793` (add `ResolveRegisteredTargetExamDateAsync` next to `ResolveRegisteredTargetCountryAsync`)
- Test: `backend/tests/OetLearner.Api.Tests/Services/LearnerServiceGoalTests.cs` (create if no existing goal-creation test file matches; otherwise add to the closest existing one — search `backend/tests/OetLearner.Api.Tests` for `CreateDefaultGoal` or `EnsureLearnerProfileAsync` first)

**Interfaces:**
- Consumes: `LearnerRegistrationProfile.TargetExamDate` (Task A1).
- Produces: `CreateDefaultGoal(userId, professionId, targetCountry, registeredTargetExamDate, now)` — new 4th positional param before `now`, consumed by nothing else yet (both call sites updated in this same task).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task EnsureLearnerProfileAsync_UsesRegisteredExamDate_WhenPresent()
{
    // Arrange: a learner + registration profile with a real TargetExamDate, no Goal row yet.
    var userId = "usr_test_examdate";
    await Db.Users.AddAsync(new LearnerUser { Id = userId, DisplayName = "Test", Email = "t@example.com", ActiveProfessionId = "nursing", AccountStatus = "active", CreatedAt = DateTimeOffset.UtcNow, LastActiveAt = DateTimeOffset.UtcNow });
    var registeredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90));
    await Db.LearnerRegistrationProfiles.AddAsync(new LearnerRegistrationProfile
    {
        Id = "signup_test", ApplicationUserAccountId = "auth_test", LearnerUserId = userId,
        FirstName = "Test", LastName = "User", ExamTypeId = "oet", ProfessionId = "nursing",
        SessionId = string.Empty, CountryTarget = "Australia", MobileNumber = "+10000000000",
        TargetExamDate = registeredDate, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    });
    await Db.SaveChangesAsync();

    // Act: any method that triggers lazy goal creation, e.g. GetGoalsAsync.
    await Service.GetGoalsAsync(userId, CancellationToken.None);

    // Assert
    var goal = await Db.Goals.SingleAsync(g => g.UserId == userId);
    Assert.Equal(registeredDate, goal.TargetExamDate);
    Assert.True(goal.TargetExamDateSetByUser);
}

[Fact]
public async Task EnsureLearnerProfileAsync_FallsBackToPlaceholder_WhenNoRegisteredExamDate()
{
    var userId = "usr_test_noexamdate";
    await Db.Users.AddAsync(new LearnerUser { Id = userId, DisplayName = "Test", Email = "t2@example.com", ActiveProfessionId = "nursing", AccountStatus = "active", CreatedAt = DateTimeOffset.UtcNow, LastActiveAt = DateTimeOffset.UtcNow });
    await Db.SaveChangesAsync(); // no LearnerRegistrationProfile row at all — mirrors admin-created users pre-this-feature

    await Service.GetGoalsAsync(userId, CancellationToken.None);

    var goal = await Db.Goals.SingleAsync(g => g.UserId == userId);
    Assert.False(goal.TargetExamDateSetByUser);
}
```

(Adjust constructor calls/fixture setup — `Db`/`Service` — to match whatever base test fixture the existing `LearnerService` tests already use; open an existing `backend/tests/OetLearner.Api.Tests/Services/LearnerService*Tests.cs` file first to copy its exact fixture pattern.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~LearnerServiceGoalTests"`
Expected: FAIL — `TargetExamDateSetByUser` doesn't exist yet / `CreateDefaultGoal` signature mismatch (whichever compiles first; if it fails to compile, that also counts as "fails" for this step).

- [ ] **Step 3: Implement**

Add the resolver next to `ResolveRegisteredTargetCountryAsync` (`LearnerService.cs:5782`):

```csharp
    private async Task<DateOnly?> ResolveRegisteredTargetExamDateAsync(string userId, CancellationToken cancellationToken)
    {
        return await db.LearnerRegistrationProfiles
            .AsNoTracking()
            .Where(x => x.LearnerUserId == userId)
            .Select(x => x.TargetExamDate)
            .SingleOrDefaultAsync(cancellationToken);
    }
```

Update `CreateDefaultGoal` (`LearnerService.cs:5934-5954`):

```csharp
    private static LearnerGoal CreateDefaultGoal(string userId, string? professionId, string targetCountry, DateOnly? registeredTargetExamDate, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProfessionId = string.IsNullOrWhiteSpace(professionId) ? "nursing" : professionId,
            TargetExamDate = registeredTargetExamDate ?? DateOnly.FromDateTime(now.UtcDateTime.AddMonths(3)),
            TargetExamDateSetByUser = registeredTargetExamDate.HasValue,
            OverallGoal = "Build a strong OET foundation and stay ready for exam day.",
            TargetWritingScore = 350,
            TargetSpeakingScore = 350,
            TargetReadingScore = 350,
            TargetListeningScore = 350,
            PreviousAttempts = 0,
            WeakSubtestsJson = JsonSupport.Serialize(new[] { "writing", "speaking" }),
            StudyHoursPerWeek = 10,
            TargetCountry = targetCountry,
            TargetOrganization = "AHPRA",
            DraftStateJson = JsonSupport.Serialize(new Dictionary<string, object?>()),
            UpdatedAt = now,
            ExamFamilyCode = "oet"
        };
```

Update both call sites. `EnsureLearnerProfileStateAsync` (`LearnerService.cs:5464-5474`) already has `loaded.Goal`/`loaded.User` from one query — extend the initial projection to also select the registration's `TargetExamDate` (mirror how `RegisteredTargetCountry` is already projected at line 5440), then pass it through:

```csharp
                    RegisteredTargetCountry = registration == null ? null : registration.CountryTarget,
                    RegisteredTargetExamDate = registration == null ? (DateOnly?)null : registration.TargetExamDate,
```

```csharp
        if (goal is null)
        {
            goal = CreateDefaultGoal(
                loaded.User.Id,
                loaded.User.ActiveProfessionId,
                registeredTargetCountry,
                loaded.RegisteredTargetExamDate,
                now);
            db.Goals.Add(goal);
            changed = true;
        }
```

`EnsureLearnerProfileAsync` (`LearnerService.cs:5717-5730`):

```csharp
        var registeredTargetCountry = await ResolveRegisteredTargetCountryAsync(userId, cancellationToken);
        var registeredTargetExamDate = await ResolveRegisteredTargetExamDateAsync(userId, cancellationToken);

        var goal = await db.Goals.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (goal is null)
        {
            goal = CreateDefaultGoal(userId, user.ActiveProfessionId, registeredTargetCountry, registeredTargetExamDate, now);
            db.Goals.Add(goal);
            changed = true;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~LearnerServiceGoalTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Services/LearnerService.cs backend/tests/OetLearner.Api.Tests/Services/LearnerServiceGoalTests.cs
git commit -m "feat(goals): lazy goal creation honors registered target exam date"
```

---

### Task A3: Registration requires the exam date (backend)

**Files:**
- Modify: `backend/src/OetLearner.Api/Contracts/AuthRequests.cs:3-24` (`RegisterRequest`)
- Modify: `backend/src/OetLearner.Api/Services/AuthService.cs:60-219` (`RegisterLearnerAsync`)
- Test: `backend/tests/OetLearner.Api.Tests` — search for an existing `AuthServiceTests`/`RegisterLearnerAsync` test file and add to it.

**Interfaces:**
- Consumes: nothing new.
- Produces: `RegisterRequest.TargetExamDate` (`DateOnly?`, required at runtime via a thrown validation error when null/past) — persisted onto `LearnerRegistrationProfile.TargetExamDate`, consumed by Task A2's lazy goal creation.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task RegisterLearnerAsync_Throws_WhenTargetExamDateMissing()
{
    var request = ValidRegisterRequest() with { TargetExamDate = null };
    await Assert.ThrowsAsync<ApiException>(() => Service.RegisterLearnerAsync(request, CancellationToken.None));
}

[Fact]
public async Task RegisterLearnerAsync_Throws_WhenTargetExamDateInPast()
{
    var request = ValidRegisterRequest() with { TargetExamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)) };
    await Assert.ThrowsAsync<ApiException>(() => Service.RegisterLearnerAsync(request, CancellationToken.None));
}

[Fact]
public async Task RegisterLearnerAsync_PersistsTargetExamDate_OnRegistrationProfile()
{
    var examDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6));
    var request = ValidRegisterRequest() with { TargetExamDate = examDate };
    await Service.RegisterLearnerAsync(request, CancellationToken.None);

    var profile = await Db.LearnerRegistrationProfiles.SingleAsync(p => p.LearnerUser.Email == request.Email);
    Assert.Equal(examDate, profile.TargetExamDate);
}
```

(`ValidRegisterRequest()` — build from whatever fixture helper the existing `AuthServiceTests` already use for a passing registration call; copy its exact shape, just add `TargetExamDate` to the `with` expression list above.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~AuthServiceTests"`
Expected: FAIL — compile error (`TargetExamDate` doesn't exist on `RegisterRequest`) or the "throws" tests fail because nothing currently rejects a missing date.

- [ ] **Step 3: Implement**

`AuthRequests.cs` — add the field to `RegisterRequest` (position doesn't matter for a record with named args, but keep it near `CountryTarget` for readability):

```csharp
public record RegisterRequest(
    string Email,
    string Password,
    string Role,
    string? DisplayName,
    string? FirstName = null,
    string? LastName = null,
    string? MobileNumber = null,
    string? ExamTypeId = null,
    string? ProfessionId = null,
    string? CountryTarget = null,
    DateOnly? TargetExamDate = null,
    bool? AgreeToTerms = null,
    bool? AgreeToPrivacy = null,
    bool? MarketingOptIn = null,
    string? ExternalRegistrationToken = null,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null,
    string? UtmTerm = null,
    string? UtmContent = null,
    string? ReferrerUrl = null,
    string? LandingPath = null);
```

`AuthService.cs` — in `RegisterLearnerAsync`, right after the existing `countryTarget` resolution (line 100), add:

```csharp
        var countryTarget = TargetCountryOptions.Canonicalize(request.CountryTarget);

        if (request.TargetExamDate is null)
        {
            throw ApiException.Validation("target_exam_date_required", "Your target OET exam date is required.");
        }
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (request.TargetExamDate.Value < today)
        {
            throw ApiException.Validation("target_exam_date_in_past", "Your target OET exam date must be today or later.");
        }
        var targetExamDate = request.TargetExamDate.Value;
```

Then in the `registrationProfile` construction (line 169-193), add the field:

```csharp
        var registrationProfile = new LearnerRegistrationProfile
        {
            Id = $"signup_{Guid.NewGuid():N}",
            ApplicationUserAccountId = account.Id,
            LearnerUserId = learner.Id,
            FirstName = firstName,
            LastName = lastName,
            ExamTypeId = signupSelection.ExamType.Id,
            ProfessionId = signupSelection.Profession.Id,
            SessionId = string.Empty,
            CountryTarget = countryTarget,
            TargetExamDate = targetExamDate,
            MobileNumber = mobileNumber,
            AgreeToTerms = request.AgreeToTerms ?? false,
            AgreeToPrivacy = request.AgreeToPrivacy ?? false,
            MarketingOptIn = request.MarketingOptIn ?? false,
            UtmSource = request.UtmSource,
            UtmMedium = request.UtmMedium,
            UtmCampaign = request.UtmCampaign,
            UtmTerm = request.UtmTerm,
            UtmContent = request.UtmContent,
            ReferrerUrl = request.ReferrerUrl,
            LandingPath = request.LandingPath,
            CreatedAt = now,
            UpdatedAt = now
        };
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~AuthServiceTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Contracts/AuthRequests.cs backend/src/OetLearner.Api/Services/AuthService.cs backend/tests/OetLearner.Api.Tests/Services/AuthServiceTests.cs
git commit -m "feat(auth): require a target exam date at registration"
```

---

### Task A4: Registration requires the exam date (frontend)

**Files:**
- Modify: `lib/auth/schemas.ts` (`signupPayloadSchema`)
- Modify: `components/auth/register/register-original-form.tsx` (step 2 UI, defaultValues, submit payload, `nextStep` field list)
- Modify: `lib/types/auth.ts:72-93` (`RegisterLearnerInput`)
- Modify: `lib/auth-client.ts:392-424` (`registerLearner` request body, if it maps fields explicitly rather than spreading `input`)

**Interfaces:**
- Consumes: nothing new.
- Produces: `SignupPayloadFormValues.examDate: string` (ISO `YYYY-MM-DD`), forwarded as `RegisterLearnerInput.targetExamDate` → backend `RegisterRequest.TargetExamDate`.

- [ ] **Step 1: Check `lib/auth-client.ts` request mapping**

Read `lib/auth-client.ts:392-424` before editing. If `registerLearner` builds its POST body by spreading the whole `input` object, the new field just needs to exist on `RegisterLearnerInput` and be named `targetExamDate` (the backend's JSON deserializer is case-insensitive per the rest of the codebase's convention — confirm by checking how `countryTarget` round-trips, since it uses the same casing pattern). If it maps fields individually, add `targetExamDate: input.targetExamDate` explicitly.

- [ ] **Step 2: Add the schema field**

`lib/auth/schemas.ts`:

```typescript
import { z } from "zod";
import { isTargetCountry } from "./target-countries";

export const signupPayloadSchema = z
  .object({
    agreeToPrivacy: z.boolean(),
    agreeToTerms: z.boolean(),
    confirmPassword: z.string().min(8, "Confirm your password"),
    countryTarget: z
      .string()
      .min(2, "Select your target country")
      .refine(isTargetCountry, "Select a valid target country"),
    email: z.email("Enter a valid email address"),
    examDate: z
      .string()
      .min(1, "Select your target exam date")
      .refine((value) => {
        const parsed = new Date(`${value}T00:00:00`);
        if (Number.isNaN(parsed.getTime())) return false;
        const today = new Date();
        today.setHours(0, 0, 0, 0);
        return parsed >= today;
      }, "Target exam date must be today or later"),
    examTypeId: z.string().min(1, "Select an exam"),
    firstName: z.string().min(2, "First name is required"),
    lastName: z.string().min(2, "Last name is required"),
    marketingOptIn: z.boolean(),
    mobileNumber: z
      .string()
      .min(7, "Mobile number is required")
      .max(20, "Enter a valid mobile number"),
    password: z.string().min(8, "Password must be at least 8 characters"),
    professionId: z.string().min(1, "Select a profession"),
  })
  .refine((value) => value.password === value.confirmPassword, {
    message: "Passwords must match",
    path: ["confirmPassword"],
  })
  .refine((value) => value.agreeToTerms, {
    message: "Accept the terms to continue",
    path: ["agreeToTerms"],
  })
  .refine((value) => value.agreeToPrivacy, {
    message: "Accept the privacy policy to continue",
    path: ["agreeToPrivacy"],
  });

export type SignupPayloadFormValues = z.input<typeof signupPayloadSchema>;
```

- [ ] **Step 3: Add the UI field, defaultValues, step-2 validation list, and submit payload**

`register-original-form.tsx` — add `examDate: ''` to `defaultValues` (~line 85-98); add `'examDate'` to the step-2 `nextStep` field-trigger array (~line 168-169, so `Next Step` validates it before advancing):

```typescript
    const fields =
      step === 1
        ? (['firstName', 'lastName', 'email', 'mobileNumber'] as const)
        : (['examTypeId', 'professionId', 'countryTarget', 'examDate'] as const);
```

Add the input to the step-2 JSX block (~after the `countryTarget` field, line 403-420):

```tsx
            <div className={styles.field}>
              <label htmlFor="examDate">Target Exam Date</label>
              <input
                id="examDate"
                type="date"
                className={styles.input}
                min={new Date().toISOString().slice(0, 10)}
                {...form.register('examDate')}
              />
              <p className={styles.fieldHint}>{errors.examDate?.message}</p>
            </div>
```

Add to the `registerLearner(...)` payload in `handleSubmit` (~line 191-213):

```typescript
      await registerLearner(
        {
          email: values.email.trim(),
          password: values.password,
          displayName: `${values.firstName} ${values.lastName}`.trim(),
          firstName: values.firstName.trim(),
          lastName: values.lastName.trim(),
          mobileNumber: values.mobileNumber.trim(),
          examTypeId: values.examTypeId,
          professionId: values.professionId,
          countryTarget: values.countryTarget,
          targetExamDate: values.examDate,
          agreeToTerms: values.agreeToTerms,
          agreeToPrivacy: values.agreeToPrivacy,
          marketingOptIn: values.marketingOptIn,
          externalRegistrationToken: registrationToken,
          utmSource: attribution.utmSource,
          utmMedium: attribution.utmMedium,
          utmCampaign: attribution.utmCampaign,
          utmTerm: attribution.utmTerm,
          utmContent: attribution.utmContent,
          referrerUrl: attribution.referrer,
          landingPath: attribution.landingPath,
        },
        { persistSession: false },
      );
```

`lib/types/auth.ts:72-93` — add to `RegisterLearnerInput`:

```typescript
export interface RegisterLearnerInput {
  email: string;
  password: string;
  displayName?: string | null;
  firstName: string;
  lastName: string;
  mobileNumber: string;
  examTypeId: string;
  professionId: string;
  countryTarget: string;
  targetExamDate: string;
  agreeToTerms: boolean;
  agreeToPrivacy: boolean;
  marketingOptIn: boolean;
  externalRegistrationToken?: string | null;
  utmSource?: string | null;
  utmMedium?: string | null;
  utmCampaign?: string | null;
  utmTerm?: string | null;
  utmContent?: string | null;
  referrerUrl?: string | null;
  landingPath?: string | null;
}
```

- [ ] **Step 4: Typecheck**

Run: `pnpm exec tsc --noEmit`
Expected: no new errors. If `lib/auth-client.ts` maps fields individually (checked in Step 1) and you didn't add `targetExamDate` there, this step will surface it as an unused-property/type error — fix it now.

- [ ] **Step 5: Manual verification**

Start the dev server via `preview_start` (`{name: "dev"}` or the project's configured launch entry), navigate to `/register`, fill step 1, advance to step 2, confirm the exam-date input renders with a `min` of today and blocks "Next Step" when empty or in the past, fill a valid future date, complete step 3, submit, and confirm the account is created (check `read_network_requests` for a 200 on `POST /v1/auth/register`).

- [ ] **Step 6: Commit**

```bash
git add lib/auth/schemas.ts components/auth/register/register-original-form.tsx lib/types/auth.ts lib/auth-client.ts
git commit -m "feat(auth): collect target exam date on the registration form"
```

---

### Task A5: Admin Add-User requires the exam date (backend)

**Files:**
- Modify: `backend/src/OetLearner.Api/Contracts/AdminRequests.cs:253-283` (`AdminUserProfileUpdateRequest`, `AdminUserCreateRequest`)
- Modify: `backend/src/OetLearner.Api/Services/AdminService.cs:3190-3372` (`CreateUserAsync`)
- Modify: `backend/src/OetLearner.Api/Services/AdminService.cs` — wherever `UpdateUserProfileAsync` maps `AdminUserProfileUpdateRequest` fields onto `LearnerRegistrationProfile` (search for `request.CountryTarget` inside that method and add `TargetExamDate` next to it)
- Test: search `backend/tests/OetLearner.Api.Tests` for an existing `AdminService`/`CreateUserAsync` test file and add to it.

**Interfaces:**
- Consumes: `LearnerRegistrationProfile.TargetExamDate` (Task A1), same lazy-creation pickup as Task A2/A3.
- Produces: `AdminUserCreateRequest.TargetExamDate` (`DateOnly?`, required when `Role == Learner`).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task CreateUserAsync_Throws_WhenLearnerMissingTargetExamDate()
{
    var request = ValidLearnerCreateRequest() with { TargetExamDate = null };
    await Assert.ThrowsAsync<ApiException>(() => Service.CreateUserAsync("admin_1", "Admin", request, CancellationToken.None));
}

[Fact]
public async Task CreateUserAsync_DoesNotRequireExamDate_ForExpertRole()
{
    var request = ValidExpertCreateRequest() with { TargetExamDate = null };
    var result = await Service.CreateUserAsync("admin_1", "Admin", request, CancellationToken.None);
    Assert.NotNull(result);
}

[Fact]
public async Task CreateUserAsync_PersistsTargetExamDate_ForLearner()
{
    var examDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(4));
    var request = ValidLearnerCreateRequest() with { TargetExamDate = examDate };
    dynamic result = await Service.CreateUserAsync("admin_1", "Admin", request, CancellationToken.None);
    string userId = result.id;

    var profile = await Db.LearnerRegistrationProfiles.SingleAsync(p => p.LearnerUserId == userId);
    Assert.Equal(examDate, profile.TargetExamDate);
}
```

(Copy `ValidLearnerCreateRequest()`/`ValidExpertCreateRequest()` fixture shape from whatever the existing `AdminService` create-user tests already use.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~AdminServiceTests&FullyQualifiedName~CreateUser"`
Expected: FAIL — compile error (no `TargetExamDate` on `AdminUserCreateRequest`) or missing-validation failure.

- [ ] **Step 3: Implement**

`AdminRequests.cs` — add to both records:

```csharp
public record AdminUserProfileUpdateRequest(
    string? DisplayName,
    string? FirstName,
    string? LastName,
    string? MobileNumber,
    string? ProfessionId,
    string? ExamTypeId,
    string? CountryTarget,
    DateOnly? TargetExamDate,
    string? Timezone,
    string? Locale,
    bool? MarketingOptIn,
    bool? AgreeToTerms,
    bool? AgreeToPrivacy,
    string[]? Specialties,
    string? Reason);

public record AdminUserCreateRequest(
    string Name,
    string Email,
    string Role,
    string? ProfessionId,
    string? MobileNumber,
    DateOnly? TargetExamDate,
    string? Password,
    bool SendInvite);
```

`AdminService.cs` `CreateUserAsync` — add the required check right after the existing profession-required check (after line 3226, still inside the `role is Learner or Expert` branch — but exam date is a LEARNER-only concept, so gate on `role == Learner` specifically):

```csharp
        if (role == ApplicationUserRoles.Learner)
        {
            if (request.TargetExamDate is null)
            {
                throw ApiException.Validation("target_exam_date_required", "A target exam date is required for learner accounts.");
            }
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            if (request.TargetExamDate.Value < today)
            {
                throw ApiException.Validation("target_exam_date_in_past", "The target exam date must be today or later.");
            }
        }
```

Then extend the existing best-effort profile update block (line 3335-3360) to always run for learners (not only when `MobileNumber` is set), so the exam date persists even if the admin left phone blank:

```csharp
        if (role == ApplicationUserRoles.Learner)
        {
            var (firstName, lastName) = SplitDisplayName(displayName);
            try
            {
                await UpdateUserProfileAsync(adminId, adminName, userId, new AdminUserProfileUpdateRequest(
                    DisplayName: null,
                    FirstName: firstName,
                    LastName: lastName,
                    MobileNumber: string.IsNullOrWhiteSpace(request.MobileNumber) ? null : request.MobileNumber.Trim(),
                    ProfessionId: professionId,
                    ExamTypeId: null,
                    CountryTarget: null,
                    TargetExamDate: request.TargetExamDate,
                    Timezone: null,
                    Locale: null,
                    MarketingOptIn: null,
                    AgreeToTerms: null,
                    AgreeToPrivacy: null,
                    Specialties: null,
                    Reason: "admin_add_user"), ct);
            }
            catch (Exception)
            {
                // Non-fatal: account exists; admin can edit the profile afterwards.
            }
        }
```

Finally, open `UpdateUserProfileAsync` in `AdminService.cs` (search for the method definition — it's referenced but not yet read in this plan; grep `private async Task.*UpdateUserProfileAsync` or `internal async Task.*UpdateUserProfileAsync` in that file) and find where it maps `request.CountryTarget` onto the `LearnerRegistrationProfile` row (a line like `if (request.CountryTarget is not null) profile.CountryTarget = ...;`). Add directly beneath it:

```csharp
            if (request.TargetExamDate is not null) profile.TargetExamDate = request.TargetExamDate;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~AdminServiceTests&FullyQualifiedName~CreateUser"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Contracts/AdminRequests.cs backend/src/OetLearner.Api/Services/AdminService.cs backend/tests/OetLearner.Api.Tests/Services/AdminServiceTests.cs
git commit -m "feat(admin): require a target exam date when adding a learner"
```

---

### Task A6: Admin Add-User requires the exam date (frontend)

**Files:**
- Modify: `components/admin/user-access/add-user-modal.tsx`
- Modify: `lib/user-access.ts:88-96` (`CreateAdminUserPayload`)

**Interfaces:**
- Consumes: nothing new.
- Produces: `CreateAdminUserPayload.targetExamDate: string` → backend `AdminUserCreateRequest.TargetExamDate`.

- [ ] **Step 1: Add the field to the payload type**

`lib/user-access.ts`:

```typescript
export interface CreateAdminUserPayload {
  name: string;
  email: string;
  role: 'learner';
  professionId: string;
  targetExamDate: string;
  mobileNumber?: string;
  password?: string;
  sendInvite: boolean;
}
```

- [ ] **Step 2: Add the UI field and required-validation check**

`add-user-modal.tsx` — add state near `professionId` (~line 43):

```typescript
  const [targetExamDate, setTargetExamDate] = useState('');
```

Reset it in `resetForm()` (~line 52-61):

```typescript
  function resetForm() {
    setName('');
    setEmail('');
    setMobileNumber('');
    setProfessionId('');
    setTargetExamDate('');
    setLoginSetup('invite');
    setPassword('');
    setAccess(initialAccess());
    setError(null);
  }
```

Add to the required-field validation block in `handleSubmit` (~line 83-86, right after the profession check):

```typescript
    if (!professionId) {
      setError('Please select a profession.');
      return;
    }
    if (!targetExamDate) {
      setError('Please set the candidate\'s target exam date.');
      return;
    }
```

Thread it into the `createAdminUser` call (~line 94-102):

```typescript
      const created = await createAdminUser({
        name: trimmedName,
        email: trimmedEmail,
        role: 'learner',
        professionId,
        targetExamDate,
        mobileNumber: mobileNumber.trim() || undefined,
        password: loginSetup === 'password' ? password : undefined,
        sendInvite: loginSetup === 'invite',
      });
```

Add the input to the form JSX, in the same grid as Profession (~line 148-170):

```tsx
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input label="Full Name" value={name} onChange={(event) => setName(event.target.value)} disabled={isSubmitting} />
          <Input
            label="Email Address"
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            disabled={isSubmitting}
          />
          <Input
            label="Phone Number"
            value={mobileNumber}
            onChange={(event) => setMobileNumber(event.target.value)}
            disabled={isSubmitting}
          />
          <Select
            label="Profession"
            value={professionId}
            onChange={(event) => setProfessionId(event.target.value)}
            options={[{ value: '', label: 'Select a profession...' }, ...professionOptions]}
            disabled={isSubmitting}
          />
          <Input
            label="Target Exam Date"
            type="date"
            value={targetExamDate}
            onChange={(event) => setTargetExamDate(event.target.value)}
            min={new Date().toISOString().slice(0, 10)}
            disabled={isSubmitting}
          />
        </div>
```

(Check the `Input` component's prop signature in `components/ui/form-controls` supports `type="date"` and `min` — it's a thin wrapper around a native `<input>` in every other usage seen so far, so this should pass through unchanged; verify quickly by reading that file if unsure.)

- [ ] **Step 3: Typecheck**

Run: `pnpm exec tsc --noEmit`
Expected: no new errors.

- [ ] **Step 4: Manual verification**

`preview_start`, sign in as admin, open the Add User modal, confirm the new date field renders, blocks submit when empty, and a successful creation round-trips (check network request body includes `targetExamDate`).

- [ ] **Step 5: Commit**

```bash
git add components/admin/user-access/add-user-modal.tsx lib/user-access.ts
git commit -m "feat(admin): collect target exam date on the Add User modal"
```

---

### Task A7: Onboarding hard-gate state (backend)

**Files:**
- Modify: `backend/src/OetLearner.Api/Services/LearnerService.cs:297-313` (`GetOnboardingStateAsync`, `BuildOnboardingStateDto`)
- Modify: `backend/src/OetLearner.Api/Services/LearnerService.cs:493-495` (`PatchGoalsAsync` — TargetExamDate assignment)
- Test: search `backend/tests/OetLearner.Api.Tests` for an existing onboarding-state test file and add to it; if none exists, add to whatever file already covers `PatchGoalsAsync`.

**Interfaces:**
- Consumes: `LearnerGoal.TargetExamDateSetByUser` (Task A1).
- Produces: `GetOnboardingStateAsync` response gains `examDateRequired: bool` — consumed by Task A8's frontend gate.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task GetOnboardingStateAsync_ExamDateRequired_WhenGoalPlaceholder()
{
    var userId = await SeedLearnerWithGoalAsync(examDateSetByUser: false);
    dynamic state = await Service.GetOnboardingStateAsync(userId, CancellationToken.None);
    Assert.True((bool)state.examDateRequired);
}

[Fact]
public async Task GetOnboardingStateAsync_ExamDateNotRequired_WhenGoalConfirmed()
{
    var userId = await SeedLearnerWithGoalAsync(examDateSetByUser: true);
    dynamic state = await Service.GetOnboardingStateAsync(userId, CancellationToken.None);
    Assert.False((bool)state.examDateRequired);
}

[Fact]
public async Task PatchGoalsAsync_SetsExamDateSetByUser_WhenTargetExamDateProvided()
{
    var userId = await SeedLearnerWithGoalAsync(examDateSetByUser: false);
    await Service.PatchGoalsAsync(userId, new PatchGoalsRequest(
        ProfessionId: null, TargetExamDate: DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),
        OverallGoal: null, ExamFamilyCode: null, TargetWritingScore: null, TargetSpeakingScore: null,
        TargetReadingScore: null, TargetListeningScore: null, PreviousAttempts: null, WeakSubtests: null,
        StudyHoursPerWeek: null, TargetCountry: null, TargetOrganization: null, DraftState: null), CancellationToken.None);

    var goal = await Db.Goals.SingleAsync(g => g.UserId == userId);
    Assert.True(goal.TargetExamDateSetByUser);
}
```

(Write `SeedLearnerWithGoalAsync(bool examDateSetByUser)` as a small private test helper that inserts a `LearnerUser` + `LearnerGoal` row directly with the given flag — mirrors whatever seeding helper the existing `LearnerService` tests already use.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~OnboardingState|FullyQualifiedName~PatchGoalsAsync_SetsExamDate"`
Expected: FAIL

- [ ] **Step 3: Implement**

`GetOnboardingStateAsync` / `BuildOnboardingStateDto` (`LearnerService.cs:297-313`):

```csharp
    public async Task<object> GetOnboardingStateAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await EnsureUserAsync(userId, cancellationToken);
        var examDateSetByUser = await db.Goals.AsNoTracking()
            .Where(g => g.UserId == userId)
            .Select(g => (bool?)g.TargetExamDateSetByUser)
            .SingleOrDefaultAsync(cancellationToken) ?? false;
        return BuildOnboardingStateDto(user, examDateRequired: !examDateSetByUser);
    }

    private static object BuildOnboardingStateDto(LearnerUser user, bool examDateRequired) => new
    {
        completed = user.OnboardingCompleted,
        currentStep = user.OnboardingCurrentStep,
        stepCount = user.OnboardingStepCount,
        canSkip = false,
        startedAt = user.OnboardingStartedAt,
        completedAt = user.OnboardingCompletedAt,
        checkpoint = user.OnboardingCompleted ? "goals" : "welcome",
        resumeRoute = user.OnboardingCompleted ? "/dashboard" : "/onboarding",
        examDateRequired
    };
```

`PatchGoalsAsync` (`LearnerService.cs:493-495`):

```csharp
        if (request.TargetExamDate.HasValue)
        {
            goal.TargetExamDate = request.TargetExamDate;
            goal.TargetExamDateSetByUser = true;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~OnboardingState|FullyQualifiedName~PatchGoalsAsync_SetsExamDate"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Services/LearnerService.cs backend/tests/OetLearner.Api.Tests/Services/LearnerServiceGoalTests.cs
git commit -m "feat(onboarding): expose examDateRequired and set the confirmed flag on goal save"
```

---

### Task A8: Onboarding hard-gate (frontend) + required `/goals` field

**Files:**
- Modify: `lib/api.ts:1349-1359` (`fetchOnboardingState` return type)
- Create: `hooks/use-exam-date-gate.ts`
- Modify: `components/auth/auth-guard.tsx`
- Modify: `app/goals/page.tsx` (`goalSchema.examDate` becomes required; reset the gate cache after a successful save)

**Interfaces:**
- Consumes: `fetchOnboardingState()` (existing, `lib/api.ts:1349`), extended with `examDateRequired: boolean`.
- Produces: `useExamDateGate(enabled: boolean): boolean | null`, `resetExamDateGateCache(): void` — consumed by `AuthGuard` and `app/goals/page.tsx`.

- [ ] **Step 1: Extend `fetchOnboardingState`'s return type**

`lib/api.ts:1349-1359`:

```typescript
export async function fetchOnboardingState(): Promise<{ completed: boolean; currentStep: number; stepCount: number; canSkip: boolean; checkpoint: string; resumeRoute: string; examDateRequired: boolean; }> {
  const data = await apiRequest<ApiRecord>('/v1/learner/onboarding/state');
  return {
    completed: Boolean(data.completed),
    currentStep: Number(data.currentStep ?? 1),
    stepCount: Number(data.stepCount ?? 4),
    canSkip: Boolean(data.canSkip),
    checkpoint: data.checkpoint ?? 'welcome',
    resumeRoute: data.resumeRoute ?? '/onboarding',
    examDateRequired: Boolean(data.examDateRequired),
  };
}
```

- [ ] **Step 2: Create the gate hook**

`hooks/use-exam-date-gate.ts`:

```typescript
'use client';

import { useEffect, useState } from 'react';
import { fetchOnboardingState } from '@/lib/api';

let cachedPromise: Promise<boolean> | null = null;

function loadExamDateRequired(): Promise<boolean> {
  if (!cachedPromise) {
    cachedPromise = fetchOnboardingState()
      .then((state) => state.examDateRequired)
      .catch(() => false);
  }
  return cachedPromise;
}

/** Call after a successful sign-out or a successful /goals exam-date save so
 * the next check re-fetches instead of replaying a stale cached value. */
export function resetExamDateGateCache() {
  cachedPromise = null;
}

/** True once we know the signed-in learner has no confirmed target exam
 * date. Fetched once per session (module-level cache) rather than once per
 * guarded page, since AuthGuard wraps ~250 pages. */
export function useExamDateGate(enabled: boolean): boolean | null {
  const [required, setRequired] = useState<boolean | null>(null);

  useEffect(() => {
    if (!enabled) {
      setRequired(null);
      return;
    }

    let cancelled = false;
    loadExamDateRequired().then((value) => {
      if (!cancelled) setRequired(value);
    });
    return () => {
      cancelled = true;
    };
  }, [enabled]);

  return required;
}
```

- [ ] **Step 3: Wire the gate into `AuthGuard`**

`components/auth/auth-guard.tsx`:

```tsx
'use client';

import { useEffect } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { Lock } from 'lucide-react';
import type { ReactNode } from 'react';
import type { UserRole } from '@/lib/types/auth';
import { useAuth } from '@/contexts/auth-context';
import { defaultRouteForRole, roleSatisfiesRequired } from '@/lib/auth-routes';
import { useExamDateGate } from '@/hooks/use-exam-date-gate';

interface AuthGuardProps {
  children: ReactNode;
  requiredRole?: UserRole;
}

const EXAM_DATE_EXEMPT_PATHS = ['/goals', '/onboarding', '/onboarding-tour'];

export function AuthGuard({ children, requiredRole }: AuthGuardProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { loading, isAuthenticated, role, pendingMfaChallenge } = useAuth();
  const nextPath = pathname ?? '/';
  const isAuthRoute =
    nextPath === '/sign-in' ||
    nextPath === '/register' ||
    nextPath === '/register/success' ||
    nextPath === '/terms' ||
    nextPath === '/privacy' ||
    nextPath === '/forgot-password' ||
    nextPath === '/forgot-password/verify' ||
    nextPath === '/reset-password' ||
    nextPath === '/reset-password/success' ||
    nextPath === '/verify-email' ||
    nextPath === '/mfa/challenge' ||
    nextPath === '/mfa/recovery' ||
    nextPath === '/mfa/setup' ||
    nextPath.startsWith('/auth/callback/');

  const examDateGateEnabled =
    !isAuthRoute &&
    isAuthenticated &&
    role === 'learner' &&
    !EXAM_DATE_EXEMPT_PATHS.some((path) => nextPath === path || nextPath.startsWith(`${path}/`));
  const examDateRequired = useExamDateGate(examDateGateEnabled);

  useEffect(() => {
    if (isAuthRoute) {
      return;
    }

    if (loading) {
      return;
    }

    if (pendingMfaChallenge) {
      router.replace(`/mfa/challenge?next=${encodeURIComponent(nextPath)}`);
      return;
    }

    if (!isAuthenticated) {
      router.replace(`/sign-in?next=${encodeURIComponent(nextPath)}`);
      return;
    }

    if (requiredRole && !roleSatisfiesRequired(role, requiredRole)) {
      router.replace(role ? defaultRouteForRole(role) : '/');
      return;
    }

    if (examDateGateEnabled && examDateRequired) {
      router.replace('/goals?required=examDate');
    }
  }, [isAuthenticated, isAuthRoute, loading, nextPath, pendingMfaChallenge, requiredRole, role, router, examDateGateEnabled, examDateRequired]);

  if (isAuthRoute) {
    return <>{children}</>;
  }

  const blockedOnExamDate = examDateGateEnabled && examDateRequired === true;

  if (loading || pendingMfaChallenge || !isAuthenticated || (requiredRole && !roleSatisfiesRequired(role, requiredRole)) || blockedOnExamDate) {
    return (
      <div className="flex min-h-screen w-full items-center justify-center bg-background-light px-6">
        <div className="flex flex-col items-center gap-4 text-center text-muted">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <Lock className="h-6 w-6 animate-pulse" />
          </div>
          <div className="space-y-1">
            <p className="text-sm font-semibold text-navy">Checking your session</p>
            <p className="text-xs text-muted">We&apos;re routing you to the correct workspace.</p>
          </div>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
```

- [ ] **Step 4: Make `/goals`'s exam date required and reset the gate cache on save**

`app/goals/page.tsx` — change the schema (~line 81):

```typescript
  examDate: z
    .string()
    .min(1, 'Your target exam date is required')
    .refine((value) => {
      const parsed = new Date(`${value}T00:00:00`);
      return !Number.isNaN(parsed.getTime());
    }, 'Enter a valid date'),
```

Remove the "Leave blank if not yet scheduled" hint text (~line 358) since the field is no longer optional — read the surrounding JSX first to edit precisely.

In the submit handler (~line 263, where `examDate: data.examDate || null` is sent), after a successful `updateUserProfile(...)` call, import and call `resetExamDateGateCache()` from `@/hooks/use-exam-date-gate` so `AuthGuard` re-checks immediately instead of showing a stale redirect on the next navigation:

```typescript
import { resetExamDateGateCache } from '@/hooks/use-exam-date-gate';
// ...inside the submit handler, after the profile update succeeds:
resetExamDateGateCache();
```

- [ ] **Step 5: Typecheck**

Run: `pnpm exec tsc --noEmit`
Expected: no new errors.

- [ ] **Step 6: Manual verification**

`preview_start`, sign in as an existing learner whose `LearnerGoal.TargetExamDateSetByUser` is `false` (any pre-existing seeded user qualifies today), navigate to `/dashboard` or any other guarded page, and confirm you're redirected to `/goals?required=examDate` instead of the page rendering. Fill in and save a real exam date, confirm the redirect stops on the next navigation. Then confirm `/goals` and `/onboarding` themselves never redirect (no loop).

- [ ] **Step 7: Commit**

```bash
git add lib/api.ts hooks/use-exam-date-gate.ts components/auth/auth-guard.tsx app/goals/page.tsx
git commit -m "feat(onboarding): hard-gate learners without a confirmed target exam date to /goals"
```

---

## Self-Review Notes

- **Spec coverage:** registration (A3/A4), admin Add-User (A5/A6), and onboarding-forced collection (A7/A8) are all covered; A1/A2 are the shared schema + plumbing both depend on.
- **Placeholder scan:** no TBD/"add validation"/"similar to Task N" left — every step has concrete code or an explicit "search for X, copy its pattern" instruction naming the exact thing to find.
- **Type consistency:** `TargetExamDate` (DateOnly, backend) ↔ `targetExamDate`/`examDate` (string, frontend ISO date) naming is intentionally different per side (matches the existing `countryTarget`/`CountryTarget` convention already in the codebase) — verified consistent across A3/A4 and A5/A6.
- **Known follow-up, not in scope here:** this plan does not touch the Full Mock Speaking AI/tutor gate — that depends on `TargetExamDate` and is its own plan, `2026-07-22-mock-speaking-ai-tutor-gate.md`.
