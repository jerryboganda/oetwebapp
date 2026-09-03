# Writing backend / classification file manifest (focused, no full-site source)

Scope: files necessary to understand + test Writing task import → classification → persistence → serving → model answers.
Speaking parallel locations listed at the end (structure mirrors Writing; not packaged).

## Task / content models + schema
- `backend/src/OetLearner.Api/Domain/ContentPaperEntities.cs` — `ContentPaper` (SubtestCode=`writing`, ProfessionId/AppliesToAllProfessions, LetterType, CandidateVisible publish gate, ExtractedTextJson[`writingStructure`], Slug unique).
- `backend/src/OetLearner.Api/Domain/WritingScenarioEntities.cs` — `WritingScenario` (Title, LetterType max 8, Profession, SourceContentPaperId bridge, TaskPromptMarkdown/WriterRole/case-note fields), `WritingTaskModelAnswer` (1:1 per scenario, HeldForReview/Ready/Rejected + IsCandidateVisible), `WritingScenarioStructuredSentence`, `WritingScenarioEmbedding`.
- `backend/src/OetLearner.Api/Domain/WritingCaseNoteDrillEntities.cs`, `WritingDrillEntities.cs`, `WritingPathwayEntities.cs`, `WritingCoachEntities.cs`, `WritingCanonRuleEntities.cs`, `WritingShowcaseEntities.cs`, `WritingAssessmentV11Entities.cs` — drills/pathway/coach/canon/showcase/assessment surfaces.
- `backend/src/OetLearner.Api/Data/LearnerDbContext.WritingScenarios.cs`, `LearnerDbContext.WritingDrills.cs`, `LearnerDbContext.WritingCanon.cs`, `LearnerDbContext.WritingShowcase.cs`, `LearnerDbContext.WritingAssessmentV11.cs` — EF config + indexes (Profession,LetterType).
- `backend/src/OetLearner.Api/Data/Migrations/20261214090000_AddWritingTaskModelAnswer.cs` — model-answer table.

## Profession + Letter Type taxonomy (canonical, rule-based — no AI classifier)
- `backend/src/OetLearner.Api/Services/Content/WritingContentStructure.cs` — `CanonicalLetterTypes` = routine_referral, urgent_referral, non_medical_referral, update_discharge, update_referral_specialist_to_gp, transfer_letter; `Validate()` (publish gate), `BuildContentItemDetail()`, `BuildModelAnswerPayload()`, case-notes/model-answer builders.
- `backend/src/OetLearner.Api/Services/Content/ContentConventionParser.cs:114-122` — `LetterTypeRules` regexes (routine/urgent/non-medical/update-discharge/update-referral/transfer) + `CardTypeRules`; `TryClassify()` (Writing N folder → letterType, profession defaults medicine; Listening/Reading force AppliesToAll).
- `backend/src/OetLearner.Api/Services/Content/RealContentFolderImporter.cs:371-439` — `WritingLetterTypeMap` hint map + `ParseWritingGroup()` (Writing N folder → WritingPaper proposal, CaseNotes vs ModelAnswer role by filename); `SpeakingCardTypeMap`, rulebook/shared-resource parsing.
- `backend/src/OetLearner.Api/Services/Entitlements/PlanModulePolicy.cs` — recall/module gating (not classification, but controls Recalls visibility).
- `scripts/extract-writing-pdfs/Program.cs:23-31` — canonical Writing 1–6 folder→letter map used for the Tutor Book seed (medicine).

## Import / upload / ingestion
- `backend/src/OetLearner.Api/Services/Content/ContentBulkImportService.cs` — StagePayload → Commit (proposal → ContentPaper + MediaAsset + dedup + audit).
- `backend/src/OetLearner.Api/Services/Content/ContentPaperService.cs` — `CreateWritingTaskAsync`, `CreateAsync`, update/query, projection via `IWritingTaskProjectionService`.
- `backend/src/OetLearner.Api/Services/Content/RealContentFolderImporter.cs` — zip/folder → `RealContentProposal` list.
- `backend/src/OetLearner.Api/Endpoints/RealContentFolderImportEndpoints.cs`, `ContentBulkImportEndpoints.cs` (check Endpoints folder), `WritingTaskAdminEndpoints.cs` — admin upload/review/commit routes.
- `scripts/extract-writing-pdfs/` — one-shot PDF text extraction (PdfPig) for Writing 1–6 → `writing-samples.v1.json` (not currently present; regenerate as needed).
- `scripts/writing-qa-export.mjs` — reproducible QA export used for `02-writing-qa-export.csv` (this manifest's companion).

## Task-serving + model-answer pipeline
- `backend/src/OetLearner.Api/Services/Writing/WritingTaskProjectionService.cs` — ContentPaper → WritingScenario projection.
- `backend/src/OetLearner.Api/Services/Writing/WritingTaskAuthoringService.cs`, `WritingTaskCaseNotesService.cs`, `WritingTaskUnderstandingService.cs`, `WritingTaskModelAnswerService.cs` — authoring, case-notes, understanding, pre-generated model answers (1:1 reuse, never regenerated per submission).
- `backend/src/OetLearner.Api/Services/Writing/WritingSubmissionEvaluationPipeline.cs` (check Services/Writing) — grading reuses the pre-generated answer.
- `backend/src/OetLearner.Api/Endpoints/WritingTaskAdminEndpoints.cs`, `WritingTaskModelAnswerAdminEndpoints.cs` — admin review/approve (HeldForReview→Ready + IsCandidateVisible).
- `backend/src/OetLearner.Api/Prompts/Writing/WritingPromptTemplates.cs` — grading/feedback prompts (NOT classification; classification is rule-based).
- `docs/WRITING-CONTENT-DATA-ENTRY-ANALYSIS.md`, `docs/WRITING-MODULE-IMPLEMENTATION-LOOP.md` — data-entry + module loop notes.
- `rulebooks/` + `OET_Writing_Rulebook_FINAL.pdf` — assessment criteria source.

## Admin config
- Admin Writing Task Builder + publish gate (`Validate()` errors block publish; warnings allow).
- `ContentPaper.CandidateVisible` (Master Catalogue §5) — explicit visibility; test/demo rows ship false.
- Runtime settings via `IRuntimeSettingsProvider` (see `docs/ADMIN-RUNTIME-SETTINGS.md`).

## Speaking parallels (same pattern, different taxonomy)
- `Services/Content/SpeakingContentStructure.cs` (criteria/card-type helpers), `ContentConventionParser.CardTypeRules`, `RealContentFolderImporter.SpeakingCardTypeMap`, `Domain/Speaking*Entities.cs`, `Services/Speaking/*`, `Endpoints/Speaking*`, `Prompts/Speaking/*`.
