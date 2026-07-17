using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Endpoints;

public static class WritingOcrEndpoints
{
    private const long MaxOcrRequestBytes = 30L * 1024 * 1024;

    public static IEndpointRouteBuilder MapWritingOcrEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/ocr")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPost("/upload", async (
            HttpRequest req,
            HttpContext http,
            IWritingOcrService service,
            CancellationToken ct) =>
        {
            if (!req.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart/form-data required" });
            }

            var form = await req.ReadFormAsync(ct);
            var files = form.Files
                .Where(f => f.Length > 0 && string.Equals(f.Name, "images", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
                return Results.BadRequest(new { error = "no images provided" });
            }

            var submissionIdRaw = form["submissionId"].ToString();
            if (!string.IsNullOrWhiteSpace(submissionIdRaw) && !Guid.TryParse(submissionIdRaw, out _))
            {
                return Results.BadRequest(new { error = "invalid submissionId" });
            }
            Guid? submissionId = Guid.TryParse(submissionIdRaw, out var sid) ? sid : null;

            var job = await service.QueueOcrJobAsync(http.WritingV2UserId(), files, submissionId, ct);
            return Results.Accepted($"/v1/writing/ocr/jobs/{job.Id}", job);
        })
        .RequireRateLimiting("writing-ocr-free")
        .WithName("UploadWritingOcr")
        .WithMetadata(new RequestSizeLimitAttribute(MaxOcrRequestBytes), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxOcrRequestBytes })
        .DisableAntiforgery();

        group.MapGet("/jobs/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingOcrService service,
            CancellationToken ct) =>
        {
            var job = await service.GetJobAsync(http.WritingV2UserId(), id, ct);
            return job is null ? Results.NotFound() : Results.Ok(job);
        })
        .WithName("GetWritingOcrJob");

        return app;
    }
}
