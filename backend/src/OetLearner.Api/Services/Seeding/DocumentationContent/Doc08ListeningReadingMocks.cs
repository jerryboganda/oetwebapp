using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-08 — Listening, Reading, Mock &amp; Recall Systems Report.</summary>
internal static class Doc08ListeningReadingMocks
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-08",
        Title: "Listening, Reading, Mock & Recall Systems Report",
        Description: "Practice and exam engines for OET Listening, Reading, full Mocks and vocabulary Recalls: timing, scoring, answer normalisation, transcripts, review, and the regression coverage behind them.",
        SortOrder: 8,
        Sections:
        [
            new DocumentationSectionBlock(
                "Listening — modes, state machine and scoring invariant",
                "Listening is delivered as a five-part finite-state-machine player (`ListeningFsmTransitions` + " +
                "`ListeningSessionService`) covering Parts A1, A2, B, C1 and C2, with a mode value on every attempt " +
                "(`Exam`, `Learning`, `Drill`, `MiniTest`, `ErrorBank`, `Home`, `Diagnostic`) that decides which UI " +
                "skin and lock behaviour apply; paper-based simulation is explicitly out of scope and legacy paper " +
                "query values fail closed to the computer-exam surface rather than being served (EV-LRM-001). Every " +
                "mode shares the same audio asset, the same transcript timing, and the same grading service, and the " +
                "scoring path carries a hard invariant stated verbatim in the module's own documentation: raw marks " +
                "are deterministic and stored on the attempt, while a scaled score and pass status come only from a " +
                "complete, owner-approved, versioned conversion table captured on that attempt — if the table is " +
                "unavailable the UI shows raw evidence and an explicit unavailable state, never a formula, " +
                "interpolation or synthetic fallback (EV-LRM-002). OET@Home mode is documented as a visual/guidance " +
                "layer only: fullscreen is optional and never blocks launch, progress or submission, and focus/" +
                "fullscreen changes are recorded as non-blocking telemetry rather than a learner-facing lock — the " +
                "documentation is explicit that this is not remote proctoring (camera, screen recording and ID " +
                "verification are named as separate, not-yet-built initiatives) (EV-LRM-003)."),
            new DocumentationSectionBlock(
                "Listening — Part A answer grading and structured miss classification",
                "The Part A short-answer grader (`ListeningGradingService`) runs a documented four-step pipeline: " +
                "build the canonical answer plus accepted synonym variants, normalise the user's answer per a " +
                "policy-selected strategy (`exact`, `trim_only`, or the default `trim_collapse_case_insensitive`; any " +
                "unrecognised profile fails closed to exact matching), match, and — only on a miss — classify why " +
                "(EV-LRM-004). Classification walks a fixed, prioritised order of six heuristics (`Empty`, " +
                "`WrongNumber`, `SpellingError`, `ExtraInfo`, `WrongSection`, `Paraphrase`) so that, for example, a " +
                "digit substitution is checked before a small-edit-distance spelling check, preventing a wrong " +
                "number from being misfiled as a typo; Levenshtein distance is used only inside this analytics " +
                "classification and never to award a mark itself (EV-LRM-005). The result is persisted on a " +
                "dedicated `MissReason` column rather than recomputed on every render, added by a named migration " +
                "(`20260521210000_AddListeningAnswerMissReason.cs`), specifically so the review page and per-paper " +
                "analytics can query it directly instead of re-running the classifier or parsing a free-text error " +
                "field; legacy rows graded before that migration carry `NULL` and the UI treats that explicitly as " +
                "\"no reason recorded\" rather than guessing one (EV-LRM-006). The canonical regression set for this " +
                "grader is `ListeningGraderMissReasonTests.cs`, run via `dotnet test ... --filter ListeningGrader` " +
                "(EV-LRM-007)."),
            new DocumentationSectionBlock(
                "Listening audio — a real, dated remediation programme with disclosed limitations",
                "The Listening audio catalogue has been through a documented, dated repair and verification effort " +
                "rather than a one-time upload. An audit dated 28 August 2026 segmented 20 legacy tests (100 audio " +
                "segments) by spoken semantic transition cue rather than a fixed timestamp, recording the exact A1/" +
                "A2/B/C1/C2 second-ranges and file sizes produced for every test (EV-LRM-008). A follow-on results " +
                "log dated 23 September 2026 documents a fleet-wide Atlas+Nova re-verification using an ASR-plus-" +
                "LLM-judge pipeline (\"JEV\", described as a TypeSafe SystemOne judgment over ASR transcripts) " +
                "layered with deterministic timing/similarity checks as hard gates, and states its own disclosure " +
                "rule directly: an uncertain or unavailable verdict is recorded as \"Needs manual review\", and " +
                "nothing is marked Verified without a content check (EV-LRM-009). Two specific defects are recorded " +
                "as found and fixed in that pass with root cause stated: Atlas Test 8's A2 section had been playing " +
                "back A1's audio because A1 and A2 pointed at one shared media asset, split at the word-exact " +
                "\"Extract Two\" cue; and Atlas Test 9's A2 preparation window had the wrong lead-in time because the " +
                "cue and its transition/preparation tail sat at the end of A1 instead of the head of A2 " +
                "(EV-LRM-010). The same results log discloses, rather than omits, that a number of Atlas papers " +
                "still carry a \"Needs manual review\" verdict on at least one section (the log names specific " +
                "Atlas Sample Tests where no \"Extract Two\" cue phrase could be automatically confirmed at the A2/" +
                "C2 head, requiring a human listen against the original recording) and that some sections were " +
                "graded outright \"Failed\" for tail-speech clipping at a boundary — this documentation pack states " +
                "that status as it stands rather than presenting the fleet as fully closed (EV-LRM-011)."),
            new DocumentationSectionBlock(
                "Reading — the zero-deviation contract for official papers",
                "Official Reading content is governed by a written \"zero-deviation contract\" that the repository " +
                "instructs every agent to read before touching a paper, and which states plainly: if one item cannot " +
                "be satisfied, stop — do not import, attach or publish a partial paper (EV-LRM-012). The contract's " +
                "substantive rules are precise and checkable: one official paper is Part A (20) + Part B (6) + Part " +
                "C (16) = 42 points; official papers are PDF-first with `texts: []` on every part (passages are " +
                "never OCR'd into HTML); the field carrying the marking key is named `correctAnswerJson`, and there " +
                "is deliberately no `correctAnswer` field; answers are taken only from the printed key in the source " +
                "PDF and are never invented, guessed, or copied from another paper; and the candidate-facing PDFs " +
                "are cropped into three distinct part-only files (Part A, Part B, Part C) with every answer-key page " +
                "dropped, because the candidate must never see the combined booklet or the key (EV-LRM-013). The " +
                "contract also records a specific, named data-integrity finding rather than a generic warning: one " +
                "paper's printed Part C key (\"JB2 C Q7-14\") was identified as pasted in from a different paper, and " +
                "the rule this produced is to stop and use passage evidence plus owner confirmation rather than " +
                "publish a key that does not match its own passage (EV-LRM-014)."),
            new DocumentationSectionBlock(
                "Reading — Part A layout detection instead of a hard-coded 1-7/8-14/15-20 split",
                "Official OET Reading Part A always has 20 items, but the three task blocks inside it move between " +
                "papers, so the platform detects the layout rather than assuming the classic split. The layout model " +
                "— shared, byte-for-byte in intent, between `lib/reading-part-a-layout.ts` (TypeScript) and " +
                "`ReadingPartALayout.cs` (C#) — expresses this as a matching-block end of 5, 6, 7 or 8, a last-block " +
                "start of 13, 14, 15 or 16, and a middle/last pair that swap between ShortAnswer and " +
                "SentenceCompletion, yielding eight valid official layouts (EV-LRM-015). Two independent detectors " +
                "exist: `detectPartALayoutFromBookletText`, which classifies the booklet's own instruction wording " +
                "(\"which text A-D\", \"complete each of the sentences\", \"answer each of the questions\") against " +
                "its numbered heading ranges, and a strict publish-time check, `detectPartALayoutFromQuestions`, " +
                "which requires a contiguous 1-20 set matching exactly one of the eight layouts and produces the " +
                "specific, named publish error \"Part A last block must start at question 13, 14, 15, or 16.\" if it " +
                "does not (EV-LRM-016). The save-and-upload playbook records that this is not a theoretical " +
                "edge case: Jayden Book papers JB1 through JB3 are cited as the concrete reason the 16-start layout " +
                "exists, since a validator still assuming the classic 15-20 split would reject them outright " +
                "(EV-LRM-017)."),
            new DocumentationSectionBlock(
                "Reading — scoring, folder routing and publish gates",
                "Reading scores on the same platform-wide anchor as Listening: 30/42 raw marks equal a scaled 350/" +
                "500, resolved only through `lib/scoring.ts` / `OetScoring` and never inlined as a formula elsewhere " +
                "(EV-LRM-018). A paper is not publishable until every question carries an explanation, an evidence " +
                "sentence and a `Published` review state, the paper itself carries a source-provenance record, and " +
                "one primary QuestionPaper PDF exists for each of Part A, B and C — publish status must be `4` " +
                "(Published), with no partial-draft papers left live (EV-LRM-019). Folder routing is contract-" +
                "enforced rather than left to naming convention: a paper's tag list and slug must include its series " +
                "token (for example `atlas-practice-series`, `nova-practice-series`) or it falls into an \"Other " +
                "papers\" bucket instead of its intended book folder, and the same five book folders must list a " +
                "paper on both the whole-paper `/reading/exam` route and the per-part `/reading/parts/a|b|c` routes " +
                "— those routes are explicitly never sent to `/mocks`, which is a separate surface with its own " +
                "entry point (EV-LRM-020)."),
            new DocumentationSectionBlock(
                "Full Mocks — deterministic randomisation with a disclosed, honest scope limit",
                "Full Mock delivery (`MockService`, `MockItemAnalysisService`, `Services/Mocks/**`) sits alongside " +
                "Listening and Reading as a first-subtest binding surface with its own entitlement service, and Full " +
                "Mock attempts are recorded as never consuming AI credits (EV-LRM-021). A documented, deliberately-" +
                "scoped randomisation helper, `RandomisationHelper.SeededShuffle`, provides a deterministic learner-" +
                "keyed Fisher-Yates shuffle backed by nine unit tests, and its own documentation states plainly what " +
                "it is not yet used for: it is safe today for mock-mode multiple-choice option ordering and Speaking " +
                "role-play card variants, where grading keys on option identity rather than position, but it is " +
                "explicitly not enabled for Reading question shuffling or graded Listening item ordering, because " +
                "those subtests' wire format still keys answers by letter/position rather than a stable option id — " +
                "enabling it there today would silently break grading, and the structural option-id migration this " +
                "depends on is tracked separately rather than implied to be done (EV-LRM-022)."),
            new DocumentationSectionBlock(
                "Recalls — versioned, idempotent vocabulary content pipeline",
                "The Recalls vocabulary bank is loaded by `RecallsContentSeeder`, documented in its own code comment " +
                "as \"Recalls Content Pack v1 (2026-05-05)\": an idempotent boot-time loader that hydrates " +
                "`VocabularyTerm` rows from versioned JSON files under `Data/SeedData/recalls/`, treating Git as the " +
                "source of truth so the content survives a container rebuild even if the database volume is wiped " +
                "(EV-LRM-023). The seeder upserts on `(Term, ExamTypeCode, ProfessionId)` and computes a SHA-256 " +
                "content hash per row so only rows whose content actually changed are written back to the database " +
                "on a re-seed; it deliberately never deletes a term that is removed from the JSON source, leaving " +
                "that as a manual admin archive action through `/admin/content/vocabulary`, and it skips (with a " +
                "logged warning, never a throw) any file that fails JSON schema validation (EV-LRM-024)."),
            new DocumentationSectionBlock(
                "Transcripts, review and cross-skill wiring",
                "Every Listening mode shares the same transcript timing data, and the post-submission review surface " +
                "replays that transcript against the learner's recorded evidence; highlight and strikethrough " +
                "annotations persist in any mode with a server-enforced 64 KB payload cap (EV-LRM-025). The review " +
                "page's miss-reason chip prefers the relational `MissReason` column described above and falls back " +
                "to a legacy free-form `errorType` field only for attempts graded before that column existed, so old " +
                "and new attempts render through the same UI helper without one code path silently going blank " +
                "(EV-LRM-026). Listening also binds directly into full Mocks as its first subtest, behind an audio-" +
                "readiness gate on the mock player, and into the platform's Teacher Classes / pathway-recommendation " +
                "surfaces documented alongside the module's domain entities."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-LRM-001", DocumentationEvidenceType.ProductUi,
                "Listening delivery modes (Exam/Learning/Drill/MiniTest/ErrorBank/Home/Diagnostic); paper-based simulation out of scope.",
                "docs/listening/exam-modes.md"),
            new DocumentationEvidenceSeed("EV-LRM-002", DocumentationEvidenceType.DataKnowledge,
                "Scoring invariant: raw marks deterministic; scaled score/pass status only from a versioned owner-approved conversion table, never a formula fallback.",
                "docs/listening/README.md, \"Scoring path — invariant\" section"),
            new DocumentationEvidenceSeed("EV-LRM-003", DocumentationEvidenceType.Security,
                "OET@Home is a non-blocking visual guidance layer; explicitly not remote proctoring.",
                "docs/listening/exam-modes.md, \"OET@Home specifics\" section"),
            new DocumentationEvidenceSeed("EV-LRM-004", DocumentationEvidenceType.Code,
                "Part A grading pipeline: build candidates, normalise, match, classify miss.",
                "backend/src/OetLearner.Api/Services/Listening/ListeningGradingService.cs; docs/listening/grader.md"),
            new DocumentationEvidenceSeed("EV-LRM-005", DocumentationEvidenceType.Code,
                "Six-heuristic prioritised MissReason classification (Empty, WrongNumber, SpellingError, ExtraInfo, WrongSection, Paraphrase).",
                "docs/listening/grader.md, \"MissReason heuristics\" table"),
            new DocumentationEvidenceSeed("EV-LRM-006", DocumentationEvidenceType.Code,
                "MissReason persisted at grade time via a dedicated migration; legacy NULL rows shown as \"no reason recorded\".",
                "backend/src/OetLearner.Api/Data/Migrations/20260521210000_AddListeningAnswerMissReason.cs; docs/listening/grader.md"),
            new DocumentationEvidenceSeed("EV-LRM-007", DocumentationEvidenceType.Testing,
                "Canonical Listening grader regression fixture.",
                "backend/tests/OetLearner.Api.Tests/Listening/ListeningGraderMissReasonTests.cs"),
            new DocumentationEvidenceSeed("EV-LRM-008", DocumentationEvidenceType.Testing,
                "Dated audio segmentation audit: 20 tests, 100 segments, exact per-section second-ranges and file sizes.",
                "docs/listening/audio-split-audit-report.md (generated 2026-08-28)"),
            new DocumentationEvidenceSeed("EV-LRM-009", DocumentationEvidenceType.Testing,
                "Fleet-wide ASR+LLM-judge audio verification pass with an explicit \"needs manual review over guessing\" disclosure rule.",
                "docs/listening/atlas-nova-issue-log-results.md (23 September 2026)"),
            new DocumentationEvidenceSeed("EV-LRM-010", DocumentationEvidenceType.Testing,
                "Two named, root-caused audio defects fixed: Atlas Test 8 A2-replays-A1 shared-asset bug; Atlas Test 9 A2 preparation-window mis-timing.",
                "docs/listening/atlas-nova-issue-log-results.md, executive summary, Issues 1-2"),
            new DocumentationEvidenceSeed("EV-LRM-011", DocumentationEvidenceType.Testing,
                "Disclosed residual verification gaps: papers/sections still marked \"Needs manual review\" or \"Failed\" after the fleet audit.",
                "docs/listening/atlas-nova-issue-log-results.md, results table"),
            new DocumentationEvidenceSeed("EV-LRM-012", DocumentationEvidenceType.DataKnowledge,
                "Zero-deviation contract: stop rather than publish a partial or non-conforming Reading paper.",
                "docs/READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md"),
            new DocumentationEvidenceSeed("EV-LRM-013", DocumentationEvidenceType.DataKnowledge,
                "Reading paper structure rules: 20/6/16=42, PDF-first with texts:[], correctAnswerJson field, part-only cropped PDFs, no answer-key pages.",
                "docs/READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md, sections A-B"),
            new DocumentationEvidenceSeed("EV-LRM-014", DocumentationEvidenceType.DataKnowledge,
                "Named data-integrity finding: a printed Part C key pasted from another paper (JB2 C Q7-14), requiring passage evidence and owner confirmation before publish.",
                "docs/READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md, item A7"),
            new DocumentationEvidenceSeed("EV-LRM-015", DocumentationEvidenceType.Code,
                "Part A layout model: matching end in {5,6,7,8}, last-block start in {13,14,15,16}, eight valid official layouts.",
                "lib/reading-part-a-layout.ts"),
            new DocumentationEvidenceSeed("EV-LRM-016", DocumentationEvidenceType.Code,
                "Booklet-text and strict question-based Part A layout detectors, including the named publish-time error message.",
                "lib/reading-part-a-layout.ts (detectPartALayoutFromBookletText, detectPartALayoutFromQuestions)"),
            new DocumentationEvidenceSeed("EV-LRM-017", DocumentationEvidenceType.DataKnowledge,
                "Jayden Book JB1-JB3 cited as the real papers requiring the 16-start Part A layout.",
                "docs/READING-MODULE-SAVE-AND-UPLOAD.md, section 2.2"),
            new DocumentationEvidenceSeed("EV-LRM-018", DocumentationEvidenceType.DataKnowledge,
                "Reading/Listening scoring anchor 30/42 == 350/500, resolved only via lib/scoring.ts / OetScoring.",
                "AGENTS.md, \"OET Domain Invariants\" section; docs/READING-MODULE-SAVE-AND-UPLOAD.md section 1"),
            new DocumentationEvidenceSeed("EV-LRM-019", DocumentationEvidenceType.DataKnowledge,
                "Reading publish gates: explanation, evidence, Published review state, source provenance, one primary PDF per part, status=4.",
                "docs/READING-MODULE-SAVE-AND-UPLOAD.md, section 1"),
            new DocumentationEvidenceSeed("EV-LRM-020", DocumentationEvidenceType.ProductUi,
                "Folder-token routing rule and shared book-folder listing across /reading/exam and /reading/parts/a|b|c, distinct from /mocks.",
                "docs/READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md, section C"),
            new DocumentationEvidenceSeed("EV-LRM-021", DocumentationEvidenceType.Code,
                "Full Mock services and entitlement service; Full Mock attempts never consume AI credits.",
                "docs/ai-learning-companion/REPO_GAP_ANALYSIS.md, F-072/F-074 row"),
            new DocumentationEvidenceSeed("EV-LRM-022", DocumentationEvidenceType.Testing,
                "Deterministic seeded-shuffle helper with 9 unit tests; documented as not yet safe for graded Reading/Listening item ordering pending an option-id migration.",
                "docs/MOCKS-RANDOMISATION.md; backend/src/OetLearner.Api/Services/RandomisationHelper.cs; backend/tests/OetLearner.Api.Tests/RandomisationHelperTests.cs"),
            new DocumentationEvidenceSeed("EV-LRM-023", DocumentationEvidenceType.Code,
                "Idempotent, Git-sourced Recalls vocabulary seeder, versioned as Content Pack v1 (2026-05-05).",
                "backend/src/OetLearner.Api/Services/Recalls/RecallsContentSeeder.cs"),
            new DocumentationEvidenceSeed("EV-LRM-024", DocumentationEvidenceType.Code,
                "Upsert key (Term, ExamTypeCode, ProfessionId), SHA-256 change detection, never-delete policy, schema-validation skip-with-warning.",
                "backend/src/OetLearner.Api/Services/Recalls/RecallsContentSeeder.cs"),
            new DocumentationEvidenceSeed("EV-LRM-025", DocumentationEvidenceType.ProductUi,
                "Shared transcript timing and annotation persistence (highlight/strikethrough) with a 64 KB server-side cap.",
                "docs/listening/exam-modes.md, \"What the modes share\" section; hooks/use-listening-annotations.ts"),
            new DocumentationEvidenceSeed("EV-LRM-026", DocumentationEvidenceType.Code,
                "Review page prefers relational MissReason and falls back to legacy errorType for pre-migration attempts.",
                "docs/listening/grader.md, \"Cross-references\" section; app/listening/review/[id]/page.tsx"),
        ],
        Revision: 2);
}
