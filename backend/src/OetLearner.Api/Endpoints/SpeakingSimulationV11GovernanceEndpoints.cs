using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin governance surface for the released Speaking simulation v1.1
/// contract. Creating a release or approval never makes it live; an explicit
/// approval action is required, and the learner assessor remains fail-closed
/// until every owner gate is satisfied.
/// </summary>
public static class SpeakingSimulationV11GovernanceEndpoints
{
    private static readonly HashSet<string> FlagKeys = new(StringComparer.Ordinal)
    {
        "calibration_approval",
        "retention_approval",
        "graph_approval",
        "profession_pack_approval",
        "audio_assessment_approval",
        "silence_prompt_approval",
    };

    private static readonly HashSet<string> NumericKeys = new(StringComparer.Ordinal)
    {
        "concurrency_budget",
        "cost_ceiling",
        "latency_sla_ms",
        "retention_days",
        "stt_cost_per_minute",
        "tts_cost_per_1000_characters",
        "silence_prompt_threshold_ms",
    };

    public static IEndpointRouteBuilder MapSpeakingSimulationV11GovernanceEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/speaking/simulation-v1.1")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser")
            .WithTags("Admin Speaking simulation v1.1 governance");

        admin.MapGet("/status", async (
            string? professionId,
            LearnerDbContext db,
            SpeakingSimulationV11ReleaseGate gate,
            CancellationToken ct) =>
        {
            var scope = string.IsNullOrWhiteSpace(professionId)
                ? "medicine"
                : professionId.Trim().ToLowerInvariant();
            var release = await gate.EvaluateAsync(scope, ct);
            var spec = await db.SpeakingSimulationV11SpecReleases.AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt).Take(20).ToListAsync(ct);
            var rubric = await db.SpeakingSimulationV11RubricReleases.AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt).Take(20).ToListAsync(ct);
            var approvals = await db.SpeakingSimulationV11OwnerApprovals.AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt).Take(200).ToListAsync(ct);

            return Results.Ok(new
            {
                professionId = scope,
                gate,
                specReleases = spec.Select(ProjectSpec),
                rubricReleases = rubric.Select(ProjectRubric),
                approvals = approvals.Select(ProjectApproval),
                rubricCriteria = release.RubricCriteria
                    ?? SpeakingSimulationV11Contracts.RubricCriteria.Criteria,
            });
        });

        admin.MapPost("/spec-releases", async (
            HttpContext http,
            SpeakingSimulationV11SpecReleaseRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            if (!string.Equals(request.SpecVersion?.Trim(), SpeakingSimulationV11Contracts.SpecVersion,
                    StringComparison.Ordinal))
                return Results.BadRequest(new { error = "speaking_v11_spec_version_must_match_contract" });
            if (string.IsNullOrWhiteSpace(request.ReleaseVersion))
                return Results.BadRequest(new { error = "speaking_v11_release_version_required" });

            var version = request.ReleaseVersion.Trim();
            if (await db.SpeakingSimulationV11SpecReleases.AnyAsync(
                    x => x.SpecVersion == SpeakingSimulationV11Contracts.SpecVersion
                        && x.ReleaseVersion == version, ct))
                return Results.Conflict(new { error = "speaking_v11_spec_release_exists" });

            var now = DateTimeOffset.UtcNow;
            var row = new SpeakingSimulationV11SpecRelease
            {
                Id = $"spv11_spec_{Guid.NewGuid():N}",
                SpecVersion = SpeakingSimulationV11Contracts.SpecVersion,
                ReleaseVersion = version,
                Status = SpeakingSimulationV11ReleaseStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.SpeakingSimulationV11SpecReleases.Add(row);
            AddAudit(db, http, "speaking_v11.spec_release.created", row.Id,
                $"spec={row.SpecVersion}; release={row.ReleaseVersion}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/admin/speaking/simulation-v1.1/spec-releases/{row.Id}",
                ProjectSpec(row));
        }).WithAdminWrite("AdminContentWrite");

        admin.MapPost("/spec-releases/{id}/approve", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var row = await db.SpeakingSimulationV11SpecReleases.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            if (row.Status != SpeakingSimulationV11ReleaseStatus.Draft)
                return Results.Conflict(new { error = "speaking_v11_spec_release_not_draft" });
            if (!string.Equals(row.SpecVersion, SpeakingSimulationV11Contracts.SpecVersion, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "speaking_v11_spec_version_invalid" });
            var now = DateTimeOffset.UtcNow;
            var peers = await db.SpeakingSimulationV11SpecReleases
                .Where(x => x.Id != id && x.Status == SpeakingSimulationV11ReleaseStatus.Approved)
                .ToListAsync(ct);
            foreach (var peer in peers)
            {
                peer.Status = SpeakingSimulationV11ReleaseStatus.Retired;
                peer.UpdatedAt = now;
            }
            row.Status = SpeakingSimulationV11ReleaseStatus.Approved;
            row.UpdatedAt = now;
            AddAudit(db, http, "speaking_v11.spec_release.approved", row.Id,
                $"spec={row.SpecVersion}; release={row.ReleaseVersion}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectSpec(row));
        }).WithAdminWrite("AdminContentPublish");

        admin.MapPost("/rubric-releases", async (
            HttpContext http,
            SpeakingSimulationV11RubricReleaseRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            if (!string.Equals(request.RubricVersion?.Trim(), SpeakingSimulationV11Contracts.RubricVersion,
                    StringComparison.Ordinal)
                || !string.Equals(request.CalibrationVersion?.Trim(), SpeakingSimulationV11Contracts.CalibrationVersion,
                    StringComparison.Ordinal)
                || !SpeakingSimulationV11Contracts.IsValidRubric(request.Criteria))
                return Results.BadRequest(new { error = "speaking_v11_rubric_definition_invalid" });
            if (request.Criteria.Any(c => c.EnabledRuleIds.Any(IsRule55)))
                return Results.BadRequest(new { error = "speaking_v11_rule55_not_in_scope" });

            var version = request.RubricVersion.Trim();
            if (await db.SpeakingSimulationV11RubricReleases.AnyAsync(
                    x => x.RubricVersion == version, ct))
                return Results.Conflict(new { error = "speaking_v11_rubric_release_exists" });

            var now = DateTimeOffset.UtcNow;
            var row = new SpeakingSimulationV11RubricRelease
            {
                Id = $"spv11_rubric_{Guid.NewGuid():N}",
                RubricVersion = version,
                CalibrationVersion = request.CalibrationVersion.Trim(),
                CriteriaJson = JsonSerializer.Serialize(request.Criteria),
                Status = SpeakingSimulationV11ReleaseStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.SpeakingSimulationV11RubricReleases.Add(row);
            AddAudit(db, http, "speaking_v11.rubric_release.created", row.Id,
                $"rubric={row.RubricVersion}; calibration={row.CalibrationVersion}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/admin/speaking/simulation-v1.1/rubric-releases/{row.Id}",
                ProjectRubric(row));
        }).WithAdminWrite("AdminContentWrite");

        admin.MapPost("/rubric-releases/{id}/approve", async (
            string id,
            HttpContext http,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var row = await db.SpeakingSimulationV11RubricReleases.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            if (row.Status != SpeakingSimulationV11ReleaseStatus.Draft)
                return Results.Conflict(new { error = "speaking_v11_rubric_release_not_draft" });
            if (!string.Equals(row.RubricVersion, SpeakingSimulationV11Contracts.RubricVersion, StringComparison.Ordinal)
                || !string.Equals(row.CalibrationVersion, SpeakingSimulationV11Contracts.CalibrationVersion, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "speaking_v11_rubric_version_invalid" });
            SpeakingSimulationV11RubricCriterion[]? criteria;
            try
            {
                criteria = JsonSerializer.Deserialize<SpeakingSimulationV11RubricCriterion[]>(row.CriteriaJson);
            }
            catch (JsonException)
            {
                criteria = null;
            }
            if (!SpeakingSimulationV11Contracts.IsValidRubric(criteria)
                || criteria!.Any(c => c.EnabledRuleIds.Any(IsRule55)))
                return Results.BadRequest(new { error = "speaking_v11_rubric_definition_invalid" });

            var now = DateTimeOffset.UtcNow;
            var peers = await db.SpeakingSimulationV11RubricReleases
                .Where(x => x.Id != id && x.Status == SpeakingSimulationV11ReleaseStatus.Approved)
                .ToListAsync(ct);
            foreach (var peer in peers)
            {
                peer.Status = SpeakingSimulationV11ReleaseStatus.Retired;
                peer.UpdatedAt = now;
            }
            row.Status = SpeakingSimulationV11ReleaseStatus.Approved;
            row.UpdatedAt = now;
            AddAudit(db, http, "speaking_v11.rubric_release.approved", row.Id,
                $"rubric={row.RubricVersion}; calibration={row.CalibrationVersion}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectRubric(row));
        }).WithAdminWrite("AdminContentPublish");

        admin.MapPost("/approvals", async (
            HttpContext http,
            SpeakingSimulationV11OwnerApprovalRequest request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var key = request.ApprovalKey?.Trim() ?? string.Empty;
            var scope = string.IsNullOrWhiteSpace(request.ScopeKey) ? "global" : request.ScopeKey.Trim();
            if (!FlagKeys.Contains(key) && !NumericKeys.Contains(key))
                return Results.BadRequest(new { error = "speaking_v11_approval_key_invalid" });
            if (string.IsNullOrWhiteSpace(request.SpecVersion) || string.IsNullOrWhiteSpace(request.RubricVersion))
                return Results.BadRequest(new { error = "speaking_v11_approval_versions_required" });
            if (!string.Equals(request.SpecVersion.Trim(), SpeakingSimulationV11Contracts.SpecVersion,
                    StringComparison.Ordinal)
                || !string.Equals(request.RubricVersion.Trim(), SpeakingSimulationV11Contracts.RubricVersion,
                    StringComparison.Ordinal))
                return Results.BadRequest(new { error = "speaking_v11_approval_spec_version_invalid" });
            if (!await db.SpeakingSimulationV11SpecReleases.AnyAsync(
                    x => x.SpecVersion == SpeakingSimulationV11Contracts.SpecVersion
                        && x.Status == SpeakingSimulationV11ReleaseStatus.Approved, ct))
                return Results.BadRequest(new { error = "speaking_v11_approval_spec_release_not_approved" });
            if (!await db.SpeakingSimulationV11RubricReleases.AnyAsync(
                    x => x.RubricVersion == SpeakingSimulationV11Contracts.RubricVersion
                        && x.Status == SpeakingSimulationV11ReleaseStatus.Approved, ct))
                return Results.BadRequest(new { error = "speaking_v11_approval_rubric_release_not_approved" });
            if (NumericKeys.Contains(key) && request.NumericValue is not > 0)
                return Results.BadRequest(new { error = "speaking_v11_numeric_approval_must_be_positive" });
            if (!string.IsNullOrWhiteSpace(request.EvidenceJson) && !IsJsonObjectOrArray(request.EvidenceJson))
                return Results.BadRequest(new { error = "speaking_v11_approval_evidence_invalid_json" });
            if (key == "audio_assessment_approval"
                && !HasApprovedAudioProviderEvidence(request.EvidenceJson))
                return Results.BadRequest(new { error = "speaking_v11_audio_provider_evidence_required" });

            var now = DateTimeOffset.UtcNow;
            var row = new SpeakingSimulationV11OwnerApproval
            {
                Id = $"spv11_approval_{Guid.NewGuid():N}",
                ApprovalKey = key,
                ScopeKey = scope,
                SpecVersion = request.SpecVersion.Trim(),
                RubricVersion = request.RubricVersion.Trim(),
                Status = SpeakingSimulationV11ApprovalStatus.Pending,
                NumericValue = request.NumericValue,
                EvidenceJson = request.EvidenceJson,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.SpeakingSimulationV11OwnerApprovals.Add(row);
            AddAudit(db, http, "speaking_v11.owner_approval.created", row.Id,
                $"key={key}; scope={scope}; status=pending");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/v1/admin/speaking/simulation-v1.1/approvals/{row.Id}",
                ProjectApproval(row));
        }).WithAdminWrite("AdminContentWrite");

        admin.MapPost("/approvals/{id}/approve", async (
            string id,
            HttpContext http,
            SpeakingSimulationV11ApprovalActionRequest? request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var row = await db.SpeakingSimulationV11OwnerApprovals.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            if (row.Status != SpeakingSimulationV11ApprovalStatus.Pending)
                return Results.Conflict(new { error = "speaking_v11_approval_not_pending" });
            if (!string.Equals(row.SpecVersion, SpeakingSimulationV11Contracts.SpecVersion, StringComparison.Ordinal)
                || !string.Equals(row.RubricVersion, SpeakingSimulationV11Contracts.RubricVersion, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "speaking_v11_approval_versions_invalid" });
            if (row.NumericValue is not > 0 && NumericKeys.Contains(row.ApprovalKey))
                return Results.BadRequest(new { error = "speaking_v11_numeric_approval_must_be_positive" });
            if (row.ApprovalKey == "audio_assessment_approval"
                && !HasApprovedAudioProviderEvidence(row.EvidenceJson))
                return Results.BadRequest(new { error = "speaking_v11_audio_provider_evidence_required" });
            var now = DateTimeOffset.UtcNow;
            row.Status = SpeakingSimulationV11ApprovalStatus.Approved;
            row.ApprovedByUserId = ActorId(http);
            row.ApprovedAt = now;
            row.UpdatedAt = now;
            AddAudit(db, http, "speaking_v11.owner_approval.approved", row.Id,
                $"key={row.ApprovalKey}; scope={row.ScopeKey}; note={request?.Note}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectApproval(row));
        }).WithAdminWrite("AdminContentPublish");

        admin.MapPost("/approvals/{id}/reject", async (
            string id,
            HttpContext http,
            SpeakingSimulationV11ApprovalActionRequest? request,
            LearnerDbContext db,
            CancellationToken ct) =>
        {
            var row = await db.SpeakingSimulationV11OwnerApprovals.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            if (row.Status != SpeakingSimulationV11ApprovalStatus.Pending)
                return Results.Conflict(new { error = "speaking_v11_approval_not_pending" });
            if (!string.Equals(row.SpecVersion, SpeakingSimulationV11Contracts.SpecVersion, StringComparison.Ordinal)
                || !string.Equals(row.RubricVersion, SpeakingSimulationV11Contracts.RubricVersion, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "speaking_v11_approval_versions_invalid" });
            row.Status = SpeakingSimulationV11ApprovalStatus.Rejected;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAudit(db, http, "speaking_v11.owner_approval.rejected", row.Id,
                $"key={row.ApprovalKey}; scope={row.ScopeKey}; note={request?.Note}");
            await db.SaveChangesAsync(ct);
            return Results.Ok(ProjectApproval(row));
        }).WithAdminWrite("AdminContentPublish");

        return app;
    }

    private static bool IsRule55(string value)
        => string.Equals(value, "R55", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "rule55", StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonObjectOrArray(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasApprovedAudioProviderEvidence(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var property = document.RootElement.TryGetProperty("provider", out var provider)
                ? provider
                : document.RootElement.TryGetProperty("approvedProvider", out var approvedProvider)
                    ? approvedProvider
                    : default;
            return property.ValueKind == JsonValueKind.String
                && string.Equals(property.GetString()?.Trim(), "azure-phoneme", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static object ProjectSpec(SpeakingSimulationV11SpecRelease row) => new
    {
        row.Id,
        row.SpecVersion,
        row.ReleaseVersion,
        status = row.Status.ToString(),
        row.CreatedAt,
        row.UpdatedAt,
    };

    private static object ProjectRubric(SpeakingSimulationV11RubricRelease row) => new
    {
        row.Id,
        row.RubricVersion,
        row.CalibrationVersion,
        status = row.Status.ToString(),
        criteria = row.CriteriaJson,
        row.CreatedAt,
        row.UpdatedAt,
    };

    private static object ProjectApproval(SpeakingSimulationV11OwnerApproval row) => new
    {
        row.Id,
        row.ApprovalKey,
        row.ScopeKey,
        row.SpecVersion,
        row.RubricVersion,
        status = row.Status.ToString(),
        row.NumericValue,
        row.EvidenceJson,
        row.ApprovedByUserId,
        row.ApprovedAt,
        row.CreatedAt,
        row.UpdatedAt,
    };

    private static string ActorId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

    private static void AddAudit(
        LearnerDbContext db,
        HttpContext http,
        string action,
        string resourceId,
        string details)
    {
        var actorId = ActorId(http);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = http.User.Identity?.Name ?? actorId,
            Action = action,
            ResourceType = "SpeakingSimulationV11Governance",
            ResourceId = resourceId,
            Details = details,
        });
    }
}
