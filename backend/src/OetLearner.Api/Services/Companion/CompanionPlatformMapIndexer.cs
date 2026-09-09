using System.Text;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionPlatformMapIndexer
{
    Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct);
}

/// <summary>
/// Publishes the destination catalog as retrievable platform knowledge
/// (Manifest 1.E).
///
/// <para>
/// <see cref="CompanionDestinationRegistry"/> already lets the companion
/// <i>navigate</i>: the model names a destination id and the server builds the
/// link. What it cannot do is <i>answer questions about</i> the product —
/// "what's in the Materials library?", "where does my progress live?", "is
/// there somewhere to practise pronunciation?" — because the catalog is a tool
/// vocabulary, not evidence. A learner asking that got either a generic answer
/// or a tool call that returned a URL with nothing to say about it.
/// </para>
///
/// <para>
/// Same table, two uses. Indexing it here rather than writing a parallel
/// description of the product is the whole point: a destination added to the
/// registry becomes both navigable and explainable in one edit, and the two can
/// never disagree about what the product contains.
/// </para>
///
/// <para>
/// <b>This is not an entitlement bypass.</b> Locked destinations are described
/// here, deliberately — a learner should be able to find out what the Video
/// Library is before buying it. Opening one still goes through
/// <see cref="ICompanionDestinationRegistry.ResolveAsync"/>, which re-checks the
/// module and flag and returns a refusal plus the upgrade path. Knowing a
/// product exists is marketing; reaching its content is entitlement.
/// </para>
/// </summary>
public sealed class CompanionPlatformMapIndexer(
    LearnerDbContext db,
    IEmbeddingService embeddings,
    ILogger<CompanionPlatformMapIndexer> logger) : ICompanionPlatformMapIndexer
{
    internal const string SourceKey = "platform:destination-map";

    /// <summary>
    /// Bumped by hand when the shape of the generated text changes. The catalog
    /// itself is content-hashed per chunk, so adding a destination does not need
    /// a version bump — only a change to how destinations are described does.
    /// </summary>
    internal const string Version = "2026-09-09.1";

    public Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct)
    {
        var warnings = new List<string>();

        return CompanionIndexWriter.WriteAsync(
            db, embeddings, logger, SourceKey, Version,
            source =>
            {
                source.SourceType = "platform_map";
                source.Title = "Platform map — where everything lives";
                source.AuthorityClass = CompanionAuthorityClass.PlatformSupport;
                source.State = CompanionSourceState.Approved;
                source.ExamTypeCode = "OET";
                source.ProfessionId = null;
                source.SubtestCode = null;
                source.IsProprietary = false;
                source.RequiredEntitlementScope = null;
                source.PackageScope = null;
                source.StorageLocator =
                    "backend/src/OetLearner.Api/Services/Companion/CompanionDestinationRegistry.cs";
                source.ApprovedAt ??= DateTimeOffset.UtcNow;
            },
            BuildChunks(), embed, warnings, ct);
    }

    /// <summary>
    /// One chunk per area of the product, plus an index chunk.
    ///
    /// <para>
    /// Grouped by <see cref="CompanionDestinationKind"/> rather than one chunk
    /// per destination, because the questions learners actually ask are
    /// area-shaped ("where do I see how I'm doing?") and thirty-seven one-line
    /// chunks would let a single vague query consume the whole evidence budget
    /// on near-identical fragments.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<CompanionChunkDraft> BuildChunks()
    {
        var live = CompanionDestinationRegistry.CatalogForIndexing
            .Where(d => d.SupersededById is null)
            .ToList();

        var chunks = new List<CompanionChunkDraft>
        {
            new(
                "Platform map — overview",
                "The OET platform with Dr Ahmed Hesham is organised into these areas: " +
                string.Join(", ", live
                    .GroupBy(d => d.Kind)
                    .OrderBy(g => g.Key)
                    .Select(g => $"{Describe(g.Key)} ({g.Count()} places)")) + ".\n" +
                "Some areas depend on the learner's package. If something is not included in their " +
                "access, say what it contains and offer the upgrade route rather than describing a way around it. " +
                "Never write a URL from memory — always resolve the destination through the platform tools, " +
                "which re-check entitlement and build the link."),
        };

        foreach (var group in live.GroupBy(d => d.Kind).OrderBy(g => g.Key))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Describe(group.Key)} — what is here and what each place is for.");
            sb.AppendLine();

            foreach (var destination in group.OrderBy(d => d.Title, StringComparer.Ordinal))
            {
                sb.Append("- ").Append(destination.Title).Append(": ").Append(destination.Description);

                if (destination.SubtestCode is { } subtest)
                {
                    sb.Append(" (").Append(subtest).Append(')');
                }

                // Named so the companion can answer "why can't I see that?"
                // accurately instead of guessing at a paywall.
                if (destination.RequiredModuleKey is { } module)
                {
                    sb.Append(" Requires the ").Append(module).Append(" module in the learner's package.");
                }

                if (destination.RequiredFeatureFlag is not null)
                {
                    sb.Append(" May be switched off platform-wide.");
                }

                sb.AppendLine();
                sb.Append("  Destination id for the open-destination tool: ")
                  .Append(destination.Id)
                  .AppendLine();
            }

            chunks.Add(new CompanionChunkDraft(
                $"Platform map — {Describe(group.Key)}",
                sb.ToString().Trim()));
        }

        return chunks;
    }

    private static string Describe(CompanionDestinationKind kind) => kind switch
    {
        CompanionDestinationKind.Practice => "Practice and exams",
        CompanionDestinationKind.Learning => "Learning and study planning",
        CompanionDestinationKind.Progress => "Progress and results",
        CompanionDestinationKind.Library => "Libraries and materials",
        CompanionDestinationKind.Account => "Account and settings",
        CompanionDestinationKind.Commerce => "Packages, credits and billing",
        CompanionDestinationKind.Support => "Support and escalation",
        _ => kind.ToString(),
    };
}
