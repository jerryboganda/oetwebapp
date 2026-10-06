#if FLEET_HAS_COMPANION_CHUNKER
// Shim for the link-compiled CompanionChunker (protocol 6.2): the real CompanionChunkDraft lives beside the EF-bound
// CompanionIndexWriter in the API, so the agent declares the identical record shape here. Compiled only when the API
// track's CompanionChunker.cs exists (see Fleet.Agent.csproj).
namespace OetLearner.Api.Services.Companion;

public readonly record struct CompanionChunkDraft(
    string Heading,
    string Text,
    int? PageNumber = null,
    int? TimestampSeconds = null);
#endif
