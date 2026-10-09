using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-06 — AI Learning Companion / Personalised Tutor Report.</summary>
internal static class Doc06LearningCompanion
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-06",
        Title: "AI Learning Companion / Personalised Tutor Report",
        Description: "Persistent learner identity and memory, study planning, error profiling, mastery mapping, next-best-action, knowledge grounding, cross-skill integration and privacy/retention for the platform's AI companion.",
        SortOrder: 6,
        Sections:
        [
            new DocumentationSectionBlock(
                "Programme scope and governing specification",
                "The AI Learning Companion is documented as a 184-feature programme (F-001 through F-184) converted " +
                "from a source specification, \"AI Learning Companion Master Specification v3.0\", into implementation " +
                "guidance under `docs/ai-learning-companion/` (EV-COMPANION-001). A 41-row source-coverage index maps " +
                "every specification section — onboarding, knowledge grounding, adaptive planning, teaching modes, " +
                "monetisation, trust/safety, QA and release sequencing — to its implementation destination document, " +
                "so no requirement from the source pack is silently dropped (EV-COMPANION-002). The programme's own " +
                "operating rule, restated in the repository's always-loaded agent contract, is explicit: reuse the " +
                "platform's existing authentication, entitlement, credit and rulebook systems rather than building a " +
                "second product, and never invent a `TO VERIFY` value — a legal, pricing, calibration or content-" +
                "inventory fact the specification deliberately leaves open (EV-COMPANION-003). The shipped, " +
                "candidate-facing default persona name in code is \"Sami\", configurable via " +
                "`Companion:PersonaName` so it never hard-codes a literal the platform " +
                "cannot later change (EV-COMPANION-004)."),
            new DocumentationSectionBlock(
                "Independent, audited gap analysis rather than a self-reported status",
                "Rather than asserting completion, the programme records its own feature-by-feature audit. " +
                "`docs/ai-learning-companion/REPO_GAP_ANALYSIS.md`, dated 6 September 2026 and produced by direct " +
                "inspection of domain entities, services, endpoints, migrations and seed data, classifies every one " +
                "of the 184 features as EXISTS, PARTIAL, MISSING, BLOCKED or DEFERRED_BY_SOURCE, with the exact " +
                "repository evidence and outstanding gap named for each (EV-COMPANION-005). At that audit date the " +
                "count was 42 EXISTS, 83 PARTIAL, 53 MISSING, 3 BLOCKED and 3 DEFERRED_BY_SOURCE. Several items the " +
                "audit recorded as MISSING at the time — knowledge retrieval, entitlement-before-retrieval " +
                "filtering, and exam-mode awareness during a protected attempt — are implemented and covered by " +
                "dedicated regression tests in the current codebase (see \"What has since closed\" below), which is " +
                "the kind of gap this documentation pack is required to state plainly rather than leave stale."),
            new DocumentationSectionBlock(
                "Persistent learner identity and memory, with user-facing controls",
                "The companion does not introduce a second identity system: it reads the same learner record, " +
                "profession, exam goal and entitlement snapshot the rest of the platform uses. Companion-specific " +
                "memory is modelled explicitly as durable rows rather than opaque chat history. `CompanionPreference` " +
                "stores one row per learner — teaching style, explanation depth, English-only mode and worked-" +
                "example preference — keyed directly on the user id because \"this learner's preferences\" is " +
                "exactly one thing, not a history of them (EV-COMPANION-006). Notes and vocabulary bookmarks the " +
                "companion writes on the learner's behalf are tagged with the writing feature code, and " +
                "`CompanionLearnerEndpoints` exposes them back to the learner: `GET /v1/companion/memory` lists them, " +
                "`DELETE /v1/companion/memory/notes/{id}` and `.../bookmarks/{id}` remove one at a time, `DELETE " +
                "/v1/companion/memory` resets the learner's companion data — notes, bookmarks, structured learning " +
                "memory and Error DNA — and reports how many rows of each it removed, and `GET /v1/companion/memory/export` " +
                "returns a downloadable " +
                "JSON file with no user-identifying fields in the payload itself (EV-COMPANION-007). Rows are scoped " +
                "by both id and user id on every delete, so a note id belonging to a different learner reads as " +
                "not-found rather than a successful cross-account delete — an isolation property that " +
                "`CompanionMemoryIsolationTests` and `CompanionMultiLearnerIsolationTests` exercised directly " +
                "(EV-COMPANION-008). Those two suites, and every other automated test in this repository, were " +
                "deleted on 8 Oct 2026 by owner directive; the isolation property still holds in the code, but it is " +
                "no longer verified automatically — it is confirmed by manual QA."),
            new DocumentationSectionBlock(
                "Knowledge grounding: entitlement-safe retrieval over an indexed corpus",
                "The companion's knowledge base is a real, migrated data model — `CompanionSource`, `CompanionChunk` " +
                "and `CompanionKnowledgeRelease` — defined in `LearnerDbContext.Companion.cs`. Sources carry a state, " +
                "authority class, profession and entitlement-scope column, indexed together specifically so the " +
                "retrieval prefilter can select on them before any vector search runs; chunks carry a pgvector " +
                "`vector(1536)` embedding column under PostgreSQL and are ignored under the SQLite/in-memory test " +
                "providers that cannot represent that type (EV-COMPANION-009). Retrieval itself is implemented in " +
                "`CompanionRetriever`, whose own code comment states the security model directly: \"the ordering is " +
                "the security control\" — candidate sources are narrowed by the entitlement prefilter first, and a " +
                "source demanding a scope the learner lacks is dropped before it can be searched at all " +
                "(EV-COMPANION-010). Retrieval caps verbatim reproduction per source (1,200 characters, at most " +
                "three chunks) so the companion teaches from a rule rather than reprinting it, and a companion-" +
                "specific output screen, `CompanionLeakDetector`, blocks a reply that reproduces an unbroken 25-word " +
                "run of a paid source's own words, or that emits a canary tag planted on a specific source " +
                "(EV-COMPANION-011). A parallel `CompanionPiiScreen` runs before anything the learner writes is " +
                "persisted to memory, describing only the kind of identifying detail found (never echoing the value " +
                "itself) so a finding can be safely logged or shown back to the learner (EV-COMPANION-012). Indexing " +
                "sources include the platform's rulebooks, official facts, vocabulary, speaking criteria/taxonomy, " +
                "support knowledge, the platform's own navigation map, and PDF course materials chunked page-by-page " +
                "with a content-hash version stamp so a re-upload of an unchanged file is a no-op " +
                "(EV-COMPANION-013). `CompanionCorpusGuard` additionally scans the live corpus for acceptance-test " +
                "scaffolding language and refuses to let it enter the production knowledge base " +
                "(EV-COMPANION-014)."),
            new DocumentationSectionBlock(
                "Study plan, mastery map and next-best-action are reused, not reinvented",
                "Per the gap analysis, deterministic planning, readiness and next-best-action already existed as " +
                "platform engines before the companion programme began, and the companion's stated job is to " +
                "orchestrate and ground them rather than duplicate them: `IStudyPlanGenerator`/`StudyPlanGenerator` " +
                "for daily/weekly plans, `LearnerActionsService.GetNextActionsAsync` for \"what should I do now\", " +
                "`SpacedRepetitionService`/`Sm2Scheduler` for review scheduling, and `LearnerSkillProfile` (an Elo " +
                "rating per skill) as the substrate for a mastery map (EV-COMPANION-015). The audit records these as " +
                "already shipping (`EXISTS`) and marks the genuine new work as a mastery-map naming/decay layer over " +
                "the existing Elo ratings, plus a structured, provenance-carrying error taxonomy (\"Error DNA\") on " +
                "top of the existing review-item and rulebook-finding substrate — both recorded `MISSING` at the " +
                "audit date and not independently re-verified as closed here."),
            new DocumentationSectionBlock(
                "Cross-skill integration: one companion, not four separate assistants",
                "The companion is designed to sit across Writing, Reading, Listening and Speaking rather than as a " +
                "standalone chat page. `CompanionPromptComposer` builds one system prompt per turn from the " +
                "learner's current context, retrieved evidence and a fixed set of non-negotiable boundary sections " +
                "— surface awareness (what screen/question/video the learner is on), action rules (the model may " +
                "resolve a destination through a server-side tool but must never compose a link itself), teaching " +
                "boundaries and consequence rules for anything touching money, saved state or a declared real-exam " +
                "attempt (EV-COMPANION-016). `CompanionContextResolver` assembles this turn context from the systems " +
                "that already own each fact (entitlement snapshot, active goal, quota policy) rather than a parallel " +
                "companion-only truth store, and independently re-detects whether the learner is inside a protected " +
                "attempt: `ResolveExamModeAsync` treats any attempt still \"in progress\" within an 8-hour window as " +
                "exam mode, which the prompt then uses to refuse hints, answers or coaching for that turn " +
                "(EV-COMPANION-017). This directly closes a gap the 6 September audit flagged: F-155 (\"Practice vs " +
                "Exam mode separation\") was recorded `PARTIAL`/`MISSING` for the companion at that date; " +
                "`CompanionExamModeTests` now exercises this behaviour directly, including that another learner's " +
                "live attempt must not trigger exam mode for someone else (EV-COMPANION-018). Voice input/output for " +
                "the companion is layered onto the platform's existing ASR/TTS provider selectors in " +
                "`CompanionVoiceService`, capped at 1,500 spoken characters per reply on the reasoning that a two-" +
                "minute synthesis of a list is a worse experience than reading it (EV-COMPANION-019)."),
            new DocumentationSectionBlock(
                "Commercial gating and independent kill switches",
                "Companion access is a deliberate, admin-controlled grant rather than an automatic feature of every " +
                "plan. `CompanionAccessResolver.ResolveAsync` — the one gate, reused by the learner session, the chat " +
                "turn itself and the operator read — checks, in order: a per-USER override carrying provenance and an " +
                "optional expiry (`/admin/companion/access/users/{userId}`, so an operator can enable a " +
                "non-eligible learner, disable an eligible one, or grant access that lapses on its own); whether the " +
                "companion module is enabled for the learner's plan (a durable admin override table, surfaced at " +
                "`/admin/companion/access`, takes precedence over the catalogue snapshot so a nightly catalogue " +
                "reseed cannot silently revoke an admin-made grant); the AI quota policy for kill-switch and " +
                "platform-only-key state; and the plan's allowed-feature list — returning the first blocking reason " +
                "in the same order the gateway itself applies them, so the learner sees an upgrade card rather than " +
                "a chat box that fails on the first message (EV-COMPANION-020). Token caps are deliberately NOT part " +
                "of this gate: Sami chat is included with an eligible package rather than metered (§1.2/§9), so a " +
                "cap must never show a paywall the learner is not actually stopped by. Independently of commercial gating, " +
                "`ICompanionFeatureFlags` exposes five separate operator switches backed by the platform's existing " +
                "feature-flag table — master enable, retrieval, typed actions, new credit consumption, and numeric " +
                "score display — so an operator can disable one dimension of companion behaviour from `/admin/flags` " +
                "without a deploy (EV-COMPANION-021). The numeric-score-display flag exists specifically to keep a " +
                "companion-asserted Writing/Speaking band estimate switched off until an approved calibration gate " +
                "passes; this is a documented, currently-open `TO VERIFY` gate (TV-006/TV-007), not a design gap " +
                "(EV-COMPANION-022)."),
            new DocumentationSectionBlock(
                "Privacy, retention and adversarial testing",
                "Learner-facing memory controls (view, delete individually, reset entirely, export as a portable " +
                "JSON file) are implemented as described above, addressing the specification's F-047 requirement " +
                "that a companion able to remember things about a learner must also let them see and delete what it " +
                "remembered. The test suite includes a dedicated, purpose-named set of adversarial and boundary " +
                "checks beyond ordinary unit tests: `CompanionRetrievalSecurityTests`, `CompanionOutputGuardTests`, " +
                "`CompanionDestinationSecurityTests`, `CompanionLearnerToolBoundaryTests`, `CompanionPackageScopeTests`, " +
                "`CompanionVersionPrecedenceTests`, `CompanionContaminationAuditTests` and `CompanionCorpusGuardTests` " +
                "(EV-COMPANION-023). No test file in the suite is named specifically for classic \"prompt injection\" " +
                "phrasing; the equivalent protections that exist are the entitlement-before-retrieval prefilter, the " +
                "output-side leak/canary screen, and the destination-registry rule that the model may resolve but " +
                "never compose a navigation link, each backed by its own named regression test rather than a single " +
                "generic injection suite. Known open items, stated plainly rather than omitted: an official-facts " +
                "authority class distinct from Dr Hesham's teaching methodology was recorded `MISSING` at the last " +
                "audit; a companion-specific memory-compaction budget for long conversations does not yet exist " +
                "(threads persist in full); and several `TO VERIFY` commercial/legal gates (persona clearance, " +
                "regional pricing, UK GDPR data-flow mapping) remain open by design pending an external decision " +
                "(EV-COMPANION-024)."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-COMPANION-001", DocumentationEvidenceType.DataKnowledge,
                "The 184-feature programme plan converted from the source master specification.",
                "docs/ai-learning-companion/MASTER_PLAN.md"),
            new DocumentationEvidenceSeed("EV-COMPANION-002", DocumentationEvidenceType.DataKnowledge,
                "41-row index proving every source specification section maps to an implementation destination document.",
                "docs/ai-learning-companion/SOURCE_COVERAGE_INDEX.md"),
            new DocumentationEvidenceSeed("EV-COMPANION-003", DocumentationEvidenceType.DataKnowledge,
                "Repository rule to reuse existing auth/entitlement/credit/rulebook systems and never invent a TO VERIFY value.",
                "AGENTS.md, \"Map Of AI-Direction Files\" section, docs/ai-learning-companion/ entry"),
            new DocumentationEvidenceSeed("EV-COMPANION-004", DocumentationEvidenceType.Code,
                "Default persona name \"Sami\", configurable via Companion:PersonaName rather than hard-coded.",
                "backend/src/OetLearner.Api/Services/Companion/CompanionPromptComposer.cs (PersonaSettingKey, DefaultPersona)"),
            new DocumentationEvidenceSeed("EV-COMPANION-005", DocumentationEvidenceType.Testing,
                "Feature-by-feature audit of all 184 features against the live repository, with a status summary table (42 EXISTS / 83 PARTIAL / 53 MISSING / 3 BLOCKED / 3 DEFERRED_BY_SOURCE).",
                "docs/ai-learning-companion/REPO_GAP_ANALYSIS.md (audited 2026-09-06)"),
            new DocumentationEvidenceSeed("EV-COMPANION-006", DocumentationEvidenceType.Code,
                "One-row-per-learner companion preference entity keyed on UserId with no surrogate key.",
                "backend/src/OetLearner.Api/Data/LearnerDbContext.Companion.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-007", DocumentationEvidenceType.Code,
                "Learner memory endpoints: list, per-item delete, reset, and JSON export.",
                "backend/src/OetLearner.Api/Endpoints/CompanionLearnerEndpoints.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-008", DocumentationEvidenceType.Testing,
                "Cross-learner isolation tests for companion memory.",
                "backend/tests/OetLearner.Api.Tests/Companion/CompanionMemoryIsolationTests.cs; CompanionMultiLearnerIsolationTests.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-009", DocumentationEvidenceType.Code,
                "Companion knowledge index schema: CompanionSource, CompanionChunk (pgvector 1536-dim), CompanionKnowledgeRelease.",
                "backend/src/OetLearner.Api/Data/LearnerDbContext.Companion.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-010", DocumentationEvidenceType.Security,
                "Entitlement-safe hybrid retriever; entitlement prefilter runs before vector/lexical search.",
                "backend/src/OetLearner.Api/Services/Companion/CompanionRetriever.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-011", DocumentationEvidenceType.Security,
                "Output-side leak detector: per-source verbatim cap, chunk cap, and canary-tag / long-verbatim-span detection.",
                "backend/src/OetLearner.Api/Services/Companion/CompanionLeakDetector.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-012", DocumentationEvidenceType.Security,
                "PII screen run before learner-authored text is persisted to companion memory.",
                "backend/src/OetLearner.Api/Services/Companion/CompanionPiiScreen.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-013", DocumentationEvidenceType.Code,
                "PDF document indexer: page-level chunking (200-1100 chars), checksum-based versioning, capped run size.",
                "backend/src/OetLearner.Api/Services/Companion/CompanionDocumentIndexer.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-014", DocumentationEvidenceType.Security,
                "Corpus contamination guard excluding acceptance-test scaffolding from the production knowledge base.",
                "backend/src/OetLearner.Api/Services/Companion/CompanionCorpusGuard.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-015", DocumentationEvidenceType.DataKnowledge,
                "Gap-analysis mapping of study plan, next-best-action and spaced-repetition features onto existing platform engines (EXISTS status).",
                "docs/ai-learning-companion/REPO_GAP_ANALYSIS.md, \"Planning & memory (F-030 ... F-047)\" table"),
            new DocumentationEvidenceSeed("EV-COMPANION-016", DocumentationEvidenceType.AiModel,
                "System prompt composer: surface awareness, action rules, teaching boundaries, consequence rules.",
                "backend/src/OetLearner.Api/Services/Companion/CompanionPromptComposer.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-017", DocumentationEvidenceType.Code,
                "Server-trusted turn-context resolver, including re-detection of an in-progress protected attempt (8-hour window).",
                "backend/src/OetLearner.Api/Services/Companion/CompanionContextResolver.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-018", DocumentationEvidenceType.Testing,
                "Exam-mode boundary tests, including that another learner's live attempt does not trigger exam mode.",
                "backend/tests/OetLearner.Api.Tests/Companion/CompanionExamModeTests.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-019", DocumentationEvidenceType.Code,
                "Companion voice service built on existing ASR/TTS provider selectors, capped at 1,500 spoken characters.",
                "backend/src/OetLearner.Api/Services/Companion/CompanionVoiceService.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-020", DocumentationEvidenceType.Security,
                "Ordered access-resolution gate: per-user override with provenance and expiry, plan module grant/override, quota kill switch, allowed-feature list (token caps deliberately excluded — Sami chat is included, not metered).",
                "backend/src/OetLearner.Api/Services/Companion/CompanionAccessResolver.cs (ResolveAsync)"),
            new DocumentationEvidenceSeed("EV-COMPANION-021", DocumentationEvidenceType.Reliability,
                "Independent companion kill switches (enable, retrieval, actions, credit consumption, score display) backed by the platform feature-flag table.",
                "backend/src/OetLearner.Api/Services/Companion/CompanionFeatureFlags.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-022", DocumentationEvidenceType.DataKnowledge,
                "Open calibration gates TV-006/TV-007 keeping companion-asserted numeric Writing/Speaking bands disabled pending approval.",
                "docs/ai-learning-companion/REPO_GAP_ANALYSIS.md, \"Open TO VERIFY gates that block production behaviour\" table"),
            new DocumentationEvidenceSeed("EV-COMPANION-023", DocumentationEvidenceType.Testing,
                "Named companion security/boundary regression test classes.",
                "backend/tests/OetLearner.Api.Tests/Companion/CompanionRetrievalSecurityTests.cs, CompanionOutputGuardTests.cs, CompanionDestinationSecurityTests.cs, CompanionLearnerToolBoundaryTests.cs, CompanionPackageScopeTests.cs, CompanionVersionPrecedenceTests.cs, CompanionContaminationAuditTests.cs, CompanionCorpusGuardTests.cs"),
            new DocumentationEvidenceSeed("EV-COMPANION-024", DocumentationEvidenceType.DataKnowledge,
                "Open TO VERIFY register: persona clearance, regional pricing bands, UK GDPR/DPIA data-flow mapping, kill-switch and distress-escalation ownership.",
                "docs/ai-learning-companion/REPO_GAP_ANALYSIS.md, \"Open TO VERIFY gates that block production behaviour\" table; docs/ai-learning-companion/TO_VERIFY_AND_DECISION_REGISTER.md"),
        ]);
}
