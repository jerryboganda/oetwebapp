using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Phase 8 of LISTENING-MODULE-PLAN.md — AI extraction stub.
//
// Mirrors `IReadingExtractionService` for Listening. Given an admin upload
// trio (Question Paper PDF + Audio Script + Answer Key), this service is
// expected to call the AI gateway with `Kind = Listening` to propose a 42-item
// authored structure ready for a human admin to review and publish.
//
// This file is a deliberately minimal scaffold so DI compiles and so a
// dedicated AI worker can land in a follow-up commit. It returns `Pending`
// for every call until a real provider is wired through. All eventual
// invocations MUST go through `IAiGatewayService.BuildGroundedPrompt(...)`
// (see docs/AI-USAGE-POLICY.md) — never construct a raw prompt here.
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
    IReadOnlyList<ListeningAuthoredQuestion> Questions);

public interface IListeningExtractionService
{
    /// <summary>
    /// Propose a 42-item authored Listening structure for the supplied paper.
    /// Implementation will call the grounded AI gateway with the uploaded
    /// Question-Paper / Audio-Script / Answer-Key text, validate the reply
    /// against the canonical 24/6/12 split, and surface a draft for admin
    /// review. Today this returns <see cref="ListeningExtractionStatus.Pending"/>.
    /// </summary>
    Task<ListeningExtractionDraft> ProposeStructureAsync(string paperId, CancellationToken ct);
}

public sealed class ListeningExtractionService : IListeningExtractionService
{
    public Task<ListeningExtractionDraft> ProposeStructureAsync(string paperId, CancellationToken ct)
    {
        // Phase 8 scaffold: real AI extraction lands in a follow-up wiring
        // commit through `IAiGatewayService` with feature code
        // `AiFeatureCodes.AdminListeningDraft` (platform-only) and Kind=Listening.
        var draft = new ListeningExtractionDraft(
            Status: ListeningExtractionStatus.Pending,
            Message: "Listening AI extraction is not yet wired. Use the manual authoring screen to fill in the 42-item map.",
            Questions: []);
        return Task.FromResult(draft);
    }
}
