using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Owner-governed v1.1 authoring and release controls. Candidate APIs do not
/// use these routes; every write requires the existing granular admin policy.
/// </summary>
public static class WritingAssessmentGovernanceEndpoints
{
    public static IEndpointRouteBuilder MapWritingAssessmentGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/writing/assessment-v11")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");

        admin.MapGet("/packs", async (
            string? profession,
            string? letterType,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var query = db.WritingAssessmentPackVersions.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(profession))
                query = query.Where(x => x.Profession == Normalize(profession));
            if (!string.IsNullOrWhiteSpace(letterType))
                query = query.Where(x => x.LetterType == NormalizeLetterType(letterType));

            var packs = await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
            return Results.Ok(packs.Select(ProjectPack));
        }).WithAdminRead("AdminContentRead");

        admin.MapPost("/packs", async (
            HttpContext http,
            WritingAssessmentPackRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var profession = Normalize(request.Profession);
            var letterType = NormalizeLetterType(request.LetterType);
            var versionKey = request.VersionKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(profession)
                || string.IsNullOrWhiteSpace(letterType)
                || string.IsNullOrWhiteSpace(versionKey))
                return Results.BadRequest(new { error = "pack_identity_required" });
            if (!IsJsonObject(request.RulesJson))
                return Results.BadRequest(new { error = "pack_rules_invalid_json" });
            if (await db.WritingAssessmentPackVersions.AnyAsync(x =>
                    x.Profession == profession
                    && x.LetterType == letterType
                    && x.VersionKey == versionKey, ct))
                return Results.Conflict(new { error = "pack_version_exists" });

            var now = DateTimeOffset.UtcNow;
            var pack = new WritingAssessmentPackVersion
            {
                Id = Guid.NewGuid(),
                Profession = profession,
                LetterType = letterType,
                VersionKey = versionKey,
                Status = WritingAssessmentReleaseStatus.Draft,
                CandidateFacing = false,
                RulesJson = request.RulesJson,
                ApprovalEvidenceJson = request.ApprovalEvidenceJson,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.WritingAssessmentPackVersions.Add(pack);
            AddAudit(db, http, "writing.assessment_v11.pack.created", pack.Id.ToString(),
                $"profession={profession}; letterType={letterType}; version={versionKey}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/admin/writing/assessment-v11/packs/{pack.Id}", ProjectPack(pack));
        }).WithAdminWrite("AdminContentWrite");

        admin.MapPost("/packs/{id:guid}/approve", async (
            Guid id,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var pack = await db.WritingAssessmentPackVersions.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (pack is null) return Results.NotFound();
            if (!IsJsonObject(pack.RulesJson))
                return Results.BadRequest(new { error = "pack_rules_invalid_json" });
            if (string.IsNullOrWhiteSpace(pack.ApprovalEvidenceJson)
                || !IsJsonObject(pack.ApprovalEvidenceJson))
                return Results.BadRequest(new { error = "pack_owner_approval_evidence_required" });
            if (pack.Status == WritingAssessmentReleaseStatus.Approved && pack.CandidateFacing)
                return Results.Ok(ProjectPack(pack));

            var now = DateTimeOffset.UtcNow;
            pack.Status = WritingAssessmentReleaseStatus.Approved;
            pack.CandidateFacing = true;
            pack.ApprovedByUserId = ActorId(http);
            pack.ApprovedAt = now;
            pack.UpdatedAt = now;
            AddAudit(db, http, "writing.assessment_v11.pack.approved", pack.Id.ToString(),
                $"profession={pack.Profession}; letterType={pack.LetterType}; version={pack.VersionKey}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectPack(pack));
        }).WithAdminWrite("AdminContentPublish");

        admin.MapGet("/release-gates", async (
            string? modelVersion,
            string? calibrationSetVersion,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var query = db.WritingAssessmentReleaseGates.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(modelVersion))
                query = query.Where(x => x.ModelVersion == modelVersion.Trim());
            if (!string.IsNullOrWhiteSpace(calibrationSetVersion))
                query = query.Where(x => x.CalibrationSetVersion == calibrationSetVersion.Trim());
            var gates = await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
            return Results.Ok(gates.Select(ProjectGate));
        }).WithAdminRead("AdminContentRead");

        admin.MapPost("/release-gates", async (
            HttpContext http,
            WritingAssessmentReleaseGateRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var modelVersion = request.ModelVersion?.Trim() ?? string.Empty;
            var calibrationSetVersion = request.CalibrationSetVersion?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(modelVersion) || string.IsNullOrWhiteSpace(calibrationSetVersion))
                return Results.BadRequest(new { error = "release_gate_identity_required" });
            if (request.OwnerApprovedTolerance is <= 0
                || request.MeanAbsoluteError is < 0
                || request.ContentConcisenessCorrelation is < -1 or > 1
                || request.LanguageCorrelation is < -1 or > 1
                || request.InventedClaimRate is < 0 or > 1
                || request.QualifiedReviewerCount < 0
                || request.HumanRatingsPerBenchmark < 0)
                return Results.BadRequest(new { error = "release_gate_metrics_invalid" });
            if (await db.WritingAssessmentReleaseGates.AnyAsync(x =>
                    x.ModelVersion == modelVersion && x.CalibrationSetVersion == calibrationSetVersion, ct))
                return Results.Conflict(new { error = "release_gate_version_exists" });

            var now = DateTimeOffset.UtcNow;
            var gate = new WritingAssessmentReleaseGate
            {
                Id = Guid.NewGuid(),
                ModelVersion = modelVersion,
                CalibrationSetVersion = calibrationSetVersion,
                Status = WritingAssessmentReleaseStatus.Blocked,
                CandidateNumericScoreEnabled = false,
                MeanAbsoluteError = request.MeanAbsoluteError,
                ContentConcisenessCorrelation = request.ContentConcisenessCorrelation,
                LanguageCorrelation = request.LanguageCorrelation,
                InventedClaimRate = request.InventedClaimRate,
                OwnerApprovedTolerance = request.OwnerApprovedTolerance,
                QualifiedReviewerCount = request.QualifiedReviewerCount,
                HumanRatingsPerBenchmark = request.HumanRatingsPerBenchmark,
                ApprovalEvidenceJson = request.ApprovalEvidenceJson,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.WritingAssessmentReleaseGates.Add(gate);
            AddAudit(db, http, "writing.assessment_v11.release_gate.created", gate.Id.ToString(),
                $"model={modelVersion}; calibration={calibrationSetVersion}; status=blocked");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/admin/writing/assessment-v11/release-gates/{gate.Id}", ProjectGate(gate));
        }).WithAdminWrite("AdminContentWrite");

        admin.MapPost("/release-gates/{id:guid}/approve", async (
            Guid id,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var gate = await db.WritingAssessmentReleaseGates.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (gate is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(gate.ApprovalEvidenceJson)
                || !IsJsonObject(gate.ApprovalEvidenceJson))
                return Results.BadRequest(new { error = "release_gate_owner_approval_evidence_required" });
            if (!WritingCalibrationReleaseService.IsCandidateReleaseAllowed(gate))
                return Results.Conflict(new { error = "release_gate_calibration_requirements_not_met" });

            gate.Status = WritingAssessmentReleaseStatus.Approved;
            gate.CandidateNumericScoreEnabled = true;
            gate.ApprovedByUserId = ActorId(http);
            gate.ApprovedAt = DateTimeOffset.UtcNow;
            gate.UpdatedAt = gate.ApprovedAt.Value;
            AddAudit(db, http, "writing.assessment_v11.release_gate.approved", gate.Id.ToString(),
                $"model={gate.ModelVersion}; calibration={gate.CalibrationSetVersion}; candidate=true");
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectGate(gate));
        }).WithAdminWrite("AdminContentPublish");

        admin.MapGet("/reports/{submissionId:guid}", async (
            Guid submissionId,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var report = await db.WritingAssessmentReportsV11.AsNoTracking()
                .Include(x => x.Facts)
                .Include(x => x.Errors)
                .Include(x => x.Criteria)
                .SingleOrDefaultAsync(x => x.SubmissionId == submissionId, ct);
            if (report is null) return Results.NotFound();
            var modelAnswer = await db.WritingAssessmentModelAnswers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ReportId == report.Id, ct);
            return Results.Ok(new
            {
                report.Id,
                report.SubmissionId,
                status = report.Status.ToString(),
                report.Profession,
                report.LetterType,
                report.RulePackVersion,
                report.ModelVersion,
                report.CalibrationSetVersion,
                report.OriginalLetterHash,
                report.OriginalLetterSnapshot,
                report.TaskSnapshot,
                report.CaseNotesSnapshot,
                report.ClassificationJson,
                report.FeatureRecordJson,
                report.TopPrioritiesJson,
                report.StrengthsJson,
                report.StudyPlanJson,
                report.PurposeScore,
                report.ContentScore,
                report.ConcisenessClarityScore,
                report.GenreStyleScore,
                report.OrganisationLayoutScore,
                report.LanguageScore,
                report.EstimatedPracticeScore,
                report.ScoreRange,
                report.ConfidenceLabel,
                report.ConfidenceRange,
                report.CandidateNumericScoreEnabled,
                report.CandidateReportVisible,
                facts = report.Facts,
                errors = report.Errors,
                criteria = report.Criteria,
                modelAnswer,
            });
        }).WithAdminRead("AdminContentRead");

        return app;
    }

    private static string ActorId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

    // Pack identity uses the same canonical vocabulary as grading preflight
    // (WritingLetterTypeTaxonomy.ToPackLetterType) so packs created with LT-*
    // codes match the tasks that reference them, and vice versa.
    private static string NormalizeLetterType(string? value) =>
        WritingLetterTypeTaxonomy.ToPackLetterType(value);

    private static bool IsJsonObject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AddAudit(LearnerDbContext db, HttpContext http, string action, string resourceId, string details)
        => db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = ActorId(http),
            ActorName = http.User.Identity?.Name ?? ActorId(http),
            Action = action,
            ResourceType = "WritingAssessmentV11",
            ResourceId = resourceId,
            Details = details,
        });

    private static object ProjectPack(WritingAssessmentPackVersion pack) => new
    {
        pack.Id,
        pack.Profession,
        pack.LetterType,
        pack.VersionKey,
        status = pack.Status.ToString(),
        pack.CandidateFacing,
        pack.RulesJson,
        pack.ApprovalEvidenceJson,
        pack.ApprovedByUserId,
        pack.ApprovedAt,
        pack.CreatedAt,
        pack.UpdatedAt,
    };

    private static object ProjectGate(WritingAssessmentReleaseGate gate) => new
    {
        gate.Id,
        gate.ModelVersion,
        gate.CalibrationSetVersion,
        status = gate.Status.ToString(),
        gate.CandidateNumericScoreEnabled,
        gate.MeanAbsoluteError,
        gate.ContentConcisenessCorrelation,
        gate.LanguageCorrelation,
        gate.InventedClaimRate,
        gate.OwnerApprovedTolerance,
        gate.QualifiedReviewerCount,
        gate.HumanRatingsPerBenchmark,
        gate.ApprovalEvidenceJson,
        gate.ApprovedByUserId,
        gate.ApprovedAt,
        gate.CreatedAt,
        gate.UpdatedAt,
    };
}

public sealed record WritingAssessmentPackRequest(
    string Profession,
    string LetterType,
    string VersionKey,
    string RulesJson,
    string? ApprovalEvidenceJson);

public sealed record WritingAssessmentReleaseGateRequest(
    string ModelVersion,
    string CalibrationSetVersion,
    decimal? MeanAbsoluteError,
    decimal? ContentConcisenessCorrelation,
    decimal? LanguageCorrelation,
    decimal? InventedClaimRate,
    decimal? OwnerApprovedTolerance,
    int QualifiedReviewerCount,
    int HumanRatingsPerBenchmark,
    string? ApprovalEvidenceJson);
