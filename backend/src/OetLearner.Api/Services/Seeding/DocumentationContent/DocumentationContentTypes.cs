using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>
/// One evidence citation to seed alongside a module. <see cref="EvidenceId"/> must be
/// globally unique across all 15 modules (enforced by a unique DB index) — convention
/// is <c>EV-[MODULE-TAG]-[3-digit number]</c>, e.g. <c>EV-AI-001</c>.
/// </summary>
public sealed record DocumentationEvidenceSeed(
    string EvidenceId,
    DocumentationEvidenceType EvidenceType,
    string Description,
    string SourceReference,
    bool IsInternalOnly = false);

/// <summary>Real, sourced content for one of the 15 specialist reports (or the Master Dossier).</summary>
public sealed record DocumentationModuleSeed(
    string Code,
    string Title,
    string Description,
    int SortOrder,
    IReadOnlyList<DocumentationSectionBlock> Sections,
    IReadOnlyList<DocumentationEvidenceSeed> Evidence);
