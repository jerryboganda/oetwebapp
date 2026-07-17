using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// Covers <see cref="ContentPaperService.BulkAsync"/> (Task T1): per-action
/// happy paths, status-gate failures recorded in the result, single summary
/// audit row, single-transaction rollback on a fatal mid-batch error, and the
/// writing→scenario projection bridge firing on bulk approve-publish.
/// </summary>
public class ContentPaperBulkActionTests
{
    private const string Prov = ContentDefaults.DefaultSourceProvenance;

    private static (LearnerDbContext db, ContentPaperService svc) BuildInMemory(
        IWritingTaskProjectionService? projection = null)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ContentPaperService(db, projection));
    }

    private static async Task<ContentPaper> SeedDraftListeningAsync(
        LearnerDbContext db, ContentPaperService svc, string title)
        => await svc.CreateAsync(new ContentPaperCreate(
            "listening", title, null, null, true, null, 40, null, null, 0, null, Prov),
            "admin-1", default);

    private static async Task<ContentPaper> SeedDraftReadingAsync(
        LearnerDbContext db, ContentPaperService svc, string title)
        => await svc.CreateAsync(new ContentPaperCreate(
            "reading", title, null, null, true, null, 40, null, null, 0, null, Prov),
            "admin-1", default);

    private static async Task<string> AddMediaAsync(LearnerDbContext db, string id)
    {
        db.MediaAssets.Add(new MediaAsset
        {
            Id = id,
            OriginalFilename = $"{id}.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            SizeBytes = 1024,
            StoragePath = $"uploads/published/aa/bb/{id}.pdf",
            Status = MediaAssetStatus.Ready,
            Sha256 = "aabbccddeeff" + id.PadRight(52, '0'),
            UploadedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<ContentPaper> SeedPublishableWritingAsync(
        LearnerDbContext db, ContentPaperService svc, string title, string slug)
    {
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "writing", title, slug, "medicine", false, null, 45, null, "routine_referral",
            0, "purpose,content", Prov), "admin-1", default);

        await AddMediaAsync(db, $"{slug}-cn");
        await AddMediaAsync(db, $"{slug}-ma");
        await svc.AttachAssetAsync(paper.Id, new ContentPaperAssetAttach(
            PaperAssetRole.CaseNotes, $"{slug}-cn", null, null, 0, true), "admin-1", default);
        await svc.AttachAssetAsync(paper.Id, new ContentPaperAssetAttach(
            PaperAssetRole.ModelAnswer, $"{slug}-ma", null, null, 1, true), "admin-1", default);

        var tracked = await db.ContentPapers.FirstAsync(p => p.Id == paper.Id);
        tracked.ExtractedTextJson = JsonSupport.Serialize(new
        {
            writingStructure = new
            {
                taskPrompt = "Using the case notes, write a referral letter to the patient's GP.",
                taskDate = "25 Mar 2023",
                writerRole = "Doctor",
                recipient = "Dr Smith, General Practitioner",
                purpose = "Referral for ongoing management",
                caseNotes = "Patient: Mr John Roberts, 58.\nDiagnosis: type 2 diabetes.",
                modelAnswerText = "Dear Dr Smith,\n\nThank you for reviewing Mr John Roberts.\n\nYours sincerely,",
                criteriaFocus = new[] { "purpose", "content", "conciseness" }
            }
        });
        await db.SaveChangesAsync();
        return paper;
    }

    [Fact]
    public async Task Bulk_archive_happy_path_archives_all_and_counts()
    {
        var (db, svc) = BuildInMemory();
        var p1 = await SeedDraftListeningAsync(db, svc, "L1");
        var p2 = await SeedDraftListeningAsync(db, svc, "L2");

        var result = await svc.BulkAsync("archive", [p1.Id, p2.Id], "admin-9", null, default);

        Assert.Equal(2, result.TotalRequested);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);

        var statuses = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Id == p1.Id || p.Id == p2.Id).Select(p => p.Status).ToListAsync();
        Assert.All(statuses, s => Assert.Equal(ContentStatus.Archived, s));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_archive_skips_already_archived()
    {
        var (db, svc) = BuildInMemory();
        var p1 = await SeedDraftListeningAsync(db, svc, "L1");
        await svc.ArchiveAsync(p1.Id, "admin-1", default);
        var p2 = await SeedDraftListeningAsync(db, svc, "L2");

        var result = await svc.BulkAsync("archive", [p1.Id, p2.Id], "admin-9", null, default);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_publish_records_status_gate_failure_in_errors()
    {
        var (db, svc) = BuildInMemory();
        // Reading drafts with no required QuestionPaper asset — publish gate
        // fails for each. (Listening is now exempt from required-asset gating,
        // so it no longer exercises this path.)
        var p1 = await SeedDraftReadingAsync(db, svc, "R1");
        var p2 = await SeedDraftReadingAsync(db, svc, "R2");

        var result = await svc.BulkAsync("publish", [p1.Id, p2.Id], "admin-9", null, default);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(2, result.Failed);
        Assert.Equal(2, result.Errors.Length);
        Assert.All(result.Errors, e => Assert.Contains("required asset roles", e));

        var statuses = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Id == p1.Id || p.Id == p2.Id).Select(p => p.Status).ToListAsync();
        Assert.All(statuses, s => Assert.Equal(ContentStatus.Draft, s));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_unknown_action_throws_argument_exception()
    {
        var (db, svc) = BuildInMemory();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.BulkAsync("frobnicate", [], "admin-1", null, default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_reject_without_reason_throws_argument_exception()
    {
        var (db, svc) = BuildInMemory();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.BulkAsync("reject", ["x"], "admin-1", "   ", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_writes_exactly_one_audit_row()
    {
        var (db, svc) = BuildInMemory();
        var p1 = await SeedDraftListeningAsync(db, svc, "L1");
        var p2 = await SeedDraftListeningAsync(db, svc, "L2");

        await svc.BulkAsync("archive", [p1.Id, p2.Id], "admin-9", null, default);

        var bulkAudits = await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "ContentPaperBulkAction").ToListAsync();
        Assert.Single(bulkAudits);
        Assert.Equal("admin-9", bulkAudits[0].ActorId);
        Assert.Contains("action=archive", bulkAudits[0].Details);
        // Regression: the affected ids belong in Details, not ResourceId, which is
        // varchar(64) in Postgres and overflowed when 2+ ~32-char ids were joined
        // (the InMemory test provider does not enforce the length, so this guards
        // the column invariant explicitly). See the bulk "Bulk action failed" 500.
        Assert.Equal("bulk", bulkAudits[0].ResourceId);
        Assert.True((bulkAudits[0].ResourceId?.Length ?? 0) <= 64);
        Assert.Contains(p1.Id, bulkAudits[0].Details);
        Assert.Contains(p2.Id, bulkAudits[0].Details);

        // No per-item archive audit rows leaked from the suppressed path.
        Assert.Empty(await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "ContentPaperArchived").ToListAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_submit_for_review_then_reject_records_reason_audit_once()
    {
        var (db, svc) = BuildInMemory();
        var p1 = await SeedPublishableWritingAsync(db, svc, "W1", "w1");
        await svc.SubmitForReviewAsync(p1.Id, "admin-1", default);

        var result = await svc.BulkAsync("reject", [p1.Id], "admin-2", "Case notes incomplete", default);

        Assert.Equal(1, result.Succeeded);
        var reload = await db.ContentPapers.AsNoTracking().FirstAsync(p => p.Id == p1.Id);
        Assert.Equal(ContentStatus.Rejected, reload.Status);

        // Only the one summary audit; the per-item ContentPaperRejected was suppressed.
        Assert.Single(await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "ContentPaperBulkAction").ToListAsync());
        Assert.Empty(await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "ContentPaperRejected").ToListAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_approve_publish_fires_writing_scenario_projection()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var ctx = new LearnerDbContext(options);
        var projection = new WritingTaskProjectionService(
            ctx, NullLogger<WritingTaskProjectionService>.Instance);
        var service = new ContentPaperService(ctx, projection);

        var paper = await SeedPublishableWritingAsync(ctx, service, "W1", "w1");
        await service.SubmitForReviewAsync(paper.Id, "admin-1", default);

        var result = await service.BulkAsync("approve-publish", [paper.Id], "admin-2", null, default);

        Assert.Equal(1, result.Succeeded);
        var reload = await ctx.ContentPapers.AsNoTracking().FirstAsync(p => p.Id == paper.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);

        var scenario = await ctx.WritingScenarios.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SourceContentPaperId == paper.Id);
        Assert.NotNull(scenario);
        Assert.Equal("published", scenario!.Status);
    }

    [Fact]
    public async Task Bulk_rolls_back_whole_batch_on_fatal_mid_batch_error()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        // Throws a fatal (non-validation) error on the 2nd SaveChanges so the
        // first item's mutation must be rolled back with the transaction.
        var interceptor = new ThrowOnNthSaveInterceptor(throwOnCall: 2);
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        using (var seed = new LearnerDbContext(options))
        {
            seed.Database.EnsureCreated();
        }

        await using var ctx = new LearnerDbContext(options);
        var svc = new ContentPaperService(ctx);
        var p1 = await SeedDraftListeningAsync(ctx, svc, "L1");
        var p2 = await SeedDraftListeningAsync(ctx, svc, "L2");

        interceptor.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() =>
            svc.BulkAsync("archive", [p1.Id, p2.Id], "admin-9", null, default));
        interceptor.Armed = false;

        // Neither paper should be archived — the transaction rolled back.
        using var verify = new LearnerDbContext(
            new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(connection).Options);
        var statuses = await verify.ContentPapers.AsNoTracking()
            .Where(p => p.Id == p1.Id || p.Id == p2.Id).Select(p => p.Status).ToListAsync();
        Assert.All(statuses, s => Assert.Equal(ContentStatus.Draft, s));
        Assert.Empty(await verify.AuditEvents.AsNoTracking()
            .Where(a => a.Action == "ContentPaperBulkAction").ToListAsync());
    }

    [Fact]
    public async Task Bulk_delete_removes_archived_paper_and_its_authoring_children()
    {
        var (db, svc) = BuildInMemory();
        var paper = await SeedDraftListeningAsync(db, svc, "L1");
        await svc.ArchiveAsync(paper.Id, "admin-1", default);
        db.ReadingExtractionDrafts.Add(new ReadingExtractionDraft { Id = "draft-1", PaperId = paper.Id, CreatedByAdminId = "admin-1" });
        await db.SaveChangesAsync();

        var result = await svc.BulkAsync("delete", [paper.Id], "admin-9", null, default);

        Assert.Equal(1, result.Succeeded);
        Assert.False(await db.ContentPapers.AsNoTracking().AnyAsync(p => p.Id == paper.Id));
        Assert.False(await db.ReadingExtractionDrafts.AsNoTracking().AnyAsync(d => d.PaperId == paper.Id));
        var audit = await db.AuditEvents.AsNoTracking()
            .SingleAsync(a => a.Action == "ContentPaperBulkAction");
        Assert.Contains("action=delete", audit.Details);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_delete_is_blocked_for_non_archived_papers()
    {
        var (db, svc) = BuildInMemory();
        var paper = await SeedDraftListeningAsync(db, svc, "L1"); // Draft, not Archived

        var result = await svc.BulkAsync("delete", [paper.Id], "admin-9", null, default);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.True(await db.ContentPapers.AsNoTracking().AnyAsync(p => p.Id == paper.Id));
        Assert.Contains(result.Errors, e => e.Contains("archived", StringComparison.OrdinalIgnoreCase));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_delete_is_blocked_when_paper_has_learner_attempts()
    {
        var (db, svc) = BuildInMemory();
        var paper = await SeedDraftListeningAsync(db, svc, "L1");
        await svc.ArchiveAsync(paper.Id, "admin-1", default);
        db.ListeningAttempts.Add(new ListeningAttempt { Id = "att-1", PaperId = paper.Id, UserId = "user-1" });
        await db.SaveChangesAsync();

        var result = await svc.BulkAsync("delete", [paper.Id], "admin-9", null, default);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.True(await db.ContentPapers.AsNoTracking().AnyAsync(p => p.Id == paper.Id));
        Assert.Contains(result.Errors, e => e.Contains("learner attempts", StringComparison.OrdinalIgnoreCase));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_force_delete_purges_archived_paper_with_listening_attempts_and_children()
    {
        var (db, svc) = BuildInMemory();
        var paper = await SeedDraftListeningAsync(db, svc, "L1");
        await svc.ArchiveAsync(paper.Id, "admin-1", default);

        db.ListeningAttempts.Add(new ListeningAttempt { Id = "att-1", PaperId = paper.Id, UserId = "user-1" });
        db.ListeningAnswers.Add(new ListeningAnswer { Id = "ans-1", ListeningAttemptId = "att-1", ListeningQuestionId = "q-1" });
        db.ListeningAttemptNotes.Add(new ListeningAttemptNote { Id = "note-1", ListeningAttemptId = "att-1", ListeningExtractId = "ex-1", Text = "note" });
        db.ListeningExpertFeedbacks.Add(new ListeningExpertFeedback { Id = "fb-1", AttemptId = "att-1" });
        await db.SaveChangesAsync();

        var result = await svc.BulkAsync("force-delete", [paper.Id], "admin-9", null, default);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.False(await db.ContentPapers.AsNoTracking().AnyAsync(p => p.Id == paper.Id));
        Assert.False(await db.ListeningAttempts.AsNoTracking().AnyAsync(a => a.PaperId == paper.Id));
        Assert.False(await db.ListeningAnswers.AsNoTracking().AnyAsync(x => x.ListeningAttemptId == "att-1"));
        Assert.False(await db.ListeningAttemptNotes.AsNoTracking().AnyAsync(x => x.ListeningAttemptId == "att-1"));
        Assert.False(await db.ListeningExpertFeedbacks.AsNoTracking().AnyAsync(x => x.AttemptId == "att-1"));

        var audit = await db.AuditEvents.AsNoTracking()
            .SingleAsync(a => a.Action == "ContentPaperBulkAction");
        Assert.Contains("action=force-delete", audit.Details);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_force_delete_purges_archived_paper_with_reading_attempts_and_children()
    {
        var (db, svc) = BuildInMemory();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "reading", "R1", null, null, true, null, 60, null, null, 0, null, Prov),
            "admin-1", default);
        await svc.ArchiveAsync(paper.Id, "admin-1", default);

        db.ReadingAttempts.Add(new ReadingAttempt { Id = "ratt-1", PaperId = paper.Id, UserId = "user-1" });
        db.ReadingAnswers.Add(new ReadingAnswer { Id = "rans-1", ReadingAttemptId = "ratt-1", ReadingQuestionId = "q-1" });
        db.ReadingAttemptFeedbacks.Add(new ReadingAttemptFeedback { Id = "rfb-1", ReadingAttemptId = "ratt-1", AuthorUserId = "expert-1" });
        db.ReadingAnswerRevisions.Add(new ReadingAnswerRevision { Id = "rrev-1", ReadingAttemptId = "ratt-1", ReadingQuestionId = "q-1" });
        await db.SaveChangesAsync();

        var result = await svc.BulkAsync("force-delete", [paper.Id], "admin-9", null, default);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.False(await db.ContentPapers.AsNoTracking().AnyAsync(p => p.Id == paper.Id));
        Assert.False(await db.ReadingAttempts.AsNoTracking().AnyAsync(a => a.PaperId == paper.Id));
        Assert.False(await db.ReadingAnswers.AsNoTracking().AnyAsync(x => x.ReadingAttemptId == "ratt-1"));
        Assert.False(await db.ReadingAttemptFeedbacks.AsNoTracking().AnyAsync(x => x.ReadingAttemptId == "ratt-1"));
        Assert.False(await db.ReadingAnswerRevisions.AsNoTracking().AnyAsync(x => x.ReadingAttemptId == "ratt-1"));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_force_delete_purges_reading_attempts_on_relational_db_respecting_fk_order()
    {
        // Relational (SQLite) so real FK constraints are enforced — the InMemory
        // provider ignores them. ReadingAnswer has a RESTRICT FK to ReadingQuestion,
        // and the question is dropped via the ReadingParts cascade, so the answer
        // must be deleted first. This is the exact ordering the force path guards.
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .Options;
        using (var seed = new LearnerDbContext(options))
        {
            seed.Database.EnsureCreated();
        }

        await using var ctx = new LearnerDbContext(options);
        var svc = new ContentPaperService(ctx);
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "reading", "R1", null, null, true, null, 60, null, null, 0, null, Prov),
            "admin-1", default);
        await svc.ArchiveAsync(paper.Id, "admin-1", default);

        ctx.ReadingParts.Add(new ReadingPart { Id = "part-1", PaperId = paper.Id });
        ctx.ReadingQuestions.Add(new ReadingQuestion { Id = "q-1", ReadingPartId = "part-1", Stem = "stem" });
        ctx.ReadingAttempts.Add(new ReadingAttempt { Id = "ratt-1", PaperId = paper.Id, UserId = "user-1" });
        ctx.ReadingAnswers.Add(new ReadingAnswer { Id = "ans-1", ReadingAttemptId = "ratt-1", ReadingQuestionId = "q-1" });
        await ctx.SaveChangesAsync();

        var result = await svc.BulkAsync("force-delete", [paper.Id], "admin-9", null, default);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);

        using var verify = new LearnerDbContext(
            new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(connection).Options);
        Assert.False(await verify.ContentPapers.AsNoTracking().AnyAsync(p => p.Id == paper.Id));
        Assert.False(await verify.ReadingAttempts.AsNoTracking().AnyAsync(a => a.PaperId == paper.Id));
        Assert.False(await verify.ReadingAnswers.AsNoTracking().AnyAsync(x => x.ReadingAttemptId == "ratt-1"));
        Assert.False(await verify.ReadingParts.AsNoTracking().AnyAsync(x => x.PaperId == paper.Id));
        Assert.False(await verify.ReadingQuestions.AsNoTracking().AnyAsync(x => x.ReadingPartId == "part-1"));
    }

    [Fact]
    public async Task Bulk_force_delete_purges_paper_used_in_a_mock_bundle_and_its_section_attempts()
    {
        var (db, svc) = BuildInMemory();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "reading", "R1", null, null, true, null, 60, null, null, 0, null, Prov),
            "admin-1", default);
        await svc.ArchiveAsync(paper.Id, "admin-1", default);

        db.MockBundles.Add(new MockBundle { Id = "mb-1", Title = "Full Mock", Slug = "full-mock" });
        db.MockBundleSections.Add(new MockBundleSection { Id = "mbs-1", MockBundleId = "mb-1", SubtestCode = "reading", ContentPaperId = paper.Id });
        db.MockAttempts.Add(new MockAttempt { Id = "matt-1", UserId = "user-1" });
        db.MockSectionAttempts.Add(new MockSectionAttempt { Id = "msa-1", MockAttemptId = "matt-1", MockBundleSectionId = "mbs-1", SubtestCode = "reading", ContentPaperId = paper.Id, LaunchRoute = "/r" });
        db.MockProctoringEvents.Add(new MockProctoringEvent { Id = "pe-1", MockAttemptId = "matt-1", MockSectionAttemptId = "msa-1", Kind = "focus_lost" });
        await db.SaveChangesAsync();

        var result = await svc.BulkAsync("force-delete", [paper.Id], "admin-9", null, default);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.False(await db.ContentPapers.AsNoTracking().AnyAsync(p => p.Id == paper.Id));
        Assert.False(await db.MockBundleSections.AsNoTracking().AnyAsync(s => s.ContentPaperId == paper.Id));
        Assert.False(await db.MockSectionAttempts.AsNoTracking().AnyAsync(a => a.MockBundleSectionId == "mbs-1"));
        // The proctoring event survives (it belongs to the still-existing MockAttempt)
        // but its link to the purged section attempt is cleared.
        Assert.Null((await db.MockProctoringEvents.AsNoTracking().SingleAsync(e => e.Id == "pe-1")).MockSectionAttemptId);
        // The mock bundle itself is NOT deleted — only its section pointing at the paper.
        Assert.True(await db.MockBundles.AsNoTracking().AnyAsync(b => b.Id == "mb-1"));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_plain_delete_is_blocked_with_clear_reason_when_paper_used_in_a_mock_bundle()
    {
        var (db, svc) = BuildInMemory();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "reading", "R1", null, null, true, null, 60, null, null, 0, null, Prov),
            "admin-1", default);
        await svc.ArchiveAsync(paper.Id, "admin-1", default);

        db.MockBundles.Add(new MockBundle { Id = "mb-1", Title = "Full Mock", Slug = "full-mock" });
        db.MockBundleSections.Add(new MockBundleSection { Id = "mbs-1", MockBundleId = "mb-1", SubtestCode = "reading", ContentPaperId = paper.Id });
        await db.SaveChangesAsync();

        var result = await svc.BulkAsync("delete", [paper.Id], "admin-9", null, default);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.True(await db.ContentPapers.AsNoTracking().AnyAsync(p => p.Id == paper.Id));
        Assert.Contains(result.Errors, e => e.Contains("mock bundle", StringComparison.OrdinalIgnoreCase));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Bulk_force_delete_still_requires_archived_status()
    {
        var (db, svc) = BuildInMemory();
        var paper = await SeedDraftListeningAsync(db, svc, "L1"); // Draft, not Archived
        db.ListeningAttempts.Add(new ListeningAttempt { Id = "att-1", PaperId = paper.Id, UserId = "user-1" });
        await db.SaveChangesAsync();

        var result = await svc.BulkAsync("force-delete", [paper.Id], "admin-9", null, default);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.True(await db.ContentPapers.AsNoTracking().AnyAsync(p => p.Id == paper.Id));
        Assert.True(await db.ListeningAttempts.AsNoTracking().AnyAsync(a => a.PaperId == paper.Id));
        Assert.Contains(result.Errors, e => e.Contains("archived", StringComparison.OrdinalIgnoreCase));
        await db.DisposeAsync();
    }

    /// <summary>Test interceptor that throws on the Nth SaveChanges once armed,
    /// simulating a fatal datastore error mid-batch.</summary>
    private sealed class ThrowOnNthSaveInterceptor(int throwOnCall) : SaveChangesInterceptor
    {
        private int _calls;
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && ++_calls == throwOnCall)
                throw new InvalidProgramException("Simulated fatal datastore failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
