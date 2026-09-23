using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-09 — AI Source-of-Truth, Rulebook &amp; Knowledge Governance Report.</summary>
internal static class Doc09SourceOfTruth
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-09",
        Title: "AI Source-of-Truth, Rulebook & Knowledge Governance Report",
        Description: "The evidence hierarchy, profession rulebooks, provenance tracking, versioning and conflict-resolution process that governs every rule the AI grader is allowed to apply.",
        SortOrder: 9,
        Sections:
        [
            new DocumentationSectionBlock(
                "A vendored, hash-verified rule registry, not an editable prompt",
                "The platform's grading rules do not live as free text inside a prompt template. They live as a " +
                "vendored, version-controlled registry — `OET_AI_Rules_Master.jsonl`, a 2.3 MB streaming JSON-" +
                "lines file under `docs/canonical-rules/` — sourced from a named external handoff package " +
                "(`OET_DEVELOPER_COMPLETE_HANDOFF_v1.0_2026-08-31/02_AI_SOURCE_OF_TRUTH/`) and originally verified " +
                "by SHA-256 against a companion checksum manifest, `HANDOFF_SHA256SUMS.txt` (EV-SOT-001). Two " +
                "sibling files travel with it: `OET_AI_DEPLOYMENT_CONTRACT.json`, which fixes the authority-" +
                "precedence order the loader must respect and the active-rule counts per profession at release " +
                "time, and `OET_AI_Source_Manifest.json`, a provenance manifest recording 30 source transcripts " +
                "totalling 84,551 individually reviewed evidence segments that the rule set was distilled from " +
                "(EV-SOT-002). The registry's own README documents every amendment to this file with a date and a " +
                "stated reason, so the rule set has an auditable edit history rather than being silently mutable " +
                "(EV-SOT-001)."),
            new DocumentationSectionBlock(
                "Evidence hierarchy: authority precedence",
                "Not every rule in the registry carries equal weight. The deployment contract fixes an explicit " +
                "authority-precedence order that the build tooling and the grading engine both respect: " +
                "`OET_OFFICIAL` (the six official OET marking criteria/definitions) ranks above " +
                "`GENERAL_ENGLISH_VALIDATED`, which ranks above `DR_HESHAM_DIRECT` (the platform owner's own " +
                "clinical/register rulings), followed by `PROFESSION_RESOURCE`, `DR_HESHAM_PREFERENCE`, " +
                "`DERIVED_PROFESSION`, and finally `LEGACY_INACTIVE` — provenance-only content the contract " +
                "explicitly marks `never_load_as_scoring_truth` (EV-SOT-002). Because the registry has no native " +
                "`critical`/`major` severity field, the build script derives one deterministically rather than " +
                "inventing it: any row whose `authority` is `OET_OFFICIAL`, or whose `classification` is `Safety`, " +
                "`Hard Rule`, `Hard Strategy`, `Validated Override`, or `Owner Override`, becomes `critical`; every " +
                "other active classification — grammar/register `Language Rule`, `Strategy`, `Style`, `Technique`, " +
                "`Preference`, `AI Rule`, `Profession Rule`, and the Rev8 owner-clarification classes — becomes " +
                "`major`. No canonical rule is ever downgraded to `minor`/`info`, on the stated principle that " +
                "every active rule must remain visible to the grader (EV-SOT-003)."),
            new DocumentationSectionBlock(
                "Six canonical, AI-grounded profession rulebooks — and an honestly-scoped gap",
                "The build script `scripts/rulebooks/build-canonical-writing-rulebooks.mjs` filters the registry " +
                "for `skill == \"Writing\" && active_for_ai == true` and regenerates one JSON rulebook per " +
                "profession under `rulebooks/writing/<profession>/rulebook.v1.json`, for the six professions the " +
                "platform has live canonical content for: Medicine, Nursing, Dentistry, Pharmacy, Physiotherapy, " +
                "and Radiography (EV-SOT-004). As of the registry's Revision 8 update, active Writing rows per " +
                "canonical pack are documented as Medicine 351, Nursing 358, Dentistry 358, Pharmacy 361, " +
                "Physiotherapy 361, and Radiography 358, at canonical rulebook version `2.5.0-cross-model-audit` " +
                "(EV-SOT-004). The remaining seven allied-health professions (dietetics, occupational therapy, " +
                "optometry, podiatry, speech pathology, veterinary, other-allied-health) are documented plainly as " +
                "still running on a legacy hand-maintained rule set, not the canonical registry — the registry's " +
                "own README states this without euphemism: a direct production query found real, currently " +
                "candidate-facing Model Answers on the legacy rulebook for five of those seven professions, and " +
                "that the registry itself has zero canonical rows for any of the five, so \"this build script " +
                "cannot construct a 'canonical' AI-grounded profession pack out of nothing\" without a genuine new " +
                "content-authorship pass (EV-SOT-005). This is stated here as the limitation it is, not implied " +
                "away: those five professions receive the platform's full deterministic rule-engine coverage " +
                "(the same 69-check-id \"always-on builtin battery\" runs regardless of which rulebook is loaded) " +
                "but not yet the richer AI-grounded contextual-judgement layer the six canonical professions have " +
                "(EV-SOT-005)."),
            new DocumentationSectionBlock(
                "Provenance and conflict resolution: the G-W-116 amendment as a worked example",
                "The registry's amendment log documents at least one case where an existing rule was found to " +
                "directly contradict a later owner ruling, and shows the resolution process rather than a silent " +
                "edit. Row `G-W-116` originally read that the words \"unfortunately\"/\"fortunately\" were not " +
                "banned by grammar and should not be auto-penalised — wording that directly contradicted a later " +
                "owner addendum's emotional-wording ban. Per the addendum's own precedence rule (a newer " +
                "clarification supersedes older conflicting content) and an explicit owner instruction dated 10 " +
                "September 2026, the row was edited in place to ban \"unfortunately\"/\"fortunately\"/\"regrettably\" " +
                "outright while preserving factual clinical usage such as \"suffered a myocardial infarction\" as " +
                "still correct, and its `classification`/`authority` were promoted to `Hard Rule`/`OET_OFFICIAL` to " +
                "match the stricter reading (EV-SOT-006). The same document records that this edit invalidated the " +
                "file's original SHA-256 by design, and states plainly that the original hash is retained only as " +
                "a provenance pointer, not a current integrity check — an explicit, documented trade-off rather " +
                "than an unnoticed drift (EV-SOT-006). A second documented pattern, \"supersession amendments\", " +
                "shows ten further rows (for example `G-W-112`, `G-W-110`, `DH-W-041`, `DH-W-042`) edited in place " +
                "with their `canonical_rule` text rewritten to begin \"Superseded in part by OWN-W-0xx...\", the " +
                "overridden wording marked, and the change recorded in the row's `notes` field, while the row's id " +
                "and its severity/authority classification are left untouched so every existing reference to that " +
                "rule id keeps resolving (EV-SOT-006)."),
            new DocumentationSectionBlock(
                "A dated, numbered owner-clarification trail",
                "Beyond individual row amendments, the platform's contributor rulebook records a running, dated " +
                "series of owner clarification rounds layered on top of the vendored registry, each with its own " +
                "id namespace: `OA-01`..`OA-15`, `OA2-01`..`OA2-20`, `OA3-01`..`OA3-05`, `OA4-01`..`OA4-09`, and " +
                "further rounds `OA5-01`..`OA5-38` (a Senior Assessor Release Audit, 16 September 2026) and " +
                "`OA6-01`..`OA6-02` (a cross-model audit, 17 September 2026) — the combined registry release is " +
                "itself named for this lineage, `v1.4-cross-model-audit` (EV-SOT-007). These rows are recorded as " +
                "carrying `profession: \"Medicine\"` in the raw registry for bookkeeping purposes but are marked to " +
                "apply globally across every canonical profession pack, per an explicit addendum clause that they " +
                "are \"active globally — not sample-only edits\" (EV-SOT-007). This is the platform's versioned " +
                "content-governance process in concrete, checkable form: a real person made a dated ruling, the " +
                "ruling was assigned a stable id, the id was wired into the registry with an explicit scope, and " +
                "the change is traceable back to that id rather than existing only as an unlogged prompt tweak."),
            new DocumentationSectionBlock(
                "Deterministic checks stay in lock-step with the owner rules",
                "Thirty-eight of the Revision 8 owner rows (`OWN-W-001`..`OWN-W-038`) are each mapped, where a " +
                "deterministic check is possible, to one or more named `WritingRuleEngine` check-ids — for " +
                "example `OWN-W-001` (\"One blank line after the Re: line\") maps to `blank_line_after_re_line` " +
                "and `model_answer_layout`, and `OWN-W-026` (\"Sign-off: professional designation only\") maps to " +
                "four separate checks (EV-SOT-008). Rows with no mechanical detector — genuinely judgement-based " +
                "rules such as introduction tense or factual-fidelity checking — are explicitly left to the AI " +
                "grader and an independent semantic Model Answer validator rather than being force-fitted into a " +
                "regex, and the registry README states this distinction openly rather than glossing over which " +
                "rules are machine-checked and which are not (EV-SOT-008). A dedicated baseline test asserts every " +
                "one of the eleven profession rulebooks (six canonical, seven legacy minus the two already " +
                "counted) carries the current `OWN-W-001..038` text identically, so the owner-clarification layer " +
                "cannot silently drift out of one profession's pack while updating another (EV-SOT-008)."),
            new DocumentationSectionBlock(
                "Runtime access is engine-only — never raw JSON from UI or endpoint code",
                "The platform's contributor rules state the access boundary as a hard invariant: \"Rulebooks: use " +
                "`lib/rulebook` or backend Rulebook services; never read rulebook JSON directly from UI/endpoints\" " +
                "(EV-SOT-009). On the TypeScript side this is `lib/rulebook/loader.ts` (`loadRulebook(kind, " +
                "profession)` / `findRule(...)` / `rulesApplicableTo(...)`, throwing a typed " +
                "`RulebookNotFoundError` for an unregistered profession) and the frozen check-id registry in " +
                "`lib/rulebook/check-ids.ts`; on the .NET side it is `RulebookLoader` (`IRulebookLoader`), which " +
                "loads the rulebook JSON from embedded assembly resources so the running server has no filesystem " +
                "dependency on the rule files at all (EV-SOT-010). The stated purpose of routing every consumer " +
                "through this narrow surface, rather than letting a UI component or endpoint read the JSON " +
                "directly, is that rule text, severity, and applicability can only ever be looked up through a " +
                "single audited code path — never hard-coded into a component, endpoint, or email template " +
                "(EV-SOT-010)."),
            new DocumentationSectionBlock(
                "The published rulebooks and versioning discipline",
                "Alongside the machine-readable registry, the platform retains the source rulebook documents " +
                "themselves under `docs/rulebooks/` — the Listening and Reading rulebooks (each stated to apply " +
                "identically across all professions, both paper- and computer-based), the Speaking rulebook " +
                "(`OET_Speaking_Rulebook_v2.pdf`), and the Writing rulebook (`OET_Writing_Rulebook_FINAL.pdf`) — " +
                "present as real files in the repository rather than referenced only by description (EV-SOT-011). " +
                "Versioning is enforced at the point of use, not just at the point of authoring: every in-flight " +
                "submission is documented as recording the exact `rulebookVersion` it was graded against, so " +
                "publishing a new rulebook release does not retroactively change the scoring basis of work already " +
                "graded (EV-SOT-012). The build script that regenerates the six canonical packs pins the exact " +
                "registry SHA-256 it was built from as a named constant and refuses to build from any other bytes, " +
                "which the README states was added specifically so \"the authoritySource hash can no longer go " +
                "stale silently\" after a registry edit (EV-SOT-001)."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-SOT-001", DocumentationEvidenceType.DataKnowledge,
                "Canonical rule registry provenance, SHA-256 verification history, and the pinned-hash build-safety mechanism, documented with dated amendments rather than silent edits.",
                "docs/canonical-rules/README.md"),
            new DocumentationEvidenceSeed("EV-SOT-002", DocumentationEvidenceType.DataKnowledge,
                "Deployment contract's authority-precedence order and never-load-as-scoring-truth exclusion for legacy/inactive rows; source manifest's transcript-evidence provenance (30 transcripts, 84,551 evidence segments).",
                "docs/canonical-rules/OET_AI_DEPLOYMENT_CONTRACT.json; docs/canonical-rules/OET_AI_Source_Manifest.json"),
            new DocumentationEvidenceSeed("EV-SOT-003", DocumentationEvidenceType.DataKnowledge,
                "Derived (not invented) severity mapping from classification/authority to critical/major, with no canonical rule ever mapped to minor/info.",
                "docs/canonical-rules/README.md, section \"Severity mapping\""),
            new DocumentationEvidenceSeed("EV-SOT-004", DocumentationEvidenceType.DataKnowledge,
                "Canonical rulebook build script and the six live professions' active-Writing-rule counts at rulebook version 2.5.0-cross-model-audit.",
                "docs/canonical-rules/README.md, section \"Revision 8 (11 Sep 2026)\"; scripts/rulebooks/build-canonical-writing-rulebooks.mjs"),
            new DocumentationEvidenceSeed("EV-SOT-005", DocumentationEvidenceType.DataKnowledge,
                "Explicit, dated coverage-gap finding: five of seven non-canonical professions have real live legacy-rulebook Model Answers with zero canonical registry rows, stated as a genuine content-authorship gap rather than glossed over.",
                "docs/canonical-rules/README.md, section \"What this is for\" (10 Sep 2026 correction and migration-status notes)"),
            new DocumentationEvidenceSeed("EV-SOT-006", DocumentationEvidenceType.DataKnowledge,
                "The G-W-116 in-place amendment (10 Sep 2026 owner governance decision) and the documented supersession-amendment pattern for ten further rows, both preserving rule ids while recording the conflict and its resolution.",
                "docs/canonical-rules/README.md, top-of-file amendment note and \"Supersession amendments\" table"),
            new DocumentationEvidenceSeed("EV-SOT-007", DocumentationEvidenceType.DataKnowledge,
                "Dated, numbered owner-clarification registry rounds (OA-01..OA-15, OA2-01..OA2-20, OA3-01..OA3-05, OA4-01..OA4-09, OA5-01..OA5-38, OA6-01..OA6-02) composing the v1.4-cross-model-audit registry release, marked as globally applying across every canonical profession.",
                "AGENTS.md, section \"OET Writing Model Answers — COMPULSORY\"; docs/canonical-rules/README.md, section \"Revision 8 (11 Sep 2026)\""),
            new DocumentationEvidenceSeed("EV-SOT-008", DocumentationEvidenceType.Code,
                "BUILTIN checkId to OWN-W owner-rule mapping table, and the cross-profession baseline test asserting identical OWN-W-001..038 text in every rulebook pack.",
                "docs/canonical-rules/README.md, section \"BUILTIN checkId ↔ OWN-W mapping\"; lib/rulebook/__tests__/writing-rulebook-baseline.test.ts"),
            new DocumentationEvidenceSeed("EV-SOT-009", DocumentationEvidenceType.Architecture,
                "Contributor-facing standing rule that rulebooks must be read only through lib/rulebook or backend Rulebook services, never as raw JSON from UI/endpoint code.",
                "AGENTS.md, section \"OET Domain Invariants\" (Rulebooks bullet)"),
            new DocumentationEvidenceSeed("EV-SOT-010", DocumentationEvidenceType.Code,
                "The engine-only access layer: lib/rulebook/loader.ts (TypeScript) and RulebookLoader/IRulebookLoader loading rulebook JSON from embedded assembly resources (.NET), with the platform's own \"strictly forbidden\" list naming direct rulebook-JSON reads.",
                "docs/RULEBOOKS.md, sections 3 \"Rule engines\" and 9 \"Strictly forbidden\""),
            new DocumentationEvidenceSeed("EV-SOT-011", DocumentationEvidenceType.DataKnowledge,
                "Source rulebook PDFs retained in the repository: Listening, Reading, Speaking (v2), and Writing (FINAL).",
                "docs/rulebooks/ (OET Listening Rulebook, OET Reading Rulebook, OET_Speaking_Rulebook_v2.pdf, OET_Writing_Rulebook_FINAL.pdf)"),
            new DocumentationEvidenceSeed("EV-SOT-012", DocumentationEvidenceType.Reliability,
                "Per-submission rulebook-version pinning so a rulebook upgrade does not retroactively re-grade already-graded submissions.",
                "docs/RULEBOOKS.md, section 7 \"Versioning & drift control\""),
        ]);
}
