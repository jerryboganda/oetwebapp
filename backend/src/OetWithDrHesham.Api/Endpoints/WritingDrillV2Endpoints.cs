using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// V2 drill endpoints. The legacy /v1/writing/drills surface (registered in
/// WritingPathwayEndpoints) returns V1 DTOs and uses a plural attempts route;
/// V2 mounts at /v1/writing/v2/drills to avoid collision while keeping the
/// frontend types stable per lib/writing/api.ts.
/// </summary>
public static class WritingDrillV2Endpoints
{
    public static IEndpointRouteBuilder MapWritingDrillV2Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/v2/drills")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/", async (
            [FromQuery] string? drillType,
            [FromQuery] string? targetSubSkill,
            [FromQuery] string? profession,
            [FromQuery] string? letterType,
            [FromQuery] int? difficulty,
            [FromQuery] bool? dueOnly,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext http,
            IWritingDrillService service,
            CancellationToken ct)
            => Results.Ok(await service.ListDrillsV2Async(
                http.WritingV2UserId(),
                drillType,
                targetSubSkill,
                profession,
                letterType,
                difficulty,
                dueOnly ?? false,
                page ?? 1,
                pageSize ?? 20,
                ct)))
            .WithName("ListWritingDrillsV2");

        // Case-note collection — declared before {id} so the literal isn't
        // swallowed as a Guid.
        group.MapGet("/case-notes", async (
            [FromQuery] string? profession,
            [FromQuery] string? format,
            HttpContext http,
            IWritingCaseNoteDrillService service,
            CancellationToken ct) =>
        {
            var rows = await service.ListAsync(http.WritingV2UserId(), profession, null, ct);
            IReadOnlyList<WritingCaseNoteDrillView> filtered = string.IsNullOrWhiteSpace(format)
                ? rows
                : rows.Where(row => row.Format == format).ToList();
            return Results.Ok(new WritingCaseNoteDrillListResponseV2(
                filtered.Select(row => new WritingCaseNoteDrillResponseV2(
                    row.Id,
                    row.Format,
                    null,
                    row.Profession,
                    row.CaseNotesMarkdown,
                    row.Sentences.Select(sentence => new WritingCaseNoteDrillSentenceResponseV2(sentence.Ordinal, sentence.SentenceText, null)).ToList(),
                    row.Format == "tag-relevance" ? ["essential", "relevant", "omit"] : null,
                    row.Status)).ToList(),
                filtered.Count));
        })
            .WithName("ListWritingCaseNoteDrillsV2");

        group.MapPost("/case-notes/{id:guid}/attempt", async (
            Guid id,
            WritingCaseNoteDrillAttemptRequestV2 request,
            HttpContext http,
            IWritingCaseNoteDrillService service,
            CancellationToken ct) =>
        {
            var drill = await service.GetAsync(http.WritingV2UserId(), id, ct);
            if (drill is null) return Results.NotFound();
            var responses = drill.Sentences.ToDictionary(
                sentence => sentence.Id,
                sentence => request.SelectedIndices.Contains(sentence.Ordinal) ? "relevant" : "omit");
            var result = await service.SubmitAttemptAsync(http.WritingV2UserId(), id, new OetWithDrHesham.Api.Services.Writing.WritingCaseNoteDrillAttemptRequest(responses, null), ct);
            return Results.Ok(new WritingCaseNoteDrillAttemptResultResponseV2(
                id,
                (int)Math.Round(result.ScorePercent),
                drill.Sentences.Select(sentence =>
                {
                    var entry = result.PerSentence.First(item => item.SentenceId == sentence.Id);
                    return new WritingCaseNoteDrillSentenceVerdictResponse(
                        sentence.Ordinal,
                        responses[sentence.Id],
                        entry.CorrectLabel,
                        entry.IsCorrect ? "correct" : "review");
                }).ToList()));
        })
        .RequireRateLimiting("writing-drills")
        .WithName("SubmitWritingCaseNoteDrillAttemptV2");

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingDrillService service,
            CancellationToken ct) =>
        {
            var drill = await service.GetDrillV2Async(http.WritingV2UserId(), id, ct);
            return drill is null ? Results.NotFound() : Results.Ok(drill);
        })
        .WithName("GetWritingDrillV2");

        group.MapPost("/{id:guid}/attempt", async (
            Guid id,
            WritingDrillAttemptRequestV2 request,
            HttpContext http,
            IWritingDrillService service,
            CancellationToken ct) =>
        {
            var result = await service.SubmitDrillAttemptV2Async(http.WritingV2UserId(), id, request, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .RequireRateLimiting("writing-drills")
        .WithName("SubmitWritingDrillAttemptV2");

        return app;
    }
}
