using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Gap B7 — coverage for <see cref="ListeningExtractionDraftService"/>.
/// Verifies AI-result persistence, approval re-using the canonical replace
/// pathway, conflict on already-decided drafts, and reject input gating.
/// </summary>
public class ListeningExtractionDraftServiceTests
{
    private static (LearnerDbContext db,
        ListeningExtractionDraftService svc,
        ListeningAuthoringService authoring) Build(bool useStubAi = true)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var authoring = new ListeningAuthoringService(db);
        var extraction = new ListeningExtractionService(useStubAi ? new StubListeningExtractionAi() : new ValidListeningExtractionAi());
        var svc = new ListeningExtractionDraftService(db, extraction, authoring);
        return (db, svc, authoring);
    }

    private static async Task<ContentPaper> SeedListeningPaperAsync(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = Guid.NewGuid().ToString("N"),
            SubtestCode = "listening",
            Title = "Listening Draft Paper",
            Slug = $"listening-{Guid.NewGuid():N}",
            Status = ContentStatus.Draft,
            ExtractedTextJson = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        return paper;
    }

    [Fact]
    public async Task Propose_PersistsPendingDraft_AndReturnsDraftId()
    {
        var (db, svc, _) = Build();
        var paper = await SeedListeningPaperAsync(db);

        var draft = await svc.ProposeAsync(paper.Id, adminId: "admin-1", default);

        Assert.NotNull(draft);
        Assert.False(string.IsNullOrWhiteSpace(draft.Id));
        Assert.Equal(paper.Id, draft.PaperId);
        Assert.Equal(ListeningExtractionDraftStatus.Pending, draft.Status);
        Assert.Equal("admin-1", draft.ProposedByUserId);
        Assert.True(draft.IsStub, "Stub AI is expected in the test harness.");
        Assert.False(string.IsNullOrWhiteSpace(draft.Summary));
        Assert.False(string.IsNullOrWhiteSpace(draft.ProposedQuestionsJson));
        Assert.NotEqual("[]", draft.ProposedQuestionsJson);

        // Audit row exists.
        var audit = await db.AuditEvents.SingleAsync(
            a => a.Action == "listening.extraction.propose");
        Assert.Equal("ListeningExtractionDraft", audit.ResourceType);
        Assert.Equal(draft.Id, audit.ResourceId);

        // Persisted as Pending.
        var persisted = await db.ListeningExtractionDrafts.AsNoTracking()
            .SingleAsync(d => d.Id == draft.Id);
        Assert.Equal(ListeningExtractionDraftStatus.Pending, persisted.Status);
    }

    [Fact]
    public async Task Approve_ChangesStatus_AndReplacesStructure_AndWritesAudit()
    {
        var (db, svc, authoring) = Build(useStubAi: false);
        var paper = await SeedListeningPaperAsync(db);
        var draft = await svc.ProposeAsync(paper.Id, "admin-1", default);

        var result = await svc.ApproveAsync(
            draft.Id, adminId: "admin-2", reason: "looks good", default);

        Assert.Equal(ListeningExtractionDraftStatus.Approved, result.Status);
        Assert.Equal("admin-2", result.DecidedByUserId);
        Assert.NotNull(result.DecidedAt);
        Assert.Equal("looks good", result.DecisionReason);

        // Structure was replaced — paper now carries the 42-item canonical proposal.
        var structure = await authoring.GetStructureAsync(paper.Id, default);
        Assert.Equal(42, structure.Counts.TotalItems);

        // Both the per-draft audit and the structural-update audit fired.
        var draftAudit = await db.AuditEvents
            .SingleAsync(a => a.Action == "listening.extraction.approve");
        Assert.Equal(draft.Id, draftAudit.ResourceId);
        var structureAudit = await db.AuditEvents
            .SingleAsync(a => a.Action == "ListeningStructureUpdated");
        Assert.Equal(paper.Id, structureAudit.ResourceId);
    }

    [Fact]
    public async Task Approve_RejectsAlreadyDecidedDraft_With409()
    {
        var (db, svc, _) = Build(useStubAi: false);
        var paper = await SeedListeningPaperAsync(db);
        var draft = await svc.ProposeAsync(paper.Id, "admin-1", default);
        await svc.ApproveAsync(draft.Id, "admin-2", reason: null, default);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.ApproveAsync(draft.Id, "admin-3", reason: null, default));

        Assert.Equal("listening_extraction_draft_already_decided", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);

        var rejectEx = await Assert.ThrowsAsync<ApiException>(() =>
            svc.RejectAsync(draft.Id, "admin-3", reason: "nope", default));
        Assert.Equal(StatusCodes.Status409Conflict, rejectEx.StatusCode);
    }

    [Fact]
    public async Task Approve_RejectsStubDraft_With400()
    {
        var (db, svc, authoring) = Build();
        var paper = await SeedListeningPaperAsync(db);
        var draft = await svc.ProposeAsync(paper.Id, "admin-1", default);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.ApproveAsync(draft.Id, "admin-2", reason: null, default));

        Assert.Equal("listening_extraction_draft_stub_not_approvable", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        var structure = await authoring.GetStructureAsync(paper.Id, default);
        Assert.Empty(structure.Questions);
    }

    [Fact]
    public async Task Approve_RejectsNonCanonicalNonStubDraft_With400()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var authoring = new ListeningAuthoringService(db);
        var extraction = new ListeningExtractionService(new InvalidShapeListeningExtractionAi());
        var svc = new ListeningExtractionDraftService(db, extraction, authoring);
        var paper = await SeedListeningPaperAsync(db);
        var draft = await svc.ProposeAsync(paper.Id, "admin-1", default);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.ApproveAsync(draft.Id, "admin-2", reason: null, default));

        Assert.Equal("listening_extraction_draft_invalid_canonical_shape", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Empty((await authoring.GetStructureAsync(paper.Id, default)).Questions);
    }


    [Fact]
    public async Task Reject_RequiresReason_AndDoesNotMutateStructure()
    {
        var (db, svc, authoring) = Build();
        var paper = await SeedListeningPaperAsync(db);
        var draft = await svc.ProposeAsync(paper.Id, "admin-1", default);

        // Empty reason → 400.
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.RejectAsync(draft.Id, "admin-2", reason: "  ", default));
        Assert.Equal("listening_extraction_draft_reject_reason_required", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);

        // Over-long reason → 400.
        var tooLong = new string('x', 501);
        var ex2 = await Assert.ThrowsAsync<ApiException>(() =>
            svc.RejectAsync(draft.Id, "admin-2", reason: tooLong, default));
        Assert.Equal("listening_extraction_draft_reject_reason_too_long", ex2.ErrorCode);

        // Valid reason → Rejected, structure untouched.
        var rejected = await svc.RejectAsync(
            draft.Id, "admin-2", reason: "wrong question count", default);
        Assert.Equal(ListeningExtractionDraftStatus.Rejected, rejected.Status);
        Assert.Equal("wrong question count", rejected.DecisionReason);

        var structure = await authoring.GetStructureAsync(paper.Id, default);
        Assert.Empty(structure.Questions);

        var audit = await db.AuditEvents
            .SingleAsync(a => a.Action == "listening.extraction.reject");
        Assert.Equal(draft.Id, audit.ResourceId);
    }

    private sealed class ValidListeningExtractionAi : IListeningExtractionAi
    {
        public Task<ListeningExtractionAiResult> ExtractAsync(string paperId, string? mediaAssetId, CancellationToken ct)
        {
            var items = new List<ListeningAuthoredQuestion>(42);
            for (var n = 1; n <= 12; n++) items.Add(ShortAnswer(n, "A1"));
            for (var n = 13; n <= 24; n++) items.Add(ShortAnswer(n, "A2"));
            for (var n = 25; n <= 30; n++) items.Add(Mcq(n, "B"));
            for (var n = 31; n <= 36; n++) items.Add(Mcq(n, "C1"));
            for (var n = 37; n <= 42; n++) items.Add(Mcq(n, "C2"));
            return Task.FromResult(new ListeningExtractionAiResult(items, "{}", IsStub: false, StubReason: null));
        }

        private static ListeningAuthoredQuestion ShortAnswer(int number, string partCode) => new(
            Id: $"lq-{number}",
            Number: number,
            PartCode: partCode,
            Type: "short_answer",
            Stem: $"Question {number}",
            Options: [],
            CorrectAnswer: $"answer {number}",
            AcceptedAnswers: [],
            Explanation: null,
            SkillTag: null,
            TranscriptExcerpt: null,
            DistractorExplanation: null,
            Points: 1);

        private static ListeningAuthoredQuestion Mcq(int number, string partCode) => new(
            Id: $"lq-{number}",
            Number: number,
            PartCode: partCode,
            Type: "multiple_choice_3",
            Stem: $"Question {number}",
            Options: ["A", "B", "C"],
            CorrectAnswer: "A",
            AcceptedAnswers: [],
            Explanation: null,
            SkillTag: null,
            TranscriptExcerpt: null,
            DistractorExplanation: null,
            Points: 1,
            OptionDistractorWhy: [null, null, null],
            OptionDistractorCategory: [null, null, null]);
    }

    private sealed class InvalidShapeListeningExtractionAi : IListeningExtractionAi
    {
        public Task<ListeningExtractionAiResult> ExtractAsync(string paperId, string? mediaAssetId, CancellationToken ct)
        {
            var items = Enumerable.Range(1, 42)
                .Select(number => new ListeningAuthoredQuestion(
                    Id: $"lq-{number}",
                    Number: number,
                    PartCode: "A1",
                    Type: "short_answer",
                    Stem: $"Question {number}",
                    Options: [],
                    CorrectAnswer: $"answer {number}",
                    AcceptedAnswers: [],
                    Explanation: null,
                    SkillTag: null,
                    TranscriptExcerpt: null,
                    DistractorExplanation: null,
                    Points: 1))
                .ToList();
            return Task.FromResult(new ListeningExtractionAiResult(items, "{}", IsStub: false, StubReason: null));
        }
    }
}
