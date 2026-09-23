using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-15 — Technical Evidence Annex (methodology; the live register itself is the dynamic Evidence Annex export).</summary>
internal static class Doc15EvidenceAnnex
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-15",
        Title: "Technical Evidence Annex",
        Description: "Screenshots, signed declarations, test runs, architecture snapshots, release proof, sample reports and the evidence register.",
        SortOrder: 15,
        Sections:
        [
            new DocumentationSectionBlock(
                "Purpose and scope",
                "This annex is the register of every EV-[MODULE]-[NUMBER] evidence citation used anywhere in " +
                "this pack. Every material claim in DOC-01 through DOC-14 is required to resolve to an entry " +
                "here; an entry with no citing claim, or a claim with no entry, is a defect in the pack, not a " +
                "stylistic gap. The register is generated live from the Admin Documentation Center's evidence " +
                "database rather than hand-copied into a static document, so it can never drift out of sync with " +
                "the specialist reports it supports."),
            new DocumentationSectionBlock(
                "Evidence ID convention and types",
                "Each id follows EV-[MODULE-TAG]-[3-digit number], for example EV-AI-001 or EV-WRITE-014, and is " +
                "unique across the whole pack. Ten evidence types are recognised, matching this pack's own " +
                "requirements: Code (repository file/module reference), Deployment (environment/CI run/image " +
                "tag), Testing (suite name, run id, result), Architecture (diagram, schema, service inventory), " +
                "Product UI (dated screenshot or recording frame), AI Model (provider/model id, configuration, " +
                "benchmark), Data/Knowledge (corpus manifest, rule count, provenance), Security (scan/report, " +
                "access-control test), Reliability (latency/error-rate/uptime/load test) and Signed Declaration " +
                "(a named engineer's or the founder's dated statement)."),
            new DocumentationSectionBlock(
                "How to read the register",
                "The Admin Documentation Center's Evidence Register view lists every entry with its module, " +
                "type, description and source reference, and supports search by feature, model, date, provider, " +
                "test, profession or evidence id (see DOC-01 §Admin Documentation Center for the product " +
                "surface). The \"Download Evidence Annex\" action on the dashboard exports the current register " +
                "as its own PDF, independent of the Master pack, so a reviewer can audit the sourcing without " +
                "reading the full narrative reports."),
            new DocumentationSectionBlock(
                "External/Immigration sanitisation",
                "An evidence entry may be marked internal-only when its source reference would otherwise expose " +
                "a private repository path, an internal configuration key, or another detail that is not " +
                "appropriate for an external reviewer. The External/Immigration export mode drops those entries " +
                "(and the sections that cite them) entirely rather than redacting them in place, so the exported " +
                "PDF never contains a partially-blacked-out claim.",
                IsInternalOnly: false),
            new DocumentationSectionBlock(
                "Known gap",
                "Product-UI evidence (dated screenshots and screen-recording frames) has not yet been captured " +
                "and attached for this initial pass — the schema and upload path support it (an evidence item may " +
                "carry an attached file via the platform's existing media-storage pipeline), but capturing the " +
                "actual screenshots is a short manual pass the owner or technical lead should complete before an " +
                "external filing, using the Evidence Register's attach action against the relevant EV-*-PRODUCTUI " +
                "style entries once the team decides which screens to capture."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-ANNEX-001", DocumentationEvidenceType.Architecture,
                "Evidence register schema: DocumentationEvidenceItem (EvidenceId, ModuleId, EvidenceType, Description, SourceReference, IsInternalOnly, optional attached MediaAsset).",
                "OET Project Web App/backend/src/OetLearner.Api/Domain/DocumentationCenterEntities.cs"),
            new DocumentationEvidenceSeed("EV-ANNEX-002", DocumentationEvidenceType.Code,
                "Evidence Annex PDF export endpoint, generated live from the evidence table rather than hand-maintained.",
                "OET Project Web App/backend/src/OetLearner.Api/Endpoints/DocumentationCenterAdminEndpoints.cs, GET /v1/admin/documentation-center/evidence/pdf"),
        ]);
}
