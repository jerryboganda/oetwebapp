using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing AI Learning Companion endpoints.
///
/// <para>
/// Two jobs, both of which the chat stream itself cannot do:
/// </para>
///
/// <list type="number">
///   <item><b>Session capability</b> — what this learner may do <i>before</i> they
///   type. The SignalR turn can only report a quota refusal after a message has
///   been sent, as a sentence in the transcript. That is the wrong shape for a
///   paywall: the surface needs capability, reason and the upgrade route up
///   front, so it can show an upgrade card instead of an unusable input box
///   (F-135, F-139…F-142).</item>
///   <item><b>Memory controls</b> — the companion's <c>save_user_note</c> and
///   <c>bookmark_recall_term</c> tools write rows on the learner's behalf, and
///   until now nothing let the learner see or delete them. A companion that can
///   remember things about you and never show you what it remembered is not
///   acceptable (F-047).</item>
/// </list>
///
/// <para>
/// UserId always comes from the JWT and is never accepted from input.
/// </para>
/// </summary>
public static class CompanionLearnerEndpoints
{
    /// <summary>
    /// Feature codes whose written rows count as companion memory. A note the
    /// learner wrote themselves, or one authored by a different AI feature (the
    /// Writing coach, say), is not the companion's to delete. The same three codes
    /// are what the quota plan's allow-list must contain for the companion to be
    /// usable, so they are defined once — on
    /// <see cref="CompanionAccessResolver"/> — and aliased here as a static field so
    /// the EF-translated <c>Contains</c> filters in the memory queries keep their
    /// existing shape.
    /// </summary>
    private static readonly string[] CompanionFeatureCodes = CompanionAccessResolver.CompanionFeatureCodes;

    public static IEndpointRouteBuilder MapCompanionLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/companion")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/session", GetSessionAsync);
        group.MapGet("/memory", GetMemoryAsync);
        group.MapGet("/memory/errors", GetMemoryErrorsAsync);
        group.MapGet("/handoffs", async (HttpContext http,
            OetLearner.Api.Services.Companion.ICompanionHandoffService handoffs,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            var rows = await handoffs.ListForUserAsync(userId, 20, ct);
            return Results.Ok(new
            {
                handoffs = rows.Select(h => new
                {
                    id = h.Id,
                    route = h.Route,
                    status = h.Status,
                    issue = h.Issue,
                    createdAt = h.CreatedAt,
                }),
            });
        });
        group.MapDelete("/memory/entries/{entryId}", DeleteMemoryEntryAsync);
        group.MapDelete("/memory/notes/{noteId}", DeleteNoteAsync);
        group.MapDelete("/memory/bookmarks/{bookmarkId}", DeleteBookmarkAsync);
        group.MapDelete("/memory", ResetMemoryAsync);
        group.MapGet("/memory/export", ExportMemoryAsync);
        group.MapGet("/preferences", GetPreferencesAsync);
        group.MapPut("/preferences", SavePreferencesAsync);

        return app;
    }

    private static async Task<IResult> GetSessionAsync(
        HttpContext http,
        ICompanionFeatureFlags flags,
        ICompanionContextResolver contexts,
        ICompanionDestinationRegistry destinations,
        ICompanionAccessResolver access,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var enabled = await flags.IsEnabledAsync(ct);
        var context = await contexts.ResolveAsync(userId, null, ct);

        // THE access gate, resolved by the one shared resolver (also used by the
        // operator read and by the chat turn itself). The master flag stays a
        // caller-side concern — as it always was — so "platform switched off" is
        // never confused with an entitlement denial, and its source stays null
        // rather than claiming an entitlement nobody evaluated.
        var decision = enabled
            ? await access.ResolveAsync(userId, ct)
            : new CompanionAccessDecision(
                false,
                CompanionAccessReasons.CompanionDisabled,
                null,
                null,
                null);

        // The upgrade route is resolved through the registry like every other
        // link, so the paywall cannot point somewhere the learner cannot open.
        var upgrade = decision.CanChat ? null : await destinations.ResolveAsync("pricing", context, ct);
        var topUp = await destinations.ResolveAsync("ai.packages", context, ct);

        return Results.Ok(new
        {
            enabled,
            persona = ResolvePersona(configuration),
            retrievalEnabled = context.RetrievalEnabled,
            actionsEnabled = context.ActionsEnabled,
            creditConsumptionEnabled = context.CreditConsumptionEnabled,
            // TV-006 / TV-007. The client must not render a numeric band while
            // this is false, and the prompt independently forbids producing one.
            scoreDisplayEnabled = context.ScoreDisplayEnabled,
            tier = context.Tier,
            hasSubscription = context.HasEligibleSubscription,
            professionId = context.ProfessionId,
            examTypeCode = context.ExamTypeCode,
            examDate = context.ExamDate?.ToString("yyyy-MM-dd"),
            daysUntilExam = context.DaysUntilExam,
            locale = context.Locale,
            aiCreditsRemaining = context.AiCreditsRemaining,
            access = new
            {
                canChat = decision.CanChat,
                reason = decision.Reason,
                // Provenance (SAMI §9 per-user layer): package_included /
                // admin_enabled / promotional / manually_disabled / expired /
                // none. `reason` remains the coarse block code the paywall has
                // always consumed, so an existing client keeps working.
                source = decision.Source,
                planCode = decision.PlanCode,
                planName = decision.PlanName,
                upgradeUrl = upgrade?.Url,
            },
            topUpUrl = topUp.Allowed ? topUp.Url : null,
        });
    }

    /// <summary>
    /// How this learner wants to be taught (F-011, F-050, F-052, F-055).
    /// Returns defaults rather than 404 when no row exists — "I have not chosen"
    /// and "the defaults apply" are the same state, and a 404 would make every
    /// caller special-case it.
    /// </summary>
    private static async Task<IResult> GetPreferencesAsync(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var row = await db.CompanionPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? new CompanionPreference { UserId = userId };

        return Results.Ok(Project(row));
    }

    private static async Task<IResult> SavePreferencesAsync(
        CompanionPreferenceRequest request,
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        if (!Enum.TryParse<CompanionTeachingStyle>(request.TeachingStyle, ignoreCase: true, out var style))
        {
            return Results.BadRequest(new { code = "unknown_teaching_style", message = "Teaching style is not recognised.", value = request.TeachingStyle });
        }

        if (!Enum.TryParse<CompanionExplanationDepth>(request.Depth, ignoreCase: true, out var depth))
        {
            return Results.BadRequest(new { code = "unknown_depth", message = "Explanation depth is not recognised.", value = request.Depth });
        }

        var row = await db.CompanionPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (row is null)
        {
            row = new CompanionPreference { UserId = userId };
            db.CompanionPreferences.Add(row);
        }

        row.TeachingStyle = style;
        row.Depth = depth;
        row.EnglishOnly = request.EnglishOnly;
        row.PreferWorkedExamples = request.PreferWorkedExamples;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Results.Ok(Project(row));
    }

    private static object Project(CompanionPreference row) => new
    {
        teachingStyle = row.TeachingStyle.ToString(),
        depth = row.Depth.ToString(),
        englishOnly = row.EnglishOnly,
        preferWorkedExamples = row.PreferWorkedExamples,
        updatedAt = row.UpdatedAt,
    };

    private static async Task<IResult> GetMemoryAsync(
        HttpContext http,
        LearnerDbContext db,
        OetLearner.Api.Services.Companion.ICompanionMemoryService memory,
        OetLearner.Api.Services.Companion.ICompanionJourneyService journeys,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var memoryEntries = await memory.GetCurrentAsync(userId, OetLearner.Api.Domain.CompanionMemoryLayers.Learning, ct);
        var journeyEntries = await memory.GetCurrentAsync(userId, OetLearner.Api.Domain.CompanionMemoryLayers.Journey, ct);
        var currentJourney = await journeys.GetCurrentAsync(userId, ct);

        var notes = await db.UserNotes
            .AsNoTracking()
            .Where(n => n.UserId == userId
                        && n.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(n.CreatedByFeatureCode))
            .OrderByDescending(n => n.CreatedAt)
            .Take(200)
            .Select(n => new
            {
                id = n.Id,
                title = n.Title,
                body = n.BodyMarkdown,
                createdAt = n.CreatedAt,
            })
            .ToListAsync(ct);

        var bookmarks = await (
            from b in db.RecallBookmarks.AsNoTracking()
            join t in db.VocabularyTerms.AsNoTracking() on b.VocabularyTermId equals t.Id into tj
            from t in tj.DefaultIfEmpty()
            where b.UserId == userId
                  && b.CreatedByFeatureCode != null
                  && CompanionFeatureCodes.Contains(b.CreatedByFeatureCode)
            orderby b.CreatedAt descending
            select new
            {
                id = b.Id,
                term = t != null ? t.Term : b.VocabularyTermId,
                createdAt = b.CreatedAt,
            })
            .Take(200)
            .ToListAsync(ct);

        return Results.Ok(new
        {
            notes,
            bookmarks,
            memories = memoryEntries.Select(m => new
            {
                id = m.Id,
                layer = m.Layer,
                kind = m.Kind,
                subtest = m.Subtest,
                content = m.Content,
                confirmedAt = m.ConfirmedAt,
                recordedAt = m.RecordedAt,
                provenanceType = m.ProvenanceType,
            }),
            journeyMemories = journeyEntries.Select(m => new
            {
                id = m.Id,
                kind = m.Kind,
                content = m.Content,
                recordedAt = m.RecordedAt,
            }),
            currentJourney = new { id = currentJourney.Id, label = currentJourney.Label, startedAt = currentJourney.StartedAt },
        });
    }

    /// <summary>The learner’s evidenced recurring errors (Error DNA), for the
    /// transparent weakness view. Evidence counts and mastery are shown so a
    /// learner can see WHY something is listed.</summary>
    private static async Task<IResult> GetMemoryErrorsAsync(
        HttpContext http,
        OetLearner.Api.Services.Companion.IErrorDnaService errorDna,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
        var weaknesses = await errorDna.TopWeaknessesAsync(userId, 30, ct);
        return Results.Ok(new
        {
            errors = weaknesses.Select(w => new
            {
                id = w.Id,
                category = w.Category,
                subtest = w.Subtest,
                pattern = w.Pattern,
                evidenceCount = w.EvidenceCount,
                mastery = w.MasteryScore,
                lastSeenAt = w.LastSeenAt,
                nextReviewAt = w.NextReviewAt,
            }),
        });
    }

    /// <summary>Deletes ONE memory entry (scoped memory control, F-047). Nothing
    /// else is touched — scores, exam dates and history in other entries stay.</summary>
    private static async Task<IResult> DeleteMemoryEntryAsync(
        HttpContext http,
        string entryId,
        OetLearner.Api.Services.Companion.ICompanionMemoryService memory,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
        var deleted = await memory.DeleteAsync(userId, entryId, ct);
        return deleted ? Results.Ok(new { deleted = true }) : Results.NotFound();
    }

    /// <summary>
    /// The same rows as <c>GET /memory</c>, as a downloadable JSON file.
    /// Separate from the listing endpoint because "show me" and "give me a copy
    /// I can keep" are different acts: this one is what a learner exercising a
    /// data-portability request actually needs, and it is served as an
    /// attachment so it lands as a file rather than a wall of text.
    /// </summary>
    private static async Task<IResult> ExportMemoryAsync(
        HttpContext http,
        LearnerDbContext db,
        OetLearner.Api.Services.Companion.ICompanionMemoryService memory,
        OetLearner.Api.Services.Companion.IErrorDnaService errorDna,
        OetLearner.Api.Services.Companion.ICompanionJourneyService journeys,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var allMemories = await memory.ExportAsync(userId, ct);
        var errorEntries = await errorDna.TopWeaknessesAsync(userId, 200, ct);
        var journeyRows = await journeys.GetHistoryAsync(userId, ct);

        var notes = await db.UserNotes
            .AsNoTracking()
            .Where(n => n.UserId == userId
                        && n.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(n.CreatedByFeatureCode))
            .OrderBy(n => n.CreatedAt)
            .Select(n => new
            {
                id = n.Id,
                title = n.Title,
                body = n.BodyMarkdown,
                createdByFeatureCode = n.CreatedByFeatureCode,
                createdAt = n.CreatedAt,
                updatedAt = n.UpdatedAt,
            })
            .ToListAsync(ct);

        var bookmarks = await (
            from b in db.RecallBookmarks.AsNoTracking()
            join t in db.VocabularyTerms.AsNoTracking() on b.VocabularyTermId equals t.Id into tj
            from t in tj.DefaultIfEmpty()
            where b.UserId == userId
                  && b.CreatedByFeatureCode != null
                  && CompanionFeatureCodes.Contains(b.CreatedByFeatureCode)
            orderby b.CreatedAt
            select new
            {
                id = b.Id,
                term = t != null ? t.Term : b.VocabularyTermId,
                definition = t != null ? t.Definition : null,
                createdByFeatureCode = b.CreatedByFeatureCode,
                createdAt = b.CreatedAt,
            })
            .ToListAsync(ct);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            exportedAt = DateTimeOffset.UtcNow,
            // No user id, email or name: the learner already knows who they are,
            // and a file that identifies them is a file that leaks if forwarded.
            notes,
            bookmarks,
            memories = allMemories.Select(m => new
            {
                layer = m.Layer,
                kind = m.Kind,
                subtest = m.Subtest,
                content = m.Content,
                confirmedAt = m.ConfirmedAt,
                supersededAt = m.SupersededAt,
                recordedAt = m.RecordedAt,
                provenanceType = m.ProvenanceType,
            }),
            errorDna = errorEntries.Select(w => new
            {
                category = w.Category,
                subtest = w.Subtest,
                pattern = w.Pattern,
                evidenceCount = w.EvidenceCount,
                mastery = w.MasteryScore,
                firstSeenAt = w.FirstSeenAt,
                lastSeenAt = w.LastSeenAt,
            }),
            journeys = journeyRows.Select(j => new
            {
                label = j.Label,
                startedAt = j.StartedAt,
                endedAt = j.EndedAt,
                outcome = j.Outcome,
                isActive = j.IsActive,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });

        return Results.File(payload, "application/json", "companion-memory.json");
    }

    private static async Task<IResult> DeleteNoteAsync(
        string noteId,
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        // Scoped by UserId as well as id: an id from another learner must read as
        // "not found", never as a successful delete.
        var deleted = await db.UserNotes
            .Where(n => n.Id == noteId
                        && n.UserId == userId
                        && n.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(n.CreatedByFeatureCode))
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> DeleteBookmarkAsync(
        string bookmarkId,
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var deleted = await db.RecallBookmarks
            .Where(b => b.Id == bookmarkId
                        && b.UserId == userId
                        && b.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(b.CreatedByFeatureCode))
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> ResetMemoryAsync(
        HttpContext http,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var notes = await db.UserNotes
            .Where(n => n.UserId == userId
                        && n.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(n.CreatedByFeatureCode))
            .ExecuteDeleteAsync(ct);

        var bookmarks = await db.RecallBookmarks
            .Where(b => b.UserId == userId
                        && b.CreatedByFeatureCode != null
                        && CompanionFeatureCodes.Contains(b.CreatedByFeatureCode))
            .ExecuteDeleteAsync(ct);

        return Results.Ok(new { notesDeleted = notes, bookmarksDeleted = bookmarks });
    }

    private static string ResolvePersona(IConfiguration configuration)
    {
        var configured = configuration[CompanionPromptComposer.PersonaSettingKey];
        return string.IsNullOrWhiteSpace(configured)
            ? CompanionPromptComposer.DefaultPersona
            : configured.Trim();
    }
}

/// <summary>Teaching-style update. Enum names are sent as strings so the wire
/// format survives a reordering of the enum.</summary>
public sealed record CompanionPreferenceRequest(
    string TeachingStyle,
    string Depth,
    bool EnglishOnly,
    bool PreferWorkedExamples);
