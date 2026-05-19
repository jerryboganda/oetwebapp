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
// The production `IListeningExtractionAi` implementation goes through
// `IAiGatewayService.BuildGroundedPrompt(...)` (see docs/AI-USAGE-POLICY.md)
// with feature code `admin.listening_draft` (platform-only). The gateway
// physically refuses ungrounded prompts via `PromptNotGroundedException`, and
// unavailable providers are reported as failed drafts without invented items.
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
    bool IsStub = false,
    string? RawResponseJson = null);

/// <summary>
/// AI seam for the actual Listening structure extraction. Tests substitute a
/// fake that returns a known authored question list.
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
    /// Delegates to <see cref="IListeningExtractionAi"/> and reports provider
    /// failures as failed drafts instead of generating replacement content.
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
                    ? (result.StubReason ?? "AI extraction returned a non-approvable draft.")
                    : "AI extraction returned a 42-item Listening structure.",
                Questions: result.Questions,
                IsStub: result.IsStub,
                RawResponseJson: result.RawResponseJson);
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

public sealed class StubListeningExtractionAi : IListeningExtractionAi
{
    public Task<ListeningExtractionAiResult> ExtractAsync(
        string paperId,
        string? mediaAssetId,
        CancellationToken ct)
    {
        throw new InvalidOperationException("Listening AI extraction provider is not configured.");
    }
}
