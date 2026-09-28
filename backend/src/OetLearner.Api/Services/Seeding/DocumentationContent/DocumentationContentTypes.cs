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

/// <summary>
/// Real, sourced content for one of the 15 specialist reports (or the Master Dossier).
/// <see cref="Revision"/> is the version number this content is published as. Bump it
/// whenever a module's sections or evidence change: on the next startup
/// <see cref="DocumentationCenterSeeder"/> publishes the new text as that version and
/// marks the older one <c>Superseded</c>. Leaving it unchanged means production keeps
/// serving the text it already has.
/// </summary>
public sealed record DocumentationModuleSeed(
    string Code,
    string Title,
    string Description,
    int SortOrder,
    IReadOnlyList<DocumentationSectionBlock> Sections,
    IReadOnlyList<DocumentationEvidenceSeed> Evidence,
    int Revision = 1);
