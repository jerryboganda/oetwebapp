using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-02 — Founder, Product Origin &amp; Innovation Ownership Statement.</summary>
internal static class Doc02FounderStatement
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-02",
        Title: "Founder, Product Origin & Innovation Ownership Statement",
        Description: "Who conceived the product, founder contribution, medical/OET domain expertise, product decision ownership and IP/commissioning chain.",
        SortOrder: 2,
        Sections:
        [
            new DocumentationSectionBlock(
                "Founder role",
                "OET with Dr Ahmed Hesham is founded and directed by Dr Ahmed Hesham, who is the platform's " +
                "document owner and the final approving authority for every clinical-content, exam-content and " +
                "product-behaviour decision recorded in this pack. The platform is built and operated by a " +
                "small named technical team led by Dr Faisal Maqsood Anwar (technical lead) together with the " +
                "AI, backend, frontend, mobile and DevOps engineers responsible for the surfaces documented in " +
                "DOC-10 (EV-FOUNDER-005). Biographical specifics that only Dr Ahmed Hesham can supply in his own " +
                "words — his medical qualification, professional registration body and number, and years of " +
                "OET/clinical-communication practice — are intentionally left for his direct confirmation before " +
                "this section is used in an external filing, rather than stated here without his sign-off."),
            new DocumentationSectionBlock(
                "Medical/OET domain expertise made visible in the product",
                "The clearest, independently checkable evidence of the founder's domain expertise is not a " +
                "biography — it is the trail of dated, granular clinical-writing rulings embedded directly in the " +
                "engineering repository's own governance files, which an engineer without OET/clinical training " +
                "could not have authored. The repository's always-loaded agent contract records four rounds of " +
                "\"Owner Clarifications\" on Writing model answers between 13 and 21 September 2026 — specifying, " +
                "for example, that medication lists take no semicolon before a final \"and\", that a vital sign is " +
                "reported as its raw value and never re-labelled with a diagnosis, that Latin dosing frequencies " +
                "(nocte, prn) must be rendered in plain English, and that a full patient name may appear once in " +
                "a letter's introduction but never recur in a later paragraph (EV-FOUNDER-002). These are exam-" +
                "board-level and clinical-register judgement calls, not generic software requirements, and they " +
                "are still the active rule set the Writing AI is validated against today."),
            new DocumentationSectionBlock(
                "Contribution to product concept, rulebooks, AI behaviour and acceptance criteria",
                "Beyond clinical content, the same governance layer shows the founder setting product and " +
                "engineering policy directly: a standing \"Ship-It\" release directive (7 Jul 2026, tightened 24 " +
                "Aug 2026) governing how every change reaches production (EV-FOUNDER-001); a non-negotiable rule " +
                "restricting all build/test compute to GitHub Actions (11 Sep 2026) to protect the production VPS " +
                "(EV-FOUNDER-003); and an Android-release rule (5 Sep 2026) requiring every previously-live Play " +
                "Store track to move together after a prior release left one track stuck (EV-FOUNDER-004). Taken " +
                "together, this is a founder acting as the platform's continuous product owner — setting " +
                "acceptance criteria, correcting live defects, and updating policy as the product evolves — not a " +
                "one-time commissioning party."),
            new DocumentationSectionBlock(
                "Development team structure and specialist roles",
                "Per the terms of reference for this pack (this document's own commissioning brief): document " +
                "owner — Dr Ahmed Hesham / OET with Dr Ahmed Hesham; primary technical lead — Dr Faisal Maqsood " +
                "Anwar and the responsible AI, backend, frontend, mobile and DevOps engineers. DOC-14 (Mandatory " +
                "Technical Sign-Offs) names the specific role each specialist signs off against; DOC-10 documents " +
                "the surfaces each of those roles is responsible for."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-FOUNDER-001", DocumentationEvidenceType.DataKnowledge,
                "Standing owner directive: the compulsory \"Ship-It Workflow\" release policy, dated 2026-07-05 and tightened 2026-08-24.",
                "OET Project Web App/AGENTS.md, section \"Ship-It Workflow — COMPULSORY\""),
            new DocumentationEvidenceSeed("EV-FOUNDER-002", DocumentationEvidenceType.DataKnowledge,
                "Four dated rounds of owner clinical-writing rulings (OA-01..OA-15, OA2-01..OA2-20, OA3-01..OA3-05, OA4-01..OA4-09) covering register, dosing-frequency wording, patient-name fidelity and closure structure.",
                "OET Project Web App/AGENTS.md, section \"OET Writing Model Answers — COMPULSORY\"; rows in docs/canonical-rules/OET_AI_Rules_Master.jsonl"),
            new DocumentationEvidenceSeed("EV-FOUNDER-003", DocumentationEvidenceType.DataKnowledge,
                "Owner directive (2026-09-11) restricting all computational workloads to GitHub Actions to protect the production VPS and local machines.",
                "OET Project Web App/AGENTS.md, section \"GITHUB ACTIONS IS THE ONLY AUTHORIZED COMPUTE ENVIRONMENT\""),
            new DocumentationEvidenceSeed("EV-FOUNDER-004", DocumentationEvidenceType.DataKnowledge,
                "Owner rule (2026-09-05) requiring every previously-live Android release track to move together, issued after a real incident left Closed Testing users stuck on an old build.",
                "OET Project Web App/AGENTS.md, section \"OET Domain Invariants\" (App releases bullet)"),
            new DocumentationEvidenceSeed("EV-FOUNDER-005", DocumentationEvidenceType.DataKnowledge,
                "Commissioning brief for this documentation pack naming the document owner and primary technical lead.",
                "\"Official Professional Documentation Requirements\" specification, field table, 23 Sep 2026"),
        ]);
}
