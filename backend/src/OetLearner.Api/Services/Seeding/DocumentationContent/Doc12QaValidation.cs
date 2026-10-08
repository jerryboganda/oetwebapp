using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-12 — QA, Validation, Reliability &amp; Safety Report.</summary>
internal static class Doc12QaValidation
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-12",
        Title: "QA, Validation, Reliability & Safety Report",
        Description: "Automated testing, golden benchmark sets, regression coverage, adversarial/boundary tests, browser and device compatibility, and the real release gate that governs what reaches production.",
        SortOrder: 12,
        Sections:
        [
            new DocumentationSectionBlock(
                "Automated test code (historical; deleted 8 Oct 2026)",
                "The backend test project contained 569 test files until 8 Oct 2026, when all test code was deleted " +
                "by owner directive. CI now runs only the language checks (typecheck and lint) and compiles the " +
                "images; the owner tests the live product by hand and reports bugs. The earlier sharded `qa-smoke.yml` " +
                "run is gone with the tests; the last commit that still contained them is tagged " +
                "`last-commit-with-tests`."),
            new DocumentationSectionBlock(
                "Regression fixtures tied to specific, dated owner rulings",
                "A significant share of the Writing test surface is not generic unit testing but named regression " +
                "fixtures written against specific, dated clinical-writing rulings, so that a later change cannot " +
                "silently reintroduce a defect an owner already flagged. Confirmed files under `backend/tests/" +
                "OetLearner.Api.Tests/Writing/` include `WritingRev8RegressionFixtureTests.cs`, " +
                "`WritingOwnerAddendumTwoRegressionFixtureTests.cs`, `WritingOwnerClarificationsThreeRegressionFixtureTests.cs`, " +
                "`WritingPatientRequestMarkerRegressionTests.cs`, `WritingCrossModelAuditRegressionTests.cs`, " +
                "`WritingSeniorAuditRound2RegressionTests.cs`, `WritingUltimateFinalRegressionFixtureTests.cs`, and " +
                "seven further files named `WritingSeniorAuditG1RegressionTests.cs` through `...G7RegressionTests.cs` " +
                "— one file per detector group added during a documented senior-assessor release audit " +
                "(EV-QA-003). The repository's own governance layer states the rule these enforce directly: every " +
                "owner-flagged defect type becomes a permanent injection test in the relevant fixture before further " +
                "letters are written, and if correct clinical wording exposes a validator weakness, the validator is " +
                "fixed rather than the letter reworded to dodge the check (EV-QA-004). " +
                "IMPORTANT, corrected 8 Oct 2026: the deploy pipeline no longer runs any test gate. Until 8 Oct 2026 " +
                "`writing-model-answer-gate` in `build-images.yml` ran `dotnet test ... --filter " +
                "'FullyQualifiedName~WritingRev8ModelAnswerGateTests'` and blocked the build on failure; that job was " +
                "removed together with all test code, so the writing rule pack is now enforced only when a human runs " +
                "the validator. EV-QA-005 below records the historical state, not a current guarantee."),
            new DocumentationSectionBlock(
                "Adversarial and security boundary tests (historical; deleted 8 Oct 2026)",
                "Before the test code was deleted, the suite included named adversarial and security-boundary " +
                "test classes covering scoring integrity and the AI Learning Companion specifically. " +
                "`M2AdversarialChallengerTests.cs` and `M2AdversarialScoringAndAiGatewayTests.cs` targeted score-" +
                "conversion tables and objective-scoring paths for adversarial inputs (EV-QA-006). For the companion, " +
                "the classes that existed were `CompanionRetrievalSecurityTests`, `CompanionOutputGuardTests`, " +
                "`CompanionDestinationSecurityTests`, `CompanionLearnerToolBoundaryTests`, `CompanionExamModeTests`, " +
                "`CompanionMemoryIsolationTests`, `CompanionMultiLearnerIsolationTests`, `CompanionPackageScopeTests`, " +
                "`CompanionVersionPrecedenceTests`, `CompanionContaminationAuditTests`, `CompanionCorpusGuardTests` " +
                "and `CompanionKillSwitchTests` (EV-QA-007). Stated plainly: no file in that suite was named for " +
                "classic \"prompt injection\" phrasing specifically. The functionally equivalent protections remain in " +
                "the product — entitlement-before-retrieval filtering, an output-side leak/canary screen, " +
                "cross-learner memory isolation, and exam-mode boundary enforcement — but with the tests gone they " +
                "are no longer regression-guarded, and only the manual UAT packs exercise them."),
            new DocumentationSectionBlock(
                "Golden benchmark sets for AI-graded and AI-assisted features",
                "Versioned golden benchmark corpora exist under `docs/benchmarks/` as JSON files, each declaring a " +
                "`corpusVersion`, a `class` (`scoring-critical` or `non-scoring`), the exact `featureCode` and " +
                "`promptVersion`/`rulebookVersion` it targets, and a list of stimulus items with expected outcomes " +
                "(EV-QA-008). Six corpora are confirmed present: `writing-grading.v1` (class `scoring-critical`, " +
                "feature `writing.grade`, items carrying full five-criterion expected scores), `speaking-" +
                "assessment.v1` (`scoring-critical`, feature `speaking.grade`), and four `non-scoring` corpora — " +
                "`admin-drafting.v1`, `admin-extraction.v1` (Listening Part A extraction), `explanations.v1` " +
                "(Reading explanation generation) and `patient-turns.v1` (Speaking role-play patient turns) " +
                "(EV-QA-009). This documentation pack does not state a pass rate, item count trend, or hallucination " +
                "rate for these corpora beyond what is written in the files themselves, per this pack's own rule " +
                "against inventing a figure that was not directly observed."),
            new DocumentationSectionBlock(
                "Browser, device and accessibility coverage — stated with its real depth and gaps",
                "The repository's own QA documentation (`docs/qa/test-coverage-map.md` and `docs/qa/release-" +
                "readiness.md`) is written as an honest coverage map rather than a pass/fail badge, using four " +
                "explicit tiers — \"Automated strong\", \"Automated smoke\", \"Manual-style verified\" and \"Gap\" " +
                "(EV-QA-010). Per that map: learner Reading/Listening/Writing/Speaking immersive player completion, " +
                "expert Writing and Speaking review-completion workspaces, and admin content-publish/audit-log/user-" +
                "mutation flows are recorded \"Automated strong\" but specifically in Chromium, with cross-browser " +
                "(Firefox/WebKit) deep-mutation parity named as a remaining gap rather than assumed covered " +
                "(EV-QA-011). Responsive coverage runs Pixel 7 and iPhone 14 Playwright projects at smoke depth; " +
                "cross-browser desktop coverage (Chromium/Firefox/WebKit) is smoke-level outside the audited " +
                "Chromium mutation paths; and a Sydney-timezone learner project covers locale/timezone sensitivity at " +
                "a limited depth (EV-QA-012). Accessibility coverage includes an automated axe smoke pass plus " +
                "keyboard/focus regression tests on dialogs and drawers, but the release-readiness document states " +
                "directly that manual assistive-technology signoff (NVDA on Windows; VoiceOver on macOS/iOS, per a " +
                "named execution checklist) was, as of that document, still pending external human execution — this " +
                "is recorded as an open item, not implied to be complete (EV-QA-013)."),
            new DocumentationSectionBlock(
                "The real production release gate, and a disclosed CI limitation",
                "The repository runs two separate GitHub Actions workflows that are easy to conflate but serve " +
                "different purposes, and its own always-loaded agent contract is explicit about which one actually " +
                "gates production: `qa-smoke.yml` (\"QA Smoke\") runs placement-entry contract checks, the six-shard " +
                "backend test matrix and a frontend unit/lint/typecheck/build job. It is deliberately not " +
                "blocked on for a routine change (EV-QA-014). The workflow that actually gates what reaches " +
                "production is the separate `build-images.yml` + `production-deploy.yml` pipeline: it runs the syntax gate " +
                "described above, then builds the web and API images off-box on GitHub-hosted runners, pushes them " +
                "to GHCR, and only then triggers a health-gated blue/green deploy on the production VPS — the " +
                "workflow's own top-of-file comment states the reason for building off-box directly: the VPS is a " +
                "shared host with 60+ co-tenant containers, and an in-place `next build` can OOM-cascade and take " +
                "the whole box down (EV-QA-015). A commit that fails the deploy workflow's health gate is not " +
                "promoted, so a broken build cannot silently reach learners; a commit that merely fails the flaky QA " +
                "Smoke workflow is not treated as blocked by that fact alone. This is stated here as a known, " +
                "disclosed limitation of the CI setup rather than hidden: QA Smoke's own release-readiness " +
                "documentation separately records that a fully green, GitHub-hosted observed run of QA Smoke itself " +
                "was, at the time that document was last updated, still a pending, unobserved verification step " +
                "(EV-QA-016)."),
            new DocumentationSectionBlock(
                "Reliability: rollback, health gates and data-protection guards",
                "The deploy workflow's blue/green model is a reliability control, not just a deployment mechanic: a " +
                "failed health check on the new slot means the old slot keeps serving traffic, and the production " +
                "storage volumes (`oetwebsite_oet_postgres_data`, `oetwebsite_oet_learner_storage`, " +
                "`oetwebsite_oet_db_backups`, `oetwebsite_oet_clamav_data`) are named Docker volumes independent of " +
                "the web/API containers, so rebuilding or recreating a container does not delete learner data " +
                "(EV-QA-017). The production installation runs `scripts/deploy/protect-production-data.sh` " +
                "specifically to block destructive volume commands (`docker compose down -v`, `docker volume rm`, " +
                "`volume prune`) at the shell level on the VPS itself, so content removal is only possible through " +
                "the admin UI rather than an operator's shell session (EV-QA-018). The deploy workflow additionally " +
                "runs an automated \"assert production rollout is image-only\" and \"verify compute offload\" check " +
                "(`scripts/deploy/verify-image-only-rollout.sh`, `scripts/deploy/verify-compute-offload.sh`) before " +
                "building, enforcing in CI itself that heavy computation happens on GitHub's runners and the VPS " +
                "only pulls and runs prebuilt images (EV-QA-019)."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-QA-001", DocumentationEvidenceType.Testing,
                "569 backend test files, sharded six ways in CI because test-class parallelisation is disabled within a process.",
                "backend/tests/OetLearner.Api.Tests/ (file count); .github/workflows/qa-smoke.yml, backend-tests job comments"),
            new DocumentationEvidenceSeed("EV-QA-002", DocumentationEvidenceType.Reliability,
                "pgvector-enabled Postgres service container in CI, fixing a documented vector(1536) type-mapping failure affecting every Postgres-backed test.",
                ".github/workflows/qa-smoke.yml, backend-tests job (postgres service, pgvector/pgvector:pg17)"),
            new DocumentationEvidenceSeed("EV-QA-003", DocumentationEvidenceType.Testing,
                "Named regression fixtures tied to dated owner clinical-writing rulings, including a 7-file senior-assessor-audit detector-group series.",
                "backend/tests/OetLearner.Api.Tests/Writing/ (WritingRev8RegressionFixtureTests.cs, WritingOwnerAddendumTwoRegressionFixtureTests.cs, WritingOwnerClarificationsThreeRegressionFixtureTests.cs, WritingSeniorAuditG1RegressionTests.cs .. WritingSeniorAuditG7RegressionTests.cs, WritingCrossModelAuditRegressionTests.cs, WritingUltimateFinalRegressionFixtureTests.cs)"),
            new DocumentationEvidenceSeed("EV-QA-004", DocumentationEvidenceType.DataKnowledge,
                "Standing rule: every owner-flagged defect type becomes a permanent regression test; fix the validator, never reword the letter to dodge it.",
                "AGENTS.md, \"OET Writing Model Answers — COMPULSORY\" section"),
            new DocumentationEvidenceSeed("EV-QA-005", DocumentationEvidenceType.Reliability,
                "Deploy pipeline hard gate running the Writing model-answer regression fixture before any image is built.",
                ".github/workflows/build-images.yml, writing-model-answer-gate job (WritingRev8ModelAnswerGateTests filter)"),
            new DocumentationEvidenceSeed("EV-QA-006", DocumentationEvidenceType.Testing,
                "Adversarial tests targeting score-conversion tables and objective scoring / AI gateway paths.",
                "backend/tests/OetLearner.Api.Tests/Assessment/M2AdversarialChallengerTests.cs; M2AdversarialScoringAndAiGatewayTests.cs"),
            new DocumentationEvidenceSeed("EV-QA-007", DocumentationEvidenceType.Security,
                "Named companion security/boundary regression classes covering retrieval, output, destinations, tool boundaries, exam mode, memory isolation, package scope, version precedence, and corpus contamination.",
                "backend/tests/OetLearner.Api.Tests/Companion/CompanionRetrievalSecurityTests.cs, CompanionOutputGuardTests.cs, CompanionDestinationSecurityTests.cs, CompanionLearnerToolBoundaryTests.cs, CompanionExamModeTests.cs, CompanionMemoryIsolationTests.cs, CompanionMultiLearnerIsolationTests.cs, CompanionPackageScopeTests.cs, CompanionVersionPrecedenceTests.cs, CompanionContaminationAuditTests.cs, CompanionCorpusGuardTests.cs, CompanionKillSwitchTests.cs"),
            new DocumentationEvidenceSeed("EV-QA-008", DocumentationEvidenceType.Testing,
                "Versioned golden benchmark JSON format: corpusVersion, class, featureCode, promptVersion, rulebookVersion, items.",
                "docs/benchmarks/writing-grading.v1.json"),
            new DocumentationEvidenceSeed("EV-QA-009", DocumentationEvidenceType.Testing,
                "Six confirmed golden benchmark corpora across Writing, Speaking, Listening extraction, Reading explanations and admin drafting.",
                "docs/benchmarks/writing-grading.v1.json, speaking-assessment.v1.json, admin-drafting.v1.json, admin-extraction.v1.json, explanations.v1.json, patient-turns.v1.json"),
            new DocumentationEvidenceSeed("EV-QA-010", DocumentationEvidenceType.Testing,
                "Four-tier honest coverage legend (Automated strong / Automated smoke / Manual-style verified / Gap).",
                "docs/qa/test-coverage-map.md, \"Coverage Legend\" section"),
            new DocumentationEvidenceSeed("EV-QA-011", DocumentationEvidenceType.Testing,
                "Chromium-strong, cross-browser-gap coverage for learner immersive players and expert/admin mutation workflows.",
                "docs/qa/test-coverage-map.md, coverage table rows for learner players and expert/admin surfaces"),
            new DocumentationEvidenceSeed("EV-QA-012", DocumentationEvidenceType.Testing,
                "Responsive (Pixel 7 / iPhone 14), cross-browser desktop, and locale/timezone (Sydney) smoke-level coverage.",
                "docs/qa/test-coverage-map.md, \"Responsive learner surfaces\", \"Cross-browser desktop\", \"Locale/timezone sensitivity\" rows"),
            new DocumentationEvidenceSeed("EV-QA-013", DocumentationEvidenceType.Testing,
                "Manual assistive-technology (NVDA/VoiceOver) signoff recorded as pending external execution, not complete.",
                "docs/qa/release-readiness.md, \"External Signoff Gates\" section"),
            new DocumentationEvidenceSeed("EV-QA-014", DocumentationEvidenceType.Reliability,
                "QA Smoke workflow kept to unit + backend evidence and deliberately not blocked on for routine changes; the Playwright/e2e matrix was removed from CI by owner directive 2026-10-03 (no automated e2e; bugs are reported by the owner and fixed on demand).",
                "AGENTS.md, \"Ship-It Workflow — COMPULSORY\" section, step 2; .github/workflows/qa-smoke.yml"),
            new DocumentationEvidenceSeed("EV-QA-015", DocumentationEvidenceType.Reliability,
                "The real production gate is the separate Build images + Deploy production pipeline: off-box image build, GHCR push, health-gated blue/green VPS deploy.",
                ".github/workflows/build-images.yml (top-of-file comment; syntax-gate, build-web jobs)"),
            new DocumentationEvidenceSeed("EV-QA-016", DocumentationEvidenceType.Reliability,
                "A fully observed, green GitHub-hosted QA Smoke run recorded as still pending external observation as of the release-readiness document.",
                "docs/qa/release-readiness.md, \"GitHub-Hosted QA Smoke Observation\" section"),
            new DocumentationEvidenceSeed("EV-QA-017", DocumentationEvidenceType.Reliability,
                "Blue/green health-gated deploy; named persistent Docker volumes independent of web/API containers.",
                "AGENTS.md, \"Storage Persistence\" section; .github/workflows/build-images.yml"),
            new DocumentationEvidenceSeed("EV-QA-018", DocumentationEvidenceType.Reliability,
                "Production script blocking destructive volume commands at the shell level; content removal only via admin UI.",
                "AGENTS.md, \"Storage Persistence\" section (scripts/deploy/protect-production-data.sh)"),
            new DocumentationEvidenceSeed("EV-QA-019", DocumentationEvidenceType.Reliability,
                "CI-enforced image-only rollout and compute-offload verification before any image build.",
                ".github/workflows/build-images.yml, build-web job (verify-image-only-rollout.sh, verify-compute-offload.sh)"),
        ]);
}
