using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Phase 8 of LISTENING-MODULE-PLAN.md — AI extraction seam.
//
// Mirrors `IReadingExtractionService` for Listening. Given an admin upload
// trio (Question Paper PDF + Audio Script + Answer Key), this service is
// expected to call the AI gateway with `Kind = Listening` to propose a 42-item
// authored structure ready for a human admin to review and publish.
//
// Today the default `IListeningExtractionAi` implementation is the
// deterministic `StubListeningExtractionAi` — same shape Reading shipped — so
// the admin UI works end-to-end without AI configured. A real grounded
// implementation lands in a follow-up commit and MUST go through
// `IAiGatewayService.BuildGroundedPrompt(...)` (see docs/AI-USAGE-POLICY.md)
// with feature code `admin.listening_draft` (platform-only). The gateway
// physically refuses ungrounded prompts via `PromptNotGroundedException`.
// ═════════════════════════════════════════════════════════════════════════════

public enum ListeningExtractionStatus
{
    Pending = 0,
    Ready = 1,
    Failed = 2,
}

public sealed record ListeningExtractionDraft(
    ListeningExtractionStatus Status,
    string Message,
    IReadOnlyList<ListeningAuthoredQuestion> Questions,
    bool IsStub = false);

/// <summary>
/// AI seam for the actual Listening structure extraction. The default
/// implementation is the stub below; tests substitute a fake that returns a
/// known authored question list, and a future grounded-gateway impl plugs in
/// here without service changes.
/// </summary>
public interface IListeningExtractionAi
{
    Task<ListeningExtractionAiResult> ExtractAsync(
        string paperId,
        string? mediaAssetId,
        CancellationToken ct);
}

public sealed record ListeningExtractionAiResult(
    IReadOnlyList<ListeningAuthoredQuestion> Questions,
    string? RawResponseJson,
    bool IsStub,
    string? StubReason);

public interface IListeningExtractionService
{
    /// <summary>
    /// Propose a 42-item authored Listening structure for the supplied paper.
    /// Today this delegates to <see cref="IListeningExtractionAi"/>; the
    /// default ai impl returns a deterministic placeholder so admins can
    /// iterate. Replace with a grounded-gateway impl in a follow-up.
    /// </summary>
    Task<ListeningExtractionDraft> ProposeStructureAsync(string paperId, CancellationToken ct);
}

public sealed class ListeningExtractionService(IListeningExtractionAi ai) : IListeningExtractionService
{
    public async Task<ListeningExtractionDraft> ProposeStructureAsync(string paperId, CancellationToken ct)
    {
        try
        {
            var result = await ai.ExtractAsync(paperId, mediaAssetId: null, ct).ConfigureAwait(false);
            return new ListeningExtractionDraft(
                Status: ListeningExtractionStatus.Ready,
                Message: result.IsStub
                    ? (result.StubReason ?? "Stub extraction returned a placeholder 42-item structure.")
                    : "AI extraction returned a 42-item Listening structure.",
                Questions: result.Questions,
                IsStub: result.IsStub);
        }
        catch (Exception ex)
        {
            return new ListeningExtractionDraft(
                Status: ListeningExtractionStatus.Failed,
                Message: ex.Message,
                Questions: [],
                IsStub: false);
        }
    }
}

/// <summary>
/// Default deterministic AI implementation. Returns a canonical 24/6/12
/// placeholder authored list so the admin UI works end-to-end without an AI
/// gateway configured.
/// </summary>
public sealed class StubListeningExtractionAi : IListeningExtractionAi
{
    public Task<ListeningExtractionAiResult> ExtractAsync(
        string paperId,
        string? mediaAssetId,
        CancellationToken ct)
    {
        var items = new List<ListeningAuthoredQuestion>(42);
        for (var n = 1; n <= 12; n++) items.Add(BlankShortAnswer(n, "A1"));
        for (var n = 13; n <= 24; n++) items.Add(BlankShortAnswer(n, "A2"));
        for (var n = 25; n <= 30; n++) items.Add(BlankMcq(n, "B"));
        for (var n = 31; n <= 36; n++) items.Add(BlankMcq(n, "C1"));
        for (var n = 37; n <= 42; n++) items.Add(BlankMcq(n, "C2"));

        return Task.FromResult(new ListeningExtractionAiResult(
            Questions: items,
            RawResponseJson: null,
            IsStub: true,
            StubReason: "AI gateway not configured for Listening extraction; returning deterministic 24/6/12 placeholder."));
    }

    private static ListeningAuthoredQuestion BlankShortAnswer(int number, string partCode) =>
        new(
            Id: $"lq-{number}",
            Number: number,
            PartCode: partCode,
            Type: "short_answer",
            Stem: string.Empty,
            Options: [],
            CorrectAnswer: string.Empty,
            AcceptedAnswers: [],
            Explanation: null,
            SkillTag: null,
            TranscriptExcerpt: null,
            DistractorExplanation: null,
            Points: 1);

    private static ListeningAuthoredQuestion BlankMcq(int number, string partCode) =>
        new(
            Id: $"lq-{number}",
            Number: number,
            PartCode: partCode,
            Type: "multiple_choice_3",
            Stem: string.Empty,
            Options: ["", "", ""],
            CorrectAnswer: string.Empty,
            AcceptedAnswers: [],
            Explanation: null,
            SkillTag: null,
            TranscriptExcerpt: null,
            DistractorExplanation: null,
            Points: 1,
            OptionDistractorWhy: [null, null, null],
            OptionDistractorCategory: [null, null, null]);
}
