using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Admin endpoints for managing <see cref="ExpertUser"/> rows. Today this
/// covers the Phase-4 follow-up gap: backfilling <c>SpecialtiesJson</c> so
/// the auto-assigner's profession-competency filter has signal to work with.
/// Without specialties, every expert is treated as a generalist — functional
/// but defeats spec §12.E intent. This surface lets admins curate per-expert
/// profession lists from the existing admin operations console.
/// </summary>
public static class ExpertAdminEndpoints
{
    public static IEndpointRouteBuilder MapExpertAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/experts")
            .RequireAuthorization("AdminContentRead")
            .RequireRateLimiting("PerUser");

        // List experts with their specialties for the backfill workflow.
        group.MapGet("", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var rows = await db.ExpertUsers
                .AsNoTracking()
                .OrderBy(e => e.DisplayName)
                .Select(e => new
                {
                    e.Id,
                    e.DisplayName,
                    e.Email,
                    e.IsActive,
                    e.SpecialtiesJson,
                })
                .ToListAsync(ct);
            return Results.Ok(rows.Select(r => new
            {
                r.Id,
                r.DisplayName,
                r.Email,
                r.IsActive,
                specialties = ParseSpecialties(r.SpecialtiesJson),
            }));
        });

        // PATCH the specialties array. Body: { specialties: string[] }.
        // Validation:
        //  - Each entry must be a known canonical OET profession id.
        //  - Empty array clears specialties → expert becomes a generalist.
        group.MapPatch("/{id}/specialties", async (
            string id, ExpertSpecialtiesUpdateDto dto, LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var expert = await db.ExpertUsers.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (expert is null) return Results.NotFound(new { error = "expert_not_found" });

            var normalized = NormalizeSpecialties(dto.Specialties).ToList();
            var unknown = normalized.Where(s => !KnownProfessions.Contains(s)).ToList();
            if (unknown.Count > 0)
            {
                return Results.BadRequest(new
                {
                    error = "unknown_profession",
                    unknown,
                    knownProfessions = KnownProfessions.OrderBy(p => p).ToArray(),
                });
            }

            expert.SpecialtiesJson = JsonSerializer.Serialize(normalized);

            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = adminId,
                ActorName = adminId,
                Action = "ExpertSpecialtiesUpdated",
                ResourceType = "ExpertUser",
                ResourceId = expert.Id,
                Details = $"specialties={string.Join(',', normalized)}",
            });

            await db.SaveChangesAsync(ct);
            return Results.Ok(new
            {
                expert.Id,
                expert.DisplayName,
                specialties = normalized,
            });
        })
        .RequireAuthorization("AdminContentWrite")
        .RequireRateLimiting("PerUserWrite");

        return app;
    }

    private static readonly HashSet<string> KnownProfessions = new(StringComparer.OrdinalIgnoreCase)
    {
        "medicine",
        "nursing",
        "dentistry",
        "pharmacy",
        "physiotherapy",
        "veterinary",
        "optometry",
        "radiography",
        "occupational-therapy",
        "speech-pathology",
        "podiatry",
        "dietetics",
        "other-allied-health",
    };

    private static IEnumerable<string> NormalizeSpecialties(IEnumerable<string>? input)
    {
        if (input is null) return Array.Empty<string>();
        return input
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string[] ParseSpecialties(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? string.Empty)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }
}

public sealed record ExpertSpecialtiesUpdateDto(IReadOnlyList<string> Specialties);
