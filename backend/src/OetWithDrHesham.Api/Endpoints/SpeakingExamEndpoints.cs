using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Speaking module rebuild (2026-06-11 spec). HTTP surface for the two-card
/// Speaking exam (Intro → Card A → Card B).
///
/// Routes (all under <c>/v1/speaking/exams</c>, learner-only, owner-checked at
/// the service layer via an IDOR guard that returns NotFound for non-owners):
///   * POST   ""                       create an exam (ai | live_tutor)
///   * GET    /{id}                     current state + server clock + current card
///   * GET    /{id}/clock              authoritative phase clock only
///   * POST   /{id}/finish-intro       intro → prep_a (reveals Card A, debits credit A)
///   * POST   /{id}/start-card         prep → active for the current card
///   * POST   /{id}/cancel             abandon the exam
///   * POST   /{id}/technical-issue    flag a technical issue (never affects scoring)
///   * GET    /{id}/results            per-card + combined results
/// </summary>
public static class SpeakingExamEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingExamEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/v1/speaking/exams")
            .RequireAuthorization("LearnerOnly")
            .WithTags("Speaking exams");

        learner.MapPost("", CreateAsync)
            .WithSummary("Create a two-card Speaking exam (ai or live_tutor).")
            .Produces<SpeakingExamDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status402PaymentRequired)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/from-booking/{bookingId}", FromBookingAsync)
            .WithSummary("Create (or resume) the live-tutor exam for a PrivateSpeaking booking.")
            .Produces<SpeakingExamDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapGet("/{id}", GetAsync)
            .WithSummary("Get the caller's own exam state, server clock, and current candidate card.")
            .Produces<SpeakingExamDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapGet("/{id}/clock", GetClockAsync)
            .WithSummary("Authoritative server-computed exam phase clock.")
            .Produces<SpeakingExamDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapPost("/{id}/finish-intro", FinishIntroAsync)
            .WithSummary("Finish the unscored Intro and reveal Card A (debits 1 AI credit).")
            .Produces<SpeakingExamDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status402PaymentRequired)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/{id}/start-card", StartCardAsync)
            .WithSummary("Start the current card's discussion (prep → active).")
            .Produces<SpeakingExamDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        learner.MapPost("/{id}/cancel", CancelAsync)
            .WithSummary("Abandon the exam.")
            .Produces<SpeakingExamDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapPost("/{id}/technical-issue", TechnicalIssueAsync)
            .WithSummary("Flag a technical issue on the exam (never affects scoring).")
            .Produces<SpeakingExamDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        learner.MapGet("/{id}/results", ResultsAsync)
            .WithSummary("Per-card and combined exam results.")
            .Produces<SpeakingExamResults>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext http, [FromBody] CreateSpeakingExamRequest body, SpeakingExamService exams, CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        return Results.Ok(await exams.CreateExamAsync(userId, body, ct));
    }

    private static async Task<IResult> GetAsync(
        HttpContext http, string id, SpeakingExamService exams, CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        return Results.Ok(await exams.GetExamForLearnerAsync(userId, id, ct));
    }

    private static async Task<IResult> FromBookingAsync(
        HttpContext http, string bookingId, SpeakingExamService exams, CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        return Results.Ok(await exams.CreateExamForBookingAsync(userId, bookingId, ct));
    }

    private static async Task<IResult> GetClockAsync(
        HttpContext http, string id, SpeakingExamService exams, CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        var detail = await exams.GetExamForLearnerAsync(userId, id, ct);
        return Results.Ok(detail.Clock);
    }

    private static async Task<IResult> FinishIntroAsync(
        HttpContext http, string id, SpeakingExamService exams, CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        return Results.Ok(await exams.FinishIntroAsync(userId, id, ct));
    }

    private static async Task<IResult> StartCardAsync(
        HttpContext http, string id, SpeakingExamService exams, CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        return Results.Ok(await exams.StartCardAsync(userId, id, ct));
    }

    private static async Task<IResult> CancelAsync(
        HttpContext http, string id, SpeakingExamService exams, CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        return Results.Ok(await exams.CancelAsync(userId, id, ct));
    }

    private static async Task<IResult> TechnicalIssueAsync(
        HttpContext http, string id, [FromBody] SpeakingTechnicalIssueRequest? body,
        SpeakingExamService exams, CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        return Results.Ok(await exams.ReportTechnicalIssueAsync(userId, id, body?.Note, ct));
    }

    private static async Task<IResult> ResultsAsync(
        HttpContext http, string id, SpeakingExamService exams, CancellationToken ct)
    {
        var userId = ResolveUserId(http);
        return Results.Ok(await exams.GetResultsAsync(userId, id, ct));
    }

    private static string ResolveUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw ApiException.Unauthorized("speaking_exam_unauthenticated",
               "You must be signed in to interact with a Speaking exam.");
}
