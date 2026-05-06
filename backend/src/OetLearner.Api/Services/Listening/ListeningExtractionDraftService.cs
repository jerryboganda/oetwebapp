using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

// `ListeningExtractionDraft` already exists in this namespace as a transient
// AI-result record (ListeningExtractionService.cs). The persisted entity
// lives in OetLearner.Api.Domain — alias it locally to keep call sites
// readable without renaming the entity (which the spec dictates).
using DraftEntity = OetLearner.Api.Domain.ListeningExtractionDraft;
using DraftStatus = OetLearner.Api.Domain.ListeningExtractionDraftStatus;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Gap B7 — Persisted AI extraction proposals (drafts) for Listening papers.
//
// Before this slice the POST .../listening/extract endpoint returned the
// `ListeningExtractionAiResult` inline; admins had no record of past
// proposals and no formal approve/reject workflow. This service ports the
// Reading pattern (`ReadingExtractionService`):
//
//   1. ProposeAsync  — runs the AI gateway (via IListeningExtractionService),
//                      persists a `Pending` draft, returns it.
//   2. GetAsync / ListAsync — read-only views.
//   3. ApproveAsync  — re-uses ListeningAuthoringService.ReplaceStructureAsync
//                      to apply the draft, then marks Approved.
//   4. RejectAsync   — marks Rejected with a required reason.
//
// Mission-critical guardrails:
//   • The AI call itself routes through `IAiGatewayService.BuildGroundedPrompt`
//     inside `GroundedListeningExtractionAi` (untouched by this slice).
//   • Every state change writes an `AuditEvent`.
//   • Approving / rejecting a non-Pending draft throws ApiException.Conflict
//     so the endpoint surfaces 409.
//   • Approve re-uses the canonical replace pathway — same publish-gate
//     validation + audit trail as a manual edit.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningExtractionDraftService
{
    Task<DraftEntity> ProposeAsync(
        string paperId,
        string adminId,
        CancellationToken ct);

    Task<DraftEntity?> GetAsync(string draftId, CancellationToken ct);

    Task<IReadOnlyList<DraftEntity>> ListAsync(
        string paperId,
        DraftStatus? status,
        CancellationToken ct);

    Task<DraftEntity> ApproveAsync(
        string draftId,
        string adminId,
        string? reason,
        CancellationToken ct);

    Task<DraftEntity> RejectAsync(
        string draftId,
        string adminId,
        string reason,
        CancellationToken ct);
}

public sealed class ListeningExtractionDraftService(
    LearnerDbContext db,
    IListeningExtractionService extraction,
    IListeningAuthoringService authoring) : IListeningExtractionDraftService
{
    private static readonly JsonSerializerOptions CamelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<DraftEntity> ProposeAsync(
        string paperId,
        string adminId,
        CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");

        if (!string.Equals(paper.SubtestCode, "listening", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation(
                "listening_paper_wrong_subtest",
                "Listening extraction can only run against listening papers.");
        }

        // Gap W2: honour the singleton ListeningPolicy (kill-switch + per-paper
        // retry quota). When no policy row exists we default to enabled with
        // the cap of 5 to match the entity defaults.
        var policy = await db.Set<ListeningPolicy>().AsNoTracking()
            .FirstOrDefaultAsync(ct);
        if (policy is { AiExtractionEnabled: false })
        {
            throw ApiException.Forbidden(
                "ai_extraction_disabled",
                "Listening AI extraction is currently disabled by policy.");
        }
        var quota = policy?.AiExtractionMaxRetriesPerPaper ?? 5;
        if (quota > 0)
        {
            var existing = await db.Set<DraftEntity>()
                .CountAsync(d => d.PaperId == paperId, ct);
            if (existing >= quota)
            {
                throw ApiException.Conflict(
                    "ai_extraction_quota_reached",
                    $"Maximum {quota} AI extraction drafts per paper have already been recorded.");
            }
        }

        // ProposeStructureAsync funnels through IListeningExtractionAi which
        // (in production) is GroundedListeningExtractionAi → grounded gateway.
        // The AI call itself, prompt grounding, and refusal-on-ungrounded
        // policy all live there — this service is purely persistence.
        var aiDraft = await extraction.ProposeStructureAsync(paperId, ct);
        if (aiDraft.Status != ListeningExtractionStatus.Ready || aiDraft.Questions.Count == 0)
        {
            throw ApiException.Validation(
                "listening_extraction_failed",
                $"Listening AI extraction did not produce an approvable structure: {aiDraft.Message}");
        }

        var now = DateTimeOffset.UtcNow;
        var draft = new DraftEntity
        {
            Id = $"led_{Guid.NewGuid():N}",
            PaperId = paperId,
            Status = DraftStatus.Pending,
            ProposedAt = now,
            ProposedByUserId = adminId,
            IsStub = aiDraft.IsStub,
            StubReason = aiDraft.IsStub ? Truncate(aiDraft.Message, 512) : null,
            Summary = Truncate(aiDraft.Message, 2048),
            ProposedQuestionsJson = JsonSerializer.Serialize(aiDraft.Questions, CamelJson),
            RawAiResponseJson = string.IsNullOrEmpty(aiDraft.RawResponseJson)
                ? null
                : Truncate(aiDraft.RawResponseJson, 65536),
        };
        db.ListeningExtractionDrafts.Add(draft);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = now,
            ActorId = adminId,
            ActorAuthAccountId = adminId,
            ActorName = adminId,
            Action = "listening.extraction.propose",
            ResourceType = "ListeningExtractionDraft",
            ResourceId = draft.Id,
            Details = JsonSerializer.Serialize(new
            {
                paperId,
                draftId = draft.Id,
                isStub = draft.IsStub,
                questionCount = aiDraft.Questions.Count,
            }),
        });

        await db.SaveChangesAsync(ct);
        return draft;
    }

    public Task<DraftEntity?> GetAsync(string draftId, CancellationToken ct)
        => db.ListeningExtractionDrafts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == draftId, ct);

    public async Task<IReadOnlyList<DraftEntity>> ListAsync(
        string paperId,
        DraftStatus? status,
        CancellationToken ct)
    {
        var q = db.ListeningExtractionDrafts.AsNoTracking()
            .Where(d => d.PaperId == paperId);
        if (status.HasValue) q = q.Where(d => d.Status == status.Value);
        return await q.OrderByDescending(d => d.ProposedAt).ToListAsync(ct);
    }

    public async Task<DraftEntity> ApproveAsync(
        string draftId,
        string adminId,
        string? reason,
        CancellationToken ct)
    {
        // Gap W1: wrap the read-validate-mutate sequence in a transaction so
        // ReplaceStructureAsync (which performs its own SaveChangesAsync) and
        // the draft-status flip either both commit or both roll back. The
        // re-load inside the tx coupled with the EF concurrency token on
        // ListeningExtractionDraft (xmin in Postgres) makes a concurrent
        // second Approve race throw DbUpdateConcurrencyException which we
        // surface as a 409.
        // Tests use the EF InMemory provider which doesn't support
        // transactions; fall back to no-op tx in that case (same shape as
        // ListeningBackfillService).
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            var draft = await db.ListeningExtractionDrafts
                .AsTracking()
                .FirstOrDefaultAsync(d => d.Id == draftId, ct)
                ?? throw ApiException.NotFound(
                    "listening_extraction_draft_not_found",
                    $"Listening extraction draft '{draftId}' not found.");

            if (draft.Status != DraftStatus.Pending)
            {
                throw ApiException.Conflict(
                    "listening_extraction_draft_already_decided",
                    $"Draft is already {draft.Status} and cannot be approved.");
            }

            if (draft.IsStub)
            {
                throw ApiException.Validation(
                    "listening_extraction_draft_stub_not_approvable",
                    draft.StubReason ?? "Stub Listening extraction drafts cannot be approved. Re-run extraction after source text is available.");
            }

            IReadOnlyList<ListeningAuthoredQuestion> questions;
            try
            {
                questions = JsonSerializer.Deserialize<List<ListeningAuthoredQuestion>>(
                    draft.ProposedQuestionsJson, CamelJson) ?? [];
            }
            catch (JsonException ex)
            {
                throw ApiException.Validation(
                    "listening_extraction_draft_invalid_payload",
                    $"Draft payload is not deserialisable: {ex.Message}");
            }
            if (questions.Count != ListeningStructureService.CanonicalTotalItems)
            {
                throw ApiException.Validation(
                    "listening_extraction_draft_invalid_shape",
                    $"Draft payload contains {questions.Count} question(s); OET Listening requires exactly {ListeningStructureService.CanonicalTotalItems}.");
            }
                    ValidateCanonicalDraftQuestions(questions);

            // Re-use the canonical replace pathway — same publish-gate
            // validation and structural audit ("ListeningStructureUpdated")
            // get fired. ReplaceStructureAsync calls SaveChangesAsync
            // internally; both writes commit/rollback as one unit via tx.
            await authoring.ReplaceStructureAsync(draft.PaperId, questions, adminId, ct);

            var now = DateTimeOffset.UtcNow;
            draft.Status = DraftStatus.Approved;
            draft.DecidedAt = now;
            draft.DecidedByUserId = adminId;
            draft.DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : Truncate(reason, 512);

            db.AuditEvents.Add(new AuditEvent
            {
                Id = $"audit_{Guid.NewGuid():N}",
                OccurredAt = now,
                ActorId = adminId,
                ActorAuthAccountId = adminId,
                ActorName = adminId,
                Action = "listening.extraction.approve",
                ResourceType = "ListeningExtractionDraft",
                ResourceId = draft.Id,
                Details = JsonSerializer.Serialize(new
                {
                    paperId = draft.PaperId,
                    draftId = draft.Id,
                    questionCount = questions.Count,
                    reason = draft.DecisionReason,
                }),
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw ApiException.Conflict(
                    "draft_concurrent_update",
                    $"Draft '{draftId}' was modified by another admin; reload and retry. ({ex.GetType().Name})");
            }
            if (tx is not null) await tx.CommitAsync(ct);
            return draft;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<DraftEntity> RejectAsync(
        string draftId,
        string adminId,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw ApiException.Validation(
                "listening_extraction_draft_reject_reason_required",
                "A non-empty reason is required to reject an extraction draft.");
        }
        if (reason.Length > 500)
        {
            throw ApiException.Validation(
                "listening_extraction_draft_reject_reason_too_long",
                "Reject reason must be 500 characters or fewer.");
        }

        // Gap W1: same transactional + concurrency-token shape as ApproveAsync.
        // No-op tx for InMemory provider (tests).
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            var draft = await db.ListeningExtractionDrafts
                .AsTracking()
                .FirstOrDefaultAsync(d => d.Id == draftId, ct)
                ?? throw ApiException.NotFound(
                    "listening_extraction_draft_not_found",
                    $"Listening extraction draft '{draftId}' not found.");

            if (draft.Status != DraftStatus.Pending)
            {
                throw ApiException.Conflict(
                    "listening_extraction_draft_already_decided",
                    $"Draft is already {draft.Status} and cannot be rejected.");
            }

            var now = DateTimeOffset.UtcNow;
            draft.Status = DraftStatus.Rejected;
            draft.DecidedAt = now;
            draft.DecidedByUserId = adminId;
            draft.DecisionReason = reason.Trim();

            db.AuditEvents.Add(new AuditEvent
            {
                Id = $"audit_{Guid.NewGuid():N}",
                OccurredAt = now,
                ActorId = adminId,
                ActorAuthAccountId = adminId,
                ActorName = adminId,
                Action = "listening.extraction.reject",
                ResourceType = "ListeningExtractionDraft",
                ResourceId = draft.Id,
                Details = JsonSerializer.Serialize(new
                {
                    paperId = draft.PaperId,
                    draftId = draft.Id,
                    reason = draft.DecisionReason,
                }),
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw ApiException.Conflict(
                    "draft_concurrent_update",
                    $"Draft '{draftId}' was modified by another admin; reload and retry. ({ex.GetType().Name})");
            }
            if (tx is not null) await tx.CommitAsync(ct);
            return draft;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? string.Empty) : s[..max];

    private static void ValidateCanonicalDraftQuestions(IReadOnlyList<ListeningAuthoredQuestion> questions)
    {
        var errors = new List<string>();
        var duplicateNumbers = questions
            .GroupBy(q => q.Number)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateNumbers.Length > 0)
        {
            errors.Add($"duplicate question numbers: {string.Join(", ", duplicateNumbers.Take(10))}");
        }

        var a1 = questions.Count(q => IsPart(q, "A1"));
        var a2 = questions.Count(q => IsPart(q, "A2"));
        var b = questions.Count(q => IsPart(q, "B"));
        var c1 = questions.Count(q => IsPart(q, "C1"));
        var c2 = questions.Count(q => IsPart(q, "C2"));
        if (a1 != 12 || a2 != 12) errors.Add($"Part A split must be A1=12 and A2=12; found A1={a1}, A2={a2}");
        if (b != 6) errors.Add($"Part B must contain exactly 6 items; found {b}");
        if (c1 != 6 || c2 != 6) errors.Add($"Part C split must be C1=6 and C2=6; found C1={c1}, C2={c2}");

        var blankStemCount = questions.Count(q => string.IsNullOrWhiteSpace(q.Stem));
        if (blankStemCount > 0) errors.Add($"{blankStemCount} item(s) have blank stems");

        var blankAnswerCount = questions.Count(q => string.IsNullOrWhiteSpace(q.CorrectAnswer));
        if (blankAnswerCount > 0) errors.Add($"{blankAnswerCount} item(s) have blank correct answers");

        var invalidMcq = questions.Count(q => (IsPart(q, "B") || IsPart(q, "C1") || IsPart(q, "C2"))
            && (!string.Equals(q.Type, "multiple_choice_3", StringComparison.OrdinalIgnoreCase)
                || q.Options is not { Count: 3 }
                || !q.Options.Any(option => string.Equals(option?.Trim(), q.CorrectAnswer?.Trim(), StringComparison.OrdinalIgnoreCase))));
        if (invalidMcq > 0)
        {
            errors.Add($"{invalidMcq} Part B/C item(s) are not 3-option MCQs with the correct answer matching one option");
        }

        if (errors.Count > 0)
        {
            throw ApiException.Validation(
                "listening_extraction_draft_invalid_canonical_shape",
                "Draft is not publish-ready and cannot be approved: " + string.Join("; ", errors));
        }
    }

    private static bool IsPart(ListeningAuthoredQuestion question, string partCode)
        => string.Equals(question.PartCode?.Trim(), partCode, StringComparison.OrdinalIgnoreCase);
}
