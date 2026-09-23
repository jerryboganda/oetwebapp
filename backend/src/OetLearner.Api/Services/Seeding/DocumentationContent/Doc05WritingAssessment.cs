using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-05 — Writing AI Assessment &amp; Tutor Technical Report.</summary>
internal static class Doc05WritingAssessment
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-05",
        Title: "Writing AI Assessment & Tutor Technical Report",
        Description: "Profession routing, OET Writing criteria mapping, evidence-locked deductions, correction and tutoring output, score calibration against a named clinician, and auditability of the Writing pipeline.",
        SortOrder: 5,
        Sections:
        [
            new DocumentationSectionBlock(
                "Profession-scoped rulebooks",
                "Writing model answers and grading rules are scoped per profession rather than treated as one generic " +
                "English-writing rubric. The platform's own governance file records that the system supports 13 " +
                "OET professions — medicine, nursing, dentistry, pharmacy, physiotherapy, veterinary, optometry, " +
                "radiography, occupational therapy, speech pathology, podiatry, dietetics, and other allied health — " +
                "and that these split into two rulebook generations: a tier-1 group (medicine, nursing, dentistry, " +
                "pharmacy, physiotherapy, radiography) running a larger, more heavily audited rule pack of 351-361 " +
                "rules, and a tier-2 group running a smaller, 210-219-rule generic pack (EV-WRITE-001). Every " +
                "Writing rule is stored as a discrete row rather than embedded in prose: `WritingCanonRule` carries " +
                "the letter types and professions it applies to, correct/incorrect examples, and a machine-checkable " +
                "detection configuration, all as structured JSON columns, and each violation raised against a real " +
                "submission is recorded against the specific rule that fired via `WritingCanonViolation` (EV-WRITE-002)."),
            new DocumentationSectionBlock(
                "OET Writing criteria mapping and evidence-locked deductions",
                "The platform's compulsory Writing governance file records four dated rounds of owner rulings — " +
                "identified as OA-01 through OA-15, OA2-01 through OA2-20, OA3-01 through OA3-05, and a further " +
                "senior-assessor audit (OA5-01 through OA5-38) and cross-model audit (OA6-01/OA6-02) — each ruling " +
                "on a specific, checkable clinical-writing behaviour: for example, that medication lists take no " +
                "semicolon before a final \"and\", that Latin dosing frequencies (e.g. \"nocte\") must be rendered in " +
                "plain English, that a vital sign is reported as its raw value and never re-labelled with a " +
                "diagnosis, and that the same functional request must never appear in both a letter's introduction " +
                "and its closure (EV-WRITE-003). These rulings are not free-standing guidance; they are enforced as " +
                "detector branches in `WritingRuleEngine`, and every owner-flagged defect type is required to become " +
                "a permanent regression fixture before further letters are produced — the governance file states " +
                "explicitly that a validator reporting zero findings while a visible defect survives is treated as a " +
                "validator defect to be fixed, not a letter to be reworded around the gap (EV-WRITE-004). This is the " +
                "platform's evidence-locked deduction model: a finding is only raised, and only trusted, when it " +
                "traces to a named, versioned rule and a specific letter span, not a generic language-quality score."),
            new DocumentationSectionBlock(
                "Production grading architecture",
                "A benchmark investigation dated 21 September 2026, carried out against the live backend checkout, " +
                "traced the production Writing grader end to end and recorded that it is not Gemini but Anthropic's " +
                "`claude-sonnet-5`, called through the Anthropic Messages API, resolved through a database-backed " +
                "feature-route table so the model can be changed by an admin without a code deploy (EV-WRITE-005). " +
                "The same investigation recorded that the deterministic `WritingRuleEngine` currently runs strictly " +
                "after the AI grade and merges its findings into the report afterwards, rather than being fed into " +
                "the model's prompt beforehand, and that the grader has no enforced structured-output schema — it " +
                "asks for JSON in prose and a parse failure causes the grade to be refused rather than silently " +
                "guessed (EV-WRITE-005). It also measured that a tier-1 profession's system prompt (for example " +
                "medicine, at 351 rules) runs to roughly 35,000 tokens versus roughly 10,600 for a tier-2 profession, " +
                "with the great majority of every request being a fixed, cacheable prefix — which is why prompt " +
                "caching, not model choice, is described as the dominant cost lever for this pipeline (EV-WRITE-005)."),
            new DocumentationSectionBlock(
                "The $0-hard-rule authoring policy",
                "Model-answer authoring itself is governed by a standing, named-non-negotiable policy: no paid AI " +
                "API call is permitted for Writing model-answer work. Every letter is written, repaired, or reviewed " +
                "by the responsible engineer directly, and any import into the validator is run with " +
                "`includeSemantic: false` — meaning the semantic layer is the engineer's own documented review, not " +
                "a further model call (EV-WRITE-006). This policy is recorded alongside an explicit scope limit: " +
                "targeted repair of a flagged defect only, never a full regeneration of an otherwise-good letter for " +
                "one small issue, and no expansion to further professions or content cells without explicit owner " +
                "approval (EV-WRITE-006)."),
            new DocumentationSectionBlock(
                "Score calibration against a named clinician",
                "The platform maintains a purpose-built calibration mechanism rather than relying on the production " +
                "grader marking its own homework: `WritingCalibrationLetter` stores a letter alongside a grade given " +
                "by the platform's named clinical authority (`DrAhmedGradeJson`), `WritingCalibrationRun` records a " +
                "dated calibration run against a specific model version, and `WritingCalibrationResult` stores the " +
                "AI's grade for each letter in that run (`AiGradeJson`) together with its absolute error against the " +
                "clinician's grade, indexed for a \"largest error first\" report per run (EV-WRITE-007). This is the " +
                "structural mechanism by which any future re-grading of the production model, or a swap to a " +
                "different model, can be measured against a real clinician's judgement rather than asserted."),
            new DocumentationSectionBlock(
                "Submissions, appeals, and the correction/tutoring surface",
                "A learner's Writing attempt is stored as a `WritingSubmission`, with a unique per-submission " +
                "`WritingGrade` carrying per-criterion feedback and a top-three-priorities list as structured JSON, " +
                "and a `WritingScoreAppeal` entity through which a learner can contest a grade, tracked by status " +
                "and linked back to the original grade it appeals (EV-WRITE-008). Idempotency is enforced at the " +
                "database level for resubmission: a unique index on the learner's id plus an idempotency key prevents " +
                "a duplicate submission from being graded twice, and a content-hash index allows the pipeline to " +
                "recognise a resubmitted letter it has already graded (EV-WRITE-008). Every submission's evidence " +
                "trail is exportable to the candidate and to reviewers as a formal document: `WritingPdfService` " +
                "generates a watermarked PDF of the attempt on demand — never stored — carrying a diagonal " +
                "\"PRACTICE COPY\" overlay, a per-page footer naming the learner and attempt id, and a last-page " +
                "invisible HMAC token that allows the platform to verify a leaked copy's origin server-side " +
                "(EV-WRITE-009). Access to that PDF is role-gated: the endpoint resolves the caller as the " +
                "attempt's owner, an authenticated expert reviewer, or an admin holding the content-read permission, " +
                "and is rate-limited to 10 downloads per day (EV-WRITE-009)."),
            new DocumentationSectionBlock(
                "Scoring anchor and country-aware pass mark",
                "Writing shares the platform's single canonical scoring module rather than any inline pass-mark " +
                "arithmetic. The canonical scoring specification records that Writing's pass mark is country-aware — " +
                "350/500 (Grade B) for the UK, Ireland, Australia, New Zealand, and Canada, but 300/500 (Grade C+) " +
                "for the USA and Qatar — and that a country is mandatory before any pass/fail determination is made; " +
                "without one, the platform must surface an explicit `country_required` state rather than default " +
                "silently (EV-WRITE-010). Both the TypeScript (`lib/scoring.ts`) and .NET (`OetScoring`) " +
                "implementations of this rule are documented as behaviourally identical and covered by dedicated test " +
                "suites (72 and 98 assertions respectively) (EV-WRITE-010)."),
            new DocumentationSectionBlock(
                "Auditability",
                "Every AI call in the Writing pipeline, like every other AI-graded surface on the platform, is " +
                "required to route through a shared coordinator so that one `AiUsageRecord` is written per physical " +
                "provider call rather than left unaccounted for; the platform's engineering rules state this " +
                "explicitly as a standing invariant, not an optional log (EV-WRITE-011). The 21 September 2026 " +
                "benchmark investigation confirmed this usage table exists in production and holds measured tokens, " +
                "latency, and outcome per Writing-grade invocation, exportable read-only via " +
                "`GET /v1/admin/ai/usage` for later audit (EV-WRITE-005)."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-WRITE-001", DocumentationEvidenceType.DataKnowledge,
                "13 supported OET professions split into a tier-1 rulebook generation (351-361 rules) and a tier-2 generation (210-219 rules).",
                "AI-WRITING-GRADER-BENCHMARK-REPORT-21-Sep-2026.md, section 5.4"),
            new DocumentationEvidenceSeed("EV-WRITE-002", DocumentationEvidenceType.Code,
                "WritingCanonRule stores machine-checkable detection config, applicable letter types/professions and examples as JSONB; WritingCanonViolation links a submission to the specific rule that fired.",
                "backend/src/OetLearner.Api/Data/LearnerDbContext.WritingCanon.cs"),
            new DocumentationEvidenceSeed("EV-WRITE-003", DocumentationEvidenceType.DataKnowledge,
                "Four dated rounds of owner clinical-writing rulings (OA-01..OA-15, OA2-01..OA2-20, OA3-01..OA3-05, OA5-01..OA5-38, OA6-01..OA6-02) specifying checkable Writing behaviours.",
                "OET with Dr Hesham/_docs-center-wt/AGENTS.md, section \"OET Writing Model Answers — COMPULSORY\"; docs/WRITING-MODEL-ANSWER-RULES.md"),
            new DocumentationEvidenceSeed("EV-WRITE-004", DocumentationEvidenceType.Testing,
                "Standing rule that a validator reporting zero findings against a visible defect is a validator defect to fix (not a letter to reword), with every owner-flagged defect becoming a permanent regression fixture.",
                "AGENTS.md, \"OET Writing Model Answers — COMPULSORY\" (OA2-01 rule and WritingRev8RegressionFixtureTests.cs reference)"),
            new DocumentationEvidenceSeed("EV-WRITE-005", DocumentationEvidenceType.AiModel,
                "Traced production grading architecture: Anthropic claude-sonnet-5 via AiProviderRegistry, DB-resolved feature route, deterministic rule engine runs after the AI grade (not fed into the prompt), no enforced structured output, measured prompt sizes (~35k tokens tier-1, ~10.6k tier-2), AiUsageRecords export.",
                "AI-WRITING-GRADER-BENCHMARK-REPORT-21-Sep-2026.md, sections 3 and 7.1/7.5"),
            new DocumentationEvidenceSeed("EV-WRITE-006", DocumentationEvidenceType.DataKnowledge,
                "$0 hard rule: no paid AI API call for Writing model-answer authoring/repair/review; validator imports use includeSemantic:false; targeted-repair-only scope limit.",
                "AGENTS.md, \"OET Writing Model Answers — COMPULSORY\" ($0 hard rule and targeted-repair bullets)"),
            new DocumentationEvidenceSeed("EV-WRITE-007", DocumentationEvidenceType.Testing,
                "Calibration entities storing a named clinician's grade per letter (DrAhmedGradeJson), a dated per-model calibration run, and per-letter AI-vs-clinician absolute error.",
                "backend/src/OetLearner.Api/Data/LearnerDbContext.WritingCalibration.cs"),
            new DocumentationEvidenceSeed("EV-WRITE-008", DocumentationEvidenceType.Code,
                "WritingSubmission/WritingGrade/WritingScoreAppeal entities with idempotency-key and content-hash indexes preventing duplicate grading.",
                "backend/src/OetLearner.Api/Data/LearnerDbContext.WritingSubmissions.cs"),
            new DocumentationEvidenceSeed("EV-WRITE-009", DocumentationEvidenceType.Security,
                "Watermarked, on-demand (never stored) Writing attempt PDF with visible overlay, per-page footer, invisible last-page HMAC token, role-gated access (owner/expert/admin) and a 10/day rate limit.",
                "backend/src/OetLearner.Api/Services/WritingPdfService.cs; backend/src/OetLearner.Api/Endpoints/WritingPdfEndpoints.cs"),
            new DocumentationEvidenceSeed("EV-WRITE-010", DocumentationEvidenceType.DataKnowledge,
                "Canonical, country-aware Writing pass-mark specification (350/500 vs 300/500) with a mandatory country_required state and cross-language (TS/.NET) parity tests.",
                "docs/SCORING.md, sections \"Writing (country-dependent)\" and \"Canonical helpers\""),
            new DocumentationEvidenceSeed("EV-WRITE-011", DocumentationEvidenceType.DataKnowledge,
                "Standing invariant that all AI calls route through a shared coordinator with one AiUsageRecord per physical provider call.",
                "AGENTS.md, \"OET Domain Invariants\" (AI calls bullet, docs/AI-USAGE-POLICY.md)"),
        ]);
}
