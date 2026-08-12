using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin-only governance API for the Listening/Reading v1.1 score tables,
/// marking policies, and author-approved rationale evidence. Candidate APIs
/// never use these projections because they contain answer-key material.
/// </summary>
public static class AssessmentGovernanceEndpoints
{
    public static IEndpointRouteBuilder MapAssessmentGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/assessment-governance")
            .RequireAuthorization("AdminAssessmentGovernanceRead")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/score-tables", async (
            string? assessment,
            string? scopeKey,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var query = db.AssessmentScoreConversionTables
                .AsNoTracking()
                .Include(x => x.Rows)
                .OrderByDescending(x => x.CreatedAt)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(assessment))
                query = query.Where(x => x.Assessment == assessment.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(scopeKey))
                query = query.Where(x => x.ScopeKey == scopeKey.Trim().ToLowerInvariant());

            var rows = await query.Take(100).ToListAsync(ct);
            return Results.Ok(rows.Select(ProjectTable));
        });

        admin.MapPost("/score-tables", async (
            HttpContext http,
            AssessmentScoreConversionTableRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var assessment = AssessmentScoreTableValidator.NormalizeAssessment(request.Assessment);
            var scopeKey = AssessmentScoreTableValidator.NormalizeScope(request.ScopeKey);
            var requestRows = request.Rows ?? Array.Empty<AssessmentScoreConversionTableRowRequest>();
            var validation = AssessmentScoreTableValidator.Validate(
                assessment,
                requestRows.Select(row => new AssessmentScoreTableRowInput(
                    row.RawScore,
                    row.ConvertedScore,
                    row.Grade,
                    row.Passed)).ToArray());
            if (!validation.IsValid)
                return Results.BadRequest(new { error = validation.ErrorCode });
            if (string.IsNullOrWhiteSpace(request.VersionKey))
                return Results.BadRequest(new { error = "score_table_version_required" });

            var exists = await db.AssessmentScoreConversionTables.AnyAsync(x =>
                x.Assessment == assessment
                && x.ScopeKey == scopeKey
                && x.VersionKey == request.VersionKey.Trim(), ct);
            if (exists)
                return Results.Conflict(new { error = "score_table_version_exists" });

            var actorId = ActorId(http);
            var now = DateTimeOffset.UtcNow;
            var table = new AssessmentScoreConversionTable
            {
                Id = $"lr-score-{Guid.NewGuid():N}",
                Assessment = assessment,
                ScopeKey = scopeKey,
                VersionKey = request.VersionKey.Trim(),
                Status = AssessmentGovernanceStatus.Draft,
                EffectiveFrom = request.EffectiveFrom ?? now,
                CreatedByUserId = actorId,
                CreatedAt = now,
                UpdatedAt = now,
                Rows = requestRows.Select(row => new AssessmentScoreConversionRow
                {
                    Id = $"lr-score-row-{Guid.NewGuid():N}",
                    RawScore = row.RawScore,
                    ConvertedScore = row.ConvertedScore,
                    Grade = string.IsNullOrWhiteSpace(row.Grade) ? null : row.Grade.Trim(),
                    Passed = row.Passed,
                }).ToList(),
            };
            db.AssessmentScoreConversionTables.Add(table);
            AddAudit(db, http, "assessment.score_table.created", table.Id,
                $"assessment={assessment} scope={scopeKey} version={table.VersionKey}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/admin/assessment-governance/score-tables/{table.Id}", ProjectTable(table));
        }).WithAdminWrite("AdminAssessmentGovernanceWrite");

        admin.MapPost("/score-tables/{id}/effective", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var table = await db.AssessmentScoreConversionTables
                .Include(x => x.Rows)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            if (table is null) return Results.NotFound();
            if (table.HasBeenUsed)
                return Results.Conflict(new { error = "score_table_used_version_is_immutable" });

            var validation = AssessmentScoreTableValidator.Validate(
                table.Assessment,
                table.Rows.Select(row => new AssessmentScoreTableRowInput(
                    row.RawScore, row.ConvertedScore, row.Grade, row.Passed)).ToArray());
            if (!validation.IsValid)
                return Results.BadRequest(new { error = validation.ErrorCode });

            var actorId = ActorId(http);
            var now = DateTimeOffset.UtcNow;
            var peers = await db.AssessmentScoreConversionTables
                .Where(x => x.Id != table.Id
                    && x.Assessment == table.Assessment
                    && x.ScopeKey == table.ScopeKey
                    && x.Status == AssessmentGovernanceStatus.Effective)
                .ToListAsync(ct);
            foreach (var peer in peers)
            {
                peer.Status = peer.HasBeenUsed
                    ? AssessmentGovernanceStatus.Locked
                    : AssessmentGovernanceStatus.Retired;
                peer.LockedAt ??= peer.HasBeenUsed ? now : null;
                peer.UpdatedAt = now;
            }

            table.Status = AssessmentGovernanceStatus.Effective;
            table.ApprovedAt = now;
            table.ApprovedByUserId = actorId;
            table.EffectiveFrom = table.EffectiveFrom == default ? now : table.EffectiveFrom;
            table.UpdatedAt = now;
            AddAudit(db, http, "assessment.score_table.effective", table.Id,
                $"assessment={table.Assessment} scope={table.ScopeKey} version={table.VersionKey}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectTable(table));
        }).WithAdminWrite("AdminAssessmentGovernanceApprove");

        admin.MapGet("/marking-policies", async (
            string? assessment,
            string? scopeKey,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var query = db.AssessmentMarkingPolicyVersions.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(assessment))
                query = query.Where(x => x.Assessment == assessment.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(scopeKey))
                query = query.Where(x => x.ScopeKey == scopeKey.Trim().ToLowerInvariant());
            var rows = await query.OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
            return Results.Ok(rows.Select(ProjectPolicy));
        });

        admin.MapPost("/marking-policies", async (
            HttpContext http,
            AssessmentMarkingPolicyRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var assessment = AssessmentScoreTableValidator.NormalizeAssessment(request.Assessment);
            if (!AssessmentScoreTableValidator.IsSupportedAssessment(assessment))
                return Results.BadRequest(new { error = "assessment_unsupported" });
            if (string.IsNullOrWhiteSpace(request.VersionKey))
                return Results.BadRequest(new { error = "marking_policy_version_required" });

            try { _ = AssessmentMarkingPolicyDocument.Parse(request.PolicyJson); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }

            var scopeKey = AssessmentScoreTableValidator.NormalizeScope(request.ScopeKey);
            var exists = await db.AssessmentMarkingPolicyVersions.AnyAsync(x =>
                x.Assessment == assessment
                && x.ScopeKey == scopeKey
                && x.VersionKey == request.VersionKey.Trim(), ct);
            if (exists) return Results.Conflict(new { error = "marking_policy_version_exists" });

            var actorId = ActorId(http);
            var now = DateTimeOffset.UtcNow;
            var policy = new AssessmentMarkingPolicyVersion
            {
                Id = $"lr-policy-{Guid.NewGuid():N}",
                Assessment = assessment,
                ScopeKey = scopeKey,
                VersionKey = request.VersionKey.Trim(),
                PolicyJson = request.PolicyJson,
                EffectiveFrom = request.EffectiveFrom ?? now,
                Status = AssessmentGovernanceStatus.Draft,
                CreatedByUserId = actorId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AssessmentMarkingPolicyVersions.Add(policy);
            AddAudit(db, http, "assessment.marking_policy.created", policy.Id,
                $"assessment={assessment} scope={scopeKey} version={policy.VersionKey}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/admin/assessment-governance/marking-policies/{policy.Id}", ProjectPolicy(policy));
        }).WithAdminWrite("AdminAssessmentGovernanceWrite");

        admin.MapPost("/marking-policies/{id}/effective", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var policy = await db.AssessmentMarkingPolicyVersions.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (policy is null) return Results.NotFound();
            if (policy.HasBeenUsed)
                return Results.Conflict(new { error = "marking_policy_used_version_is_immutable" });

            var actorId = ActorId(http);
            var now = DateTimeOffset.UtcNow;
            var peers = await db.AssessmentMarkingPolicyVersions
                .Where(x => x.Id != policy.Id
                    && x.Assessment == policy.Assessment
                    && x.ScopeKey == policy.ScopeKey
                    && x.Status == AssessmentGovernanceStatus.Effective)
                .ToListAsync(ct);
            foreach (var peer in peers)
            {
                peer.Status = peer.HasBeenUsed
                    ? AssessmentGovernanceStatus.Locked
                    : AssessmentGovernanceStatus.Retired;
                peer.UpdatedAt = now;
            }

            policy.Status = AssessmentGovernanceStatus.Effective;
            policy.ApprovedAt = now;
            policy.ApprovedByUserId = actorId;
            policy.EffectiveFrom = policy.EffectiveFrom == default ? now : policy.EffectiveFrom;
            policy.UpdatedAt = now;
            AddAudit(db, http, "assessment.marking_policy.effective", policy.Id,
                $"assessment={policy.Assessment} scope={policy.ScopeKey} version={policy.VersionKey}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectPolicy(policy));
        }).WithAdminWrite("AdminAssessmentGovernanceApprove");

        admin.MapGet("/rationales", async (
            string? assessment,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var query = db.AssessmentRationales.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(assessment))
                query = query.Where(x => x.Assessment == assessment.Trim().ToLowerInvariant());
            var rows = await query.OrderByDescending(x => x.UpdatedAt).Take(200).ToListAsync(ct);
            return Results.Ok(rows.Select(x => new
            {
                x.Id,
                x.Assessment,
                x.QuestionRevisionId,
                x.SourceSentence,
                x.RationaleText,
                x.EvidenceCount,
                status = x.Status.ToString(),
                x.CreatedByUserId,
                x.ApprovedByUserId,
                x.CreatedAt,
                x.UpdatedAt,
            }));
        });

        admin.MapPost("/rationales", async (
            HttpContext http,
            AssessmentRationaleRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var assessment = AssessmentScoreTableValidator.NormalizeAssessment(request.Assessment);
            if (!AssessmentScoreTableValidator.IsSupportedAssessment(assessment))
                return Results.BadRequest(new { error = "assessment_unsupported" });
            if (string.IsNullOrWhiteSpace(request.QuestionRevisionId)
                || string.IsNullOrWhiteSpace(request.SourceSentence)
                || string.IsNullOrWhiteSpace(request.RationaleText)
                || request.EvidenceCount <= 0)
                return Results.BadRequest(new { error = "rationale_requires_source_text_and_evidence" });
            if (request.SourceSentence.Length > 4096 || request.RationaleText.Length > 4096)
                return Results.BadRequest(new { error = "rationale_text_too_long" });

            var revisionId = request.QuestionRevisionId.Trim();
            if (await db.AssessmentRationales.AnyAsync(x =>
                x.Assessment == assessment && x.QuestionRevisionId == revisionId, ct))
                return Results.Conflict(new { error = "rationale_revision_exists" });

            var actorId = ActorId(http);
            var now = DateTimeOffset.UtcNow;
            var rationale = new AssessmentRationale
            {
                Id = $"lr-rationale-{Guid.NewGuid():N}",
                Assessment = assessment,
                QuestionRevisionId = revisionId,
                SourceSentence = request.SourceSentence.Trim(),
                RationaleText = request.RationaleText.Trim(),
                EvidenceCount = request.EvidenceCount,
                Status = AssessmentGovernanceStatus.Draft,
                CreatedByUserId = actorId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AssessmentRationales.Add(rationale);
            AddAudit(db, http, "assessment.rationale.created", rationale.Id,
                $"assessment={assessment}; questionRevisionId={revisionId}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/admin/assessment-governance/rationales/{rationale.Id}",
                new { rationale.Id, rationale.Assessment, rationale.QuestionRevisionId, status = rationale.Status.ToString() });
        }).WithAdminWrite("AdminAssessmentGovernanceWrite");

        admin.MapPost("/rationales/{id}/effective", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var rationale = await db.AssessmentRationales.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (rationale is null) return Results.NotFound();
            if (rationale.EvidenceCount <= 0
                || string.IsNullOrWhiteSpace(rationale.SourceSentence)
                || string.IsNullOrWhiteSpace(rationale.RationaleText))
                return Results.BadRequest(new { error = "rationale_incomplete" });
            rationale.Status = AssessmentGovernanceStatus.Effective;
            rationale.ApprovedByUserId = ActorId(http);
            rationale.UpdatedAt = DateTimeOffset.UtcNow;
            AddAudit(db, http, "assessment.rationale.effective", rationale.Id,
                $"assessment={rationale.Assessment}; questionRevisionId={rationale.QuestionRevisionId}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { rationale.Id, status = rationale.Status.ToString(), rationale.ApprovedByUserId });
        }).WithAdminWrite("AdminAssessmentGovernanceApprove");

        admin.MapGet("/re-mark-jobs", async (
            string? assessment,
            AssessmentGovernanceStatus? status,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var query = db.AssessmentReMarkJobs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(assessment))
                query = query.Where(x => x.Assessment == assessment.Trim().ToLowerInvariant());
            if (status is AssessmentGovernanceStatus requestedStatus)
                query = query.Where(x => x.Status == requestedStatus);
            var jobs = await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
            return Results.Ok(jobs.Select(x => new
            {
                x.Id,
                x.Assessment,
                x.AttemptId,
                x.QuestionRevisionId,
                x.Reason,
                status = x.Status.ToString(),
                x.RequestedByUserId,
                x.ApprovedByUserId,
                x.CreatedAt,
                x.ApprovedAt,
                x.CompletedAt,
                affectedAttemptIds = x.AffectedAttemptIdsJson,
            }));
        });

        admin.MapPost("/re-mark-jobs", async (
            HttpContext http,
            AssessmentReMarkJobRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var assessment = AssessmentScoreTableValidator.NormalizeAssessment(request.Assessment);
            if (!AssessmentScoreTableValidator.IsSupportedAssessment(assessment))
                return Results.BadRequest(new { error = "assessment_unsupported" });
            if (string.IsNullOrWhiteSpace(request.AttemptId)
                || string.IsNullOrWhiteSpace(request.QuestionRevisionId)
                || string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "remark_job_fields_required" });
            if (!IsValidKeySnapshot(request.OriginalKeySnapshotJson)
                || !IsValidKeySnapshot(request.NewKeySnapshotJson))
                return Results.BadRequest(new { error = "remark_key_snapshot_invalid_json" });

            var attemptId = request.AttemptId.Trim();
            var paperId = assessment == "reading"
                ? await db.ReadingAttempts
                    .Where(x => x.Id == attemptId && x.Status == ReadingAttemptStatus.Submitted)
                    .Select(x => x.PaperId)
                    .SingleOrDefaultAsync(ct)
                : await db.ListeningAttempts
                    .Where(x => x.Id == attemptId && x.Status == ListeningAttemptStatus.Submitted)
                    .Select(x => x.PaperId)
                    .SingleOrDefaultAsync(ct);
            if (paperId is null)
                return Results.NotFound(new { error = "submitted_attempt_not_found" });

            var questionRevisionId = request.QuestionRevisionId.Trim();
            var currentKey = assessment == "reading"
                ? await (
                    from question in db.ReadingQuestions
                    join part in db.ReadingParts on question.ReadingPartId equals part.Id
                    where question.Id == questionRevisionId && part.PaperId == paperId
                    select new CurrentKeySnapshot(question.CorrectAnswerJson, question.AcceptedSynonymsJson))
                    .SingleOrDefaultAsync(ct)
                : await db.ListeningQuestions
                    .Where(question => question.Id == questionRevisionId && question.PaperId == paperId)
                    .Select(question => new CurrentKeySnapshot(question.CorrectAnswerJson, question.AcceptedSynonymsJson))
                    .SingleOrDefaultAsync(ct);
            if (currentKey is null)
                return Results.NotFound(new { error = "remark_question_revision_not_found" });

            if (!TryCanonicalizeKeySnapshot(
                    request.OriginalKeySnapshotJson,
                    currentKey,
                    requireMatch: true,
                    out var originalKeySnapshot))
            {
                return Results.Conflict(new { error = "remark_original_key_snapshot_mismatch" });
            }
            if (!TryCanonicalizeKeySnapshot(
                    request.NewKeySnapshotJson,
                    currentKey,
                    requireMatch: false,
                    out var newKeySnapshot))
            {
                return Results.BadRequest(new { error = "remark_new_key_snapshot_invalid" });
            }

            var active = await db.AssessmentReMarkJobs.AnyAsync(x =>
                x.Assessment == assessment
                && x.AttemptId == attemptId
                && x.QuestionRevisionId == questionRevisionId
                && x.Status != AssessmentGovernanceStatus.Completed
                && x.Status != AssessmentGovernanceStatus.Retired, ct);
            if (active) return Results.Conflict(new { error = "remark_job_already_open" });

            var actorId = ActorId(http);
            var job = new AssessmentReMarkJob
            {
                Id = $"lr-remark-{Guid.NewGuid():N}",
                Assessment = assessment,
                AttemptId = attemptId,
                QuestionRevisionId = questionRevisionId,
                Reason = request.Reason.Trim(),
                OriginalKeySnapshotJson = originalKeySnapshot,
                NewKeySnapshotJson = newKeySnapshot,
                RequestedByUserId = actorId,
                Status = AssessmentGovernanceStatus.InReview,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.AssessmentReMarkJobs.Add(job);
            AddAudit(db, http, "assessment.remark.requested", job.Id,
                $"assessment={assessment}; attemptId={attemptId}; questionRevisionId={job.QuestionRevisionId}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/admin/assessment-governance/re-mark-jobs/{job.Id}",
                new { job.Id, status = job.Status.ToString() });
        }).WithAdminWrite("AdminAssessmentGovernanceWrite");

        admin.MapPost("/re-mark-jobs/{id}/approve", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var job = await db.AssessmentReMarkJobs.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (job is null) return Results.NotFound();
            if (job.Status == AssessmentGovernanceStatus.Completed)
                return Results.Ok(new { job.Id, status = job.Status.ToString() });
            if (job.Status != AssessmentGovernanceStatus.InReview)
                return Results.Conflict(new { error = "remark_job_not_in_review" });
            job.Status = AssessmentGovernanceStatus.Approved;
            job.ApprovedByUserId = ActorId(http);
            job.ApprovedAt = DateTimeOffset.UtcNow;
            AddAudit(db, http, "assessment.remark.approved", job.Id,
                $"assessment={job.Assessment}; attemptId={job.AttemptId}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { job.Id, status = job.Status.ToString(), job.ApprovedByUserId });
        }).WithAdminWrite("AdminAssessmentGovernanceApprove");

        admin.MapPost("/re-mark-jobs/{id}/execute", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            ReadingGradingService readingGrader,
            ListeningGradingService listeningGrader,
            CancellationToken ct) =>
        {
            var job = await db.AssessmentReMarkJobs.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (job is null) return Results.NotFound();
            if (job.Status == AssessmentGovernanceStatus.Completed)
                return Results.Ok(new { job.Id, status = job.Status.ToString(), affectedAttemptIds = job.AffectedAttemptIdsJson });
            if (job.Status != AssessmentGovernanceStatus.Approved)
                return Results.Conflict(new { error = "remark_job_not_approved" });

            object originalResult;
            object result;
            if (job.Assessment == "reading")
            {
                var original = await db.ReadingAttempts.AsNoTracking()
                    .Where(x => x.Id == job.AttemptId)
                    .Select(x => new
                    {
                        x.RawScore,
                        x.MaxRawScore,
                        x.ScaledScore,
                        x.ScoreConversionGrade,
                        x.ScoreConversionTableVersionKey,
                    })
                    .SingleAsync(ct);
                originalResult = original;
                var graded = await readingGrader.RegradeSubmittedAsync(
                        job.AttemptId,
                        job.QuestionRevisionId,
                        job.NewKeySnapshotJson,
                        ct)
                    ?? throw new InvalidOperationException("submitted_attempt_not_found");
                result = new { graded.RawScore, graded.MaxRawScore, graded.ScaledScore, graded.GradeLetter };
            }
            else
            {
                var original = await db.ListeningAttempts.AsNoTracking()
                    .Where(x => x.Id == job.AttemptId)
                    .Select(x => new
                    {
                        x.RawScore,
                        x.MaxRawScore,
                        x.ScaledScore,
                        x.ScoreConversionGrade,
                        x.ScoreConversionTableVersionKey,
                    })
                    .SingleAsync(ct);
                originalResult = original;
                var graded = await listeningGrader.RegradeWithKeyAsync(
                    job.AttemptId,
                    job.QuestionRevisionId,
                    job.NewKeySnapshotJson,
                    ct);
                result = new { graded.RawScore, graded.MaxRawScore, graded.ScaledScore, grade = graded.ScoreConversionGrade };
            }

            job.Status = AssessmentGovernanceStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.AffectedAttemptIdsJson = JsonSerializer.Serialize(new
            {
                attemptId = job.AttemptId,
                original = originalResult,
                updated = result,
            });
            AddAudit(db, http, "assessment.remark.completed", job.Id,
                $"assessment={job.Assessment}; attemptId={job.AttemptId}; questionRevisionId={job.QuestionRevisionId}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { job.Id, status = job.Status.ToString(), affectedAttemptIds = job.AffectedAttemptIdsJson, result });
        }).WithAdminWrite("AdminAssessmentGovernanceExecute");
        return app;
    }

    private static bool IsValidKeySnapshot(string? value)
    {
        return TryReadKeySnapshot(value, out _);
    }

    private sealed record CurrentKeySnapshot(string CorrectAnswerJson, string? AcceptedSynonymsJson);

    private sealed record KeySnapshotProjection(
        bool HasCorrectAnswer,
        string? CorrectAnswerJson,
        bool HasAcceptedVariants,
        string? AcceptedSynonymsJson);

    private static bool TryCanonicalizeKeySnapshot(
        string? value,
        CurrentKeySnapshot current,
        bool requireMatch,
        out string canonical)
    {
        canonical = string.Empty;
        if (!TryReadKeySnapshot(value, out var projection)
            || !TryNormalizeJsonText(
                projection.HasCorrectAnswer ? projection.CorrectAnswerJson : current.CorrectAnswerJson,
                rejectNull: true,
                out var correctAnswerJson))
        {
            return false;
        }

        var acceptedVariantsJson = projection.HasAcceptedVariants
            ? projection.AcceptedSynonymsJson
            : current.AcceptedSynonymsJson;
        if (acceptedVariantsJson is not null)
        {
            if (!TryNormalizeStringArrayJson(acceptedVariantsJson, out var normalizedAcceptedVariantsJson))
                return false;
            acceptedVariantsJson = normalizedAcceptedVariantsJson;
        }

        canonical = JsonSerializer.Serialize(new
        {
            correctAnswerJson,
            acceptedSynonymsJson = acceptedVariantsJson,
        });

        if (!requireMatch) return true;

        if (!TryNormalizeJsonText(current.CorrectAnswerJson, rejectNull: true, out var currentAnswerJson))
            return false;
        string? currentAcceptedVariantsJson = null;
        if (current.AcceptedSynonymsJson is not null
            && !TryNormalizeStringArrayJson(current.AcceptedSynonymsJson, out currentAcceptedVariantsJson))
        {
            return false;
        }

        return string.Equals(correctAnswerJson, currentAnswerJson, StringComparison.Ordinal)
            && string.Equals(acceptedVariantsJson, currentAcceptedVariantsJson, StringComparison.Ordinal);
    }

    private static bool TryReadKeySnapshot(string? value, out KeySnapshotProjection projection)
    {
        projection = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            var hasCorrectJson = root.TryGetProperty("correctAnswerJson", out var correctJson);
            var hasCorrect = root.TryGetProperty("correctAnswer", out var correct);
            if (hasCorrectJson && hasCorrect) return false;

            var hasAcceptedJson = root.TryGetProperty("acceptedSynonymsJson", out var acceptedJson);
            var hasAccepted = root.TryGetProperty("acceptedVariants", out var accepted);
            if (hasAcceptedJson && hasAccepted) return false;
            if (!hasCorrectJson && !hasCorrect && !hasAcceptedJson && !hasAccepted) return false;

            string? correctAnswerJson = null;
            if (hasCorrectJson)
            {
                if (correctJson.ValueKind == JsonValueKind.Null) return false;
                correctAnswerJson = correctJson.ValueKind == JsonValueKind.String
                    ? correctJson.GetString()
                    : correctJson.GetRawText();
            }
            else if (hasCorrect)
            {
                if (correct.ValueKind == JsonValueKind.Null) return false;
                correctAnswerJson = correct.GetRawText();
            }

            string? acceptedVariantsJson = null;
            if (hasAcceptedJson)
            {
                acceptedVariantsJson = acceptedJson.ValueKind == JsonValueKind.Null
                    ? null
                    : acceptedJson.ValueKind == JsonValueKind.String
                        ? acceptedJson.GetString()
                        : acceptedJson.GetRawText();
            }
            else if (hasAccepted)
            {
                acceptedVariantsJson = accepted.ValueKind == JsonValueKind.Null
                    ? null
                    : accepted.GetRawText();
            }

            projection = new KeySnapshotProjection(
                HasCorrectAnswer: hasCorrectJson || hasCorrect,
                CorrectAnswerJson: correctAnswerJson,
                HasAcceptedVariants: hasAcceptedJson || hasAccepted,
                AcceptedSynonymsJson: acceptedVariantsJson);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryNormalizeJsonText(
        string? value,
        bool rejectNull,
        out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            if (rejectNull && document.RootElement.ValueKind == JsonValueKind.Null) return false;
            normalized = JsonSerializer.Serialize(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryNormalizeStringArrayJson(
        string value,
        out string normalized)
    {
        normalized = string.Empty;
        if (!TryNormalizeJsonText(value, rejectNull: true, out normalized)) return false;
        try
        {
            using var document = JsonDocument.Parse(normalized);
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.EnumerateArray().Any(item =>
                    item.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(item.GetString())))
            {
                normalized = string.Empty;
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            normalized = string.Empty;
            return false;
        }
    }
    private static string ActorId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

    private static void AddAudit(LearnerDbContext db, HttpContext http, string action, string resourceId, string details)
    {
        var actorId = ActorId(http);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = http.User.Identity?.Name ?? actorId,
            Action = action,
            ResourceType = "AssessmentGovernance",
            ResourceId = resourceId,
            Details = details,
        });
    }

    private static object ProjectTable(AssessmentScoreConversionTable table) => new
    {
        table.Id,
        table.Assessment,
        table.ScopeKey,
        table.VersionKey,
        status = table.Status.ToString(),
        table.EffectiveFrom,
        table.ApprovedAt,
        table.ApprovedByUserId,
        table.LockedAt,
        table.HasBeenUsed,
        rows = table.Rows.OrderBy(x => x.RawScore).Select(row => new
        {
            row.RawScore,
            row.ConvertedScore,
            row.Grade,
            row.Passed,
        }),
    };

    private static object ProjectPolicy(AssessmentMarkingPolicyVersion policy) => new
    {
        policy.Id,
        policy.Assessment,
        policy.ScopeKey,
        policy.VersionKey,
        policy.PolicyJson,
        status = policy.Status.ToString(),
        policy.EffectiveFrom,
        policy.ApprovedAt,
        policy.ApprovedByUserId,
        policy.HasBeenUsed,
    };
}

public sealed record AssessmentScoreConversionTableRequest(
    string Assessment,
    string? ScopeKey,
    string VersionKey,
    DateTimeOffset? EffectiveFrom,
    IReadOnlyList<AssessmentScoreConversionTableRowRequest> Rows);

public sealed record AssessmentScoreConversionTableRowRequest(
    int RawScore,
    int ConvertedScore,
    string? Grade,
    bool? Passed);

public sealed record AssessmentMarkingPolicyRequest(
    string Assessment,
    string? ScopeKey,
    string VersionKey,
    string PolicyJson,
    DateTimeOffset? EffectiveFrom);

public sealed record AssessmentRationaleRequest(
    string Assessment,
    string QuestionRevisionId,
    string SourceSentence,
    string RationaleText,
    int EvidenceCount);

public sealed record AssessmentReMarkJobRequest(
    string Assessment,
    string AttemptId,
    string QuestionRevisionId,
    string Reason,
    string OriginalKeySnapshotJson,
    string NewKeySnapshotJson);
