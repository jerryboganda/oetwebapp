using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-01 — Master Technical & Innovation Dossier. Synthesises DOC-02..DOC-15; detailed citations live in each specialist report.</summary>
internal static class Doc01MasterDossier
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-01",
        Title: "Master Technical & Innovation Dossier",
        Description: "Single external-review narrative covering the complete platform and its innovation — readable by a non-developer in 10 minutes, traceable claim-by-claim by a technical reviewer.",
        SortOrder: 1,
        Sections:
        [
            new DocumentationSectionBlock(
                "Executive overview",
                "OET with Dr Ahmed Hesham is an exam-preparation platform for the Occupational English Test " +
                "(OET), the English-proficiency exam required of internationally trained healthcare " +
                "professionals seeking registration in countries including the UK, Australia, New Zealand and " +
                "the UAE. Its users are doctors, nurses and allied-health professionals whose registration, " +
                "livelihood and ability to practise medicine abroad depend on a single graded exam attempt. " +
                "OET is technically demanding to prepare candidates for precisely because it is not general " +
                "English: a Writing answer is graded against clinical-register conventions (passive voice for " +
                "medication changes, precise vital-sign reporting, no invented dates or diagnoses), a Speaking " +
                "role-play must simulate a believable patient with a hidden, consistent clinical history, and " +
                "Listening/Reading content must mirror real healthcare workplace and consultation contexts. " +
                "The platform's current operational footprint spans one backend serving five client surfaces " +
                "(web, Android, iOS, Windows desktop, best-effort macOS — DOC-10), a versioned clinical " +
                "rulebook governance layer built from real owner clinical judgement rather than generic " +
                "grading heuristics (DOC-09), and AI-assisted Writing, Speaking, and a learning-companion " +
                "tutor, each separately documented in DOC-03 through DOC-08. What is genuinely innovative " +
                "here, versus a standard LMS with an AI wrapper, is detailed in \"Innovation map\" below: it is " +
                "not \"the platform uses AI\", it is the specific combination of a founder-authored clinical " +
                "rulebook, a hidden-state patient role-play engine, and evidence-locked, auditable AI grading — " +
                "each traceable to real code and real dated governance decisions, not marketing description."),
            new DocumentationSectionBlock(
                "Founder & domain fit",
                "Dr Ahmed Hesham is the platform's founder and document owner; the technical build is led by " +
                "Dr Faisal Maqsood Anwar together with the AI, backend, frontend, mobile and DevOps engineers " +
                "named against their surfaces in DOC-14's sign-off table. DOC-02 sets out the fuller picture, " +
                "including an intentional, disclosed limit: the founder's specific medical qualification and " +
                "registration details are left for his own direct confirmation before external use, rather " +
                "than stated without his sign-off. What is independently checkable today is the trail of " +
                "dated, granular clinical-writing rulings embedded directly in the engineering repository's " +
                "own governance files — four rounds of \"Owner Clarifications\" on Writing model answers " +
                "between 13 and 21 September 2026 alone, specifying exam-board-level clinical-register " +
                "judgement calls no generic software requirement would produce (DOC-02, DOC-09). This is the " +
                "clearest evidence available that the product's clinical logic is founder-authored domain " +
                "expertise, not an off-the-shelf grading model."),
            new DocumentationSectionBlock(
                "Innovation map",
                "Five things are genuinely novel here, each cross-referenced to its full specialist report: " +
                "(1) A founder-authored, versioned clinical rulebook governing AI Writing feedback, with " +
                "dated owner-clarification rounds tied to named validator versions so every graded submission " +
                "can be traced to the exact rule set it was checked against — live (DOC-05, DOC-09). " +
                "(2) A Live Speaking Agent whose patient persona carries a hidden, fact-gated clinical state " +
                "(allowed facts, prohibited facts, reveal conditions) separate from the assessor that scores " +
                "the session afterwards — implemented in the codebase, with realtime and post-session scoring " +
                "kept deliberately apart (DOC-04). (3) An AI Learning Companion designed as a persistent " +
                "cross-skill mentor rather than a stateless chatbot, with real retrieval, entitlement-gating " +
                "and exam-mode awareness now implemented in code (DOC-06). (4) A storage and AI-provider " +
                "architecture built for vendor substitution from day one — a single interface swaps between " +
                "AWS S3, DigitalOcean Spaces and Cloudflare R2 for storage, and a single provider table swaps " +
                "AI vendors, by configuration alone, not a rewrite (DOC-03, DOC-10). (5) A documentation and " +
                "evidence-governance discipline that is itself unusual for a platform this size: dated owner " +
                "directives are written into the engineering contract as binding rules, and this very pack is " +
                "generated from live system state rather than hand-maintained prose (DOC-13, DOC-15). Each " +
                "item above is implemented in the codebase today; where a capability is beta, restricted or " +
                "planned rather than fully live, its specialist report says so explicitly (see DOC-04 §status, " +
                "DOC-06 §status, DOC-07 §status)."),
            new DocumentationSectionBlock(
                "Architecture in one view",
                "One ASP.NET Core Minimal API backend (EF Core, PostgreSQL with pgvector, SignalR) is called " +
                "by every candidate-facing surface — Next.js web, Android and iOS via Capacitor, Windows/macOS " +
                "via a remote-loading Tauri shell — so no client maintains its own copy of scoring, rulebook " +
                "or entitlement logic (DOC-10). The AI orchestration layer sits behind a swappable provider " +
                "table (DOC-03); the knowledge/RAG layer is the versioned rulebook and canonical-rules registry " +
                "(DOC-09); the realtime voice layer is the Live Speaking Agent's session/persona engine " +
                "(DOC-04); the assessment/scoring layer spans Writing, Speaking, Listening/Reading and " +
                "Placement (DOC-05, DOC-07, DOC-08); and the storage/analytics/admin control plane is the " +
                "IFileStorage abstraction, audit-event log and the Admin Documentation Center itself " +
                "(DOC-10, DOC-11, DOC-15). Seven named SignalR hubs keep each realtime feature area " +
                "independently authorizable rather than multiplexed through one generic socket (DOC-10)."),
            new DocumentationSectionBlock(
                "Evidence & metrics",
                "Concrete, sourced figures cited across the specialist reports include: the production Writing " +
                "grader model and its measured prompt-size/cost profile (DOC-05); the deployed AI provider " +
                "roster and default models, including the default Anthropic model used platform-wide (DOC-03); " +
                "1,325 commits recorded on the main development history between 1 July and 24 September 2026, " +
                "counted directly from git history rather than estimated (DOC-13); a real production Writing " +
                "catalogue scan (194 published tasks, initially zero publish-ready) followed by a genuine paid " +
                "end-to-end grading proof on a live learner account (DOC-13); a real, dated audio-integrity " +
                "audit of the Listening catalogue with outstanding items disclosed rather than hidden (DOC-08); " +
                "and a Security report that states plainly, rather than implies, that no completed independent " +
                "penetration test exists yet (DOC-11). Every figure above resolves to a numbered evidence entry " +
                "in its source report's Evidence Register (DOC-15) — this dossier does not restate their " +
                "citations to avoid duplicate evidence identifiers; follow the module reference to the primary " +
                "source."),
            new DocumentationSectionBlock(
                "Innovation, viability & scalability",
                "Competitive differentiation rests on the same barriers to replication a reviewer would expect " +
                "to see argued, and each is real rather than aspirational: a founder-authored, dated, versioned " +
                "clinical rulebook that a generic team could not reproduce without the underlying medical/OET " +
                "domain judgement (DOC-02, DOC-09); a multi-profession evidence and grading pipeline with " +
                "named validator versions per rule round (DOC-05); a hidden-state Speaking role-play engine " +
                "separating persona behaviour from scoring (DOC-04); and cross-skill learner state intended to " +
                "connect Writing, Speaking, Listening and Reading progress through one companion rather than " +
                "four disconnected tools (DOC-06). The current engineering team is named by role in DOC-14's " +
                "sign-off table. Scalability is architectural, not aspirational: the AI-provider and storage " +
                "layers are already built for vendor substitution (DOC-03, DOC-10), and the same backend " +
                "already serves five client platforms from one codebase (DOC-10) — the concrete, explicitly-" +
                "labelled next steps for further exam adapters, institutional/B2B use and international growth " +
                "are set out, and clearly separated from what is already live, in DOC-14's roadmap. Commercial " +
                "operability — cost controls, credit/entitlement metering, and infrastructure economics — is " +
                "addressed at architecture level in DOC-03 (AI cost/usage tracking) and DOC-10 (storage/compute " +
                "boundaries); this pack does not restate business financial evidence, which sits outside its " +
                "technical scope per this pack's own terms of reference."),
            new DocumentationSectionBlock(
                "Roadmap",
                "A full, explicitly labelled (committed / target / aspirational) roadmap is maintained in " +
                "DOC-14 rather than duplicated here, in line with this pack's own rule that a planned " +
                "capability is never presented as already live. In summary: near-term work builds on the " +
                "provider-abstraction and multi-shell architecture already in production (DOC-03, DOC-10); " +
                "medium-term ambitions include further exam-board adapters and deeper cross-skill learner " +
                "state in the Learning Companion (DOC-06, DOC-14); longer-term, aspirational ambitions include " +
                "institutional/B2B and white-label deployment models, which DOC-14 states plainly are not yet " +
                "committed work. Readers evaluating innovation and growth potential should treat DOC-14 as " +
                "authoritative on status labelling, not this summary."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-MASTER-001", DocumentationEvidenceType.DataKnowledge,
                "This dossier synthesises 14 specialist reports (DOC-02..DOC-15), each independently sourced and evidenced; this document intentionally does not duplicate their evidence identifiers.",
                "Admin Documentation Center — DOC-02 through DOC-15, this pack"),
            new DocumentationEvidenceSeed("EV-MASTER-002", DocumentationEvidenceType.Architecture,
                "One backend (ASP.NET Core Minimal API, EF Core, PostgreSQL, SignalR) serving five client surfaces (Next.js web, Android, iOS, Windows desktop, best-effort macOS) from a single codebase.",
                "AGENTS.md, section \"Stack\"; DOC-10"),
        ]);
}
