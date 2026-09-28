using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
    // ── Content Packages (reference data) ──

    private static void SeedContentPackages(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.ContentPackages.AddRange(
            new ContentPackage
            {
                Id = "pkg-full-en-2026", Code = "full-en-2026", Title = "Full OET Course 2026 (English)",
                Description = "Comprehensive OET preparation covering all four subtests with structured modules, practice tasks, and model answers.",
                PackageType = "full_course", InstructionLanguage = "en", BillingPlanId = "premium-monthly",
                Status = ContentStatus.Published, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-full-ar-nursing-2026", Code = "full-ar-nursing-2026", Title = "Full OET Nursing Course 2026 (Arabic)",
                Description = "Complete OET Nursing course with Arabic instruction. All subtests covered with profession-specific materials.",
                PackageType = "full_course", ProfessionId = "nursing", InstructionLanguage = "ar",
                BillingPlanId = "premium-monthly", Status = ContentStatus.Published, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-full-ar-medicine-2026", Code = "full-ar-medicine-2026", Title = "Full OET Medicine Course 2026 (Arabic)",
                Description = "Complete OET Medicine course with Arabic instruction for doctors.",
                PackageType = "full_course", ProfessionId = "medicine", InstructionLanguage = "ar",
                BillingPlanId = "premium-monthly", Status = ContentStatus.Published, DisplayOrder = 3, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-crash-en-general", Code = "crash-en-general", Title = "OET Crash Course (English)",
                Description = "Intensive short-format OET preparation covering key strategies and high-impact practice across all subtests.",
                PackageType = "crash_course", InstructionLanguage = "en", BillingPlanId = "basic-monthly",
                Status = ContentStatus.Published, DisplayOrder = 4, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-crash-ar-general", Code = "crash-ar-general", Title = "OET Crash Course (Arabic)",
                Description = "Condensed OET preparation with Arabic instruction.",
                PackageType = "crash_course", InstructionLanguage = "ar", BillingPlanId = "basic-monthly",
                Status = ContentStatus.Published, DisplayOrder = 5, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-crash-en-pharmacy", Code = "crash-en-pharmacy", Title = "OET Pharmacy Crash Course (English)",
                Description = "Pharmacy-focused OET crash course with profession-specific tasks and strategies.",
                PackageType = "crash_course", ProfessionId = "pharmacy", InstructionLanguage = "en",
                BillingPlanId = "basic-monthly", Status = ContentStatus.Published, DisplayOrder = 6, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-combo-lr-recalls", Code = "combo-lr-recalls", Title = "Listening, Reading & Recalls Combo",
                Description = "Combined package for Listening and Reading practice including recent recall materials.",
                PackageType = "combo", InstructionLanguage = "en", BillingPlanId = "basic-monthly",
                Status = ContentStatus.Published, DisplayOrder = 7, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentPackage
            {
                Id = "pkg-foundation-basic-en", Code = "foundation-basic-en", Title = "Basic English for OET",
                Description = "Foundation English course to build core language skills before starting OET-specific preparation.",
                PackageType = "foundation", InstructionLanguage = "en",
                Status = ContentStatus.Published, DisplayOrder = 8, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            }
        );

        // Package content rules linking packages to programs
        db.PackageContentRules.AddRange(
            new PackageContentRule { Id = "pcr-001", PackageId = "pkg-full-en-2026", RuleType = "include_program", TargetId = "prg-full-en-2026", TargetType = "program" },
            new PackageContentRule { Id = "pcr-002", PackageId = "pkg-full-ar-nursing-2026", RuleType = "include_program", TargetId = "prg-full-ar-nursing-2026", TargetType = "program" },
            new PackageContentRule { Id = "pcr-003", PackageId = "pkg-full-ar-medicine-2026", RuleType = "include_program", TargetId = "prg-full-ar-medicine-2026", TargetType = "program" },
            new PackageContentRule { Id = "pcr-004", PackageId = "pkg-crash-en-general", RuleType = "include_program", TargetId = "prg-crash-en-general", TargetType = "program" },
            new PackageContentRule { Id = "pcr-005", PackageId = "pkg-crash-ar-general", RuleType = "include_program", TargetId = "prg-crash-ar-general", TargetType = "program" },
            new PackageContentRule { Id = "pcr-006", PackageId = "pkg-crash-en-pharmacy", RuleType = "include_program", TargetId = "prg-crash-en-pharmacy", TargetType = "program" },
            new PackageContentRule { Id = "pcr-007", PackageId = "pkg-foundation-basic-en", RuleType = "include_program", TargetId = "prg-foundation-basic-en", TargetType = "program" }
        );
    }

    // ── Content Programs with Tracks and Modules (reference data) ──

    private static void SeedContentPrograms(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        // ── Full English OET Course 2026 ──
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prg-full-en-2026", Code = "full-en-2026", Title = "Full OET Online Course 2026",
            Description = "Comprehensive OET preparation covering Writing, Speaking, Reading, and Listening with structured progression.",
            InstructionLanguage = "en", ProgramType = "full_course", Status = ContentStatus.Published,
            DisplayOrder = 1, EstimatedDurationMinutes = 4800, CreatedAt = now, UpdatedAt = now, PublishedAt = now
        });

        db.ContentTracks.AddRange(
            new ContentTrack { Id = "trk-full-en-writing", ProgramId = "prg-full-en-2026", SubtestCode = "writing", Title = "Writing Track", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-full-en-speaking", ProgramId = "prg-full-en-2026", SubtestCode = "speaking", Title = "Speaking Track", DisplayOrder = 2, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-full-en-reading", ProgramId = "prg-full-en-2026", SubtestCode = "reading", Title = "Reading Track", DisplayOrder = 3, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-full-en-listening", ProgramId = "prg-full-en-2026", SubtestCode = "listening", Title = "Listening Track", DisplayOrder = 4, Status = ContentStatus.Published }
        );

        db.ContentModules.AddRange(
            new ContentModule { Id = "mod-full-en-wr-01", TrackId = "trk-full-en-writing", Title = "Writing Fundamentals", Description = "Core OET writing skills: purpose, structure, and register.", DisplayOrder = 1, EstimatedDurationMinutes = 120, Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-wr-02", TrackId = "trk-full-en-writing", Title = "Case Notes & Task Analysis", Description = "Extracting relevant information from case notes.", DisplayOrder = 2, EstimatedDurationMinutes = 90, PrerequisiteModuleId = "mod-full-en-wr-01", Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-wr-03", TrackId = "trk-full-en-writing", Title = "Model Answers & Criteria Deep-Dive", Description = "Understanding scoring criteria through model answer analysis.", DisplayOrder = 3, EstimatedDurationMinutes = 120, PrerequisiteModuleId = "mod-full-en-wr-02", Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-sp-01", TrackId = "trk-full-en-speaking", Title = "Speaking Fundamentals", Description = "OET speaking format, role card analysis, and clinical communication.", DisplayOrder = 1, EstimatedDurationMinutes = 90, Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-sp-02", TrackId = "trk-full-en-speaking", Title = "Role Play Practice", Description = "Structured practice with common OET speaking scenarios.", DisplayOrder = 2, EstimatedDurationMinutes = 120, PrerequisiteModuleId = "mod-full-en-sp-01", Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-rd-01", TrackId = "trk-full-en-reading", Title = "Reading Parts A, B & C Strategies", Description = "Strategies and practice for all three reading parts.", DisplayOrder = 1, EstimatedDurationMinutes = 120, Status = ContentStatus.Published },
            new ContentModule { Id = "mod-full-en-lt-01", TrackId = "trk-full-en-listening", Title = "Listening Parts A, B & C Strategies", Description = "Strategies and practice for all three listening parts.", DisplayOrder = 1, EstimatedDurationMinutes = 120, Status = ContentStatus.Published }
        );

        // Wire existing demo content items into lessons
        db.ContentLessons.AddRange(
            new ContentLesson { Id = "lsn-wr-01-task", ModuleId = "mod-full-en-wr-01", ContentItemId = "wt-001", Title = "Practice: Discharge Summary", LessonType = "practice_task", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentLesson { Id = "lsn-wr-02-task", ModuleId = "mod-full-en-wr-02", ContentItemId = "wt-002", Title = "Practice: Referral Letter", LessonType = "practice_task", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentLesson { Id = "lsn-sp-01-task", ModuleId = "mod-full-en-sp-02", ContentItemId = "st-001", Title = "Practice: Patient Handover", LessonType = "practice_task", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentLesson { Id = "lsn-sp-02-task", ModuleId = "mod-full-en-sp-02", ContentItemId = "st-002", Title = "Practice: Breaking Bad News", LessonType = "practice_task", DisplayOrder = 2, Status = ContentStatus.Published },
            new ContentLesson { Id = "lsn-rd-01-task", ModuleId = "mod-full-en-rd-01", ContentItemId = "rt-001", Title = "Practice: HAI Prevention (Part C)", LessonType = "practice_task", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentLesson { Id = "lsn-lt-01-task", ModuleId = "mod-full-en-lt-01", ContentItemId = "lt-001", Title = "Practice: Asthma Management", LessonType = "practice_task", DisplayOrder = 1, Status = ContentStatus.Published }
        );

        // ── Arabic Nursing Course ──
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prg-full-ar-nursing-2026", Code = "full-ar-nursing-2026", Title = "Full OET Nursing Course 2026 (Arabic)",
            Description = "Comprehensive nursing-focused OET course with Arabic instruction.",
            ProfessionId = "nursing", InstructionLanguage = "ar", ProgramType = "full_course",
            Status = ContentStatus.Published, DisplayOrder = 2, EstimatedDurationMinutes = 4800,
            CreatedAt = now, UpdatedAt = now, PublishedAt = now
        });

        db.ContentTracks.AddRange(
            new ContentTrack { Id = "trk-ar-nursing-writing", ProgramId = "prg-full-ar-nursing-2026", SubtestCode = "writing", Title = "Writing Track (Arabic)", DisplayOrder = 1, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-ar-nursing-speaking", ProgramId = "prg-full-ar-nursing-2026", SubtestCode = "speaking", Title = "Speaking Track (Arabic)", DisplayOrder = 2, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-ar-nursing-reading", ProgramId = "prg-full-ar-nursing-2026", SubtestCode = "reading", Title = "Reading Track (Arabic)", DisplayOrder = 3, Status = ContentStatus.Published },
            new ContentTrack { Id = "trk-ar-nursing-listening", ProgramId = "prg-full-ar-nursing-2026", SubtestCode = "listening", Title = "Listening Track (Arabic)", DisplayOrder = 4, Status = ContentStatus.Published }
        );

        // ── Arabic Medicine Course ──
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prg-full-ar-medicine-2026", Code = "full-ar-medicine-2026", Title = "Full OET Medicine Course 2026 (Arabic Doctors)",
            Description = "Medicine-focused OET course with Arabic instruction for doctors.",
            ProfessionId = "medicine", InstructionLanguage = "ar", ProgramType = "full_course",
            Status = ContentStatus.Published, DisplayOrder = 3, EstimatedDurationMinutes = 4800,
            CreatedAt = now, UpdatedAt = now, PublishedAt = now
        });

        // ── Crash Courses ──
        db.ContentPrograms.AddRange(
            new ContentProgram
            {
                Id = "prg-crash-en-general", Code = "crash-en-general", Title = "OET Crash Course (English)",
                Description = "Intensive short-format OET preparation.",
                InstructionLanguage = "en", ProgramType = "crash_course", Status = ContentStatus.Published,
                DisplayOrder = 4, EstimatedDurationMinutes = 1200, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentProgram
            {
                Id = "prg-crash-ar-general", Code = "crash-ar-general", Title = "OET Crash Course (Arabic)",
                Description = "Condensed OET preparation with Arabic instruction.",
                InstructionLanguage = "ar", ProgramType = "crash_course", Status = ContentStatus.Published,
                DisplayOrder = 5, EstimatedDurationMinutes = 1200, CreatedAt = now, UpdatedAt = now, PublishedAt = now
            },
            new ContentProgram
            {
                Id = "prg-crash-en-pharmacy", Code = "crash-en-pharmacy", Title = "OET Pharmacy Crash Course",
                Description = "Pharmacy-focused crash course with profession-specific tasks.",
                ProfessionId = "pharmacy", InstructionLanguage = "en", ProgramType = "crash_course",
                Status = ContentStatus.Published, DisplayOrder = 6, EstimatedDurationMinutes = 900,
                CreatedAt = now, UpdatedAt = now, PublishedAt = now
            }
        );

        // ── Foundation English ──
        db.ContentPrograms.Add(new ContentProgram
        {
            Id = "prg-foundation-basic-en", Code = "foundation-basic-en", Title = "Basic English for OET Preparation",
            Description = "Foundation level English course to build language skills before starting OET-specific preparation.",
            InstructionLanguage = "en", ProgramType = "foundation", Status = ContentStatus.Published,
            DisplayOrder = 7, EstimatedDurationMinutes = 2400, CreatedAt = now, UpdatedAt = now, PublishedAt = now
        });

        db.ContentTracks.Add(
            new ContentTrack { Id = "trk-foundation-core", ProgramId = "prg-foundation-basic-en", SubtestCode = null, Title = "Core English Skills", DisplayOrder = 1, Status = ContentStatus.Published }
        );

        db.ContentModules.AddRange(
            new ContentModule { Id = "mod-foundation-grammar", TrackId = "trk-foundation-core", Title = "Grammar Essentials", Description = "Tenses, articles, prepositions, and formal register.", DisplayOrder = 1, EstimatedDurationMinutes = 180, Status = ContentStatus.Published },
            new ContentModule { Id = "mod-foundation-vocab", TrackId = "trk-foundation-core", Title = "Medical Vocabulary", Description = "Common medical terms, abbreviations, and clinical language.", DisplayOrder = 2, EstimatedDurationMinutes = 150, PrerequisiteModuleId = "mod-foundation-grammar", Status = ContentStatus.Published },
            new ContentModule { Id = "mod-foundation-reading", TrackId = "trk-foundation-core", Title = "Reading Comprehension Basics", Description = "Building reading speed and comprehension for healthcare texts.", DisplayOrder = 3, EstimatedDurationMinutes = 120, Status = ContentStatus.Published }
        );

        // ── Sample free preview assets ──
        db.FreePreviewAssets.AddRange(
            new FreePreviewAsset { Id = "fp-grammar-sample", Title = "OET Grammar Sample", PreviewType = "sample_lesson", ConversionCtaText = "Unlock the full course", TargetPackageId = "pkg-full-en-2026", Status = ContentStatus.Published, DisplayOrder = 1, CreatedAt = now },
            new FreePreviewAsset { Id = "fp-writing-sample", Title = "OET Writing Sample", PreviewType = "sample_lesson", ConversionCtaText = "Start your OET Writing journey", TargetPackageId = "pkg-full-en-2026", Status = ContentStatus.Published, DisplayOrder = 2, CreatedAt = now },
            new FreePreviewAsset { Id = "fp-speaking-sample", Title = "OET Speaking Sample", PreviewType = "sample_lesson", ConversionCtaText = "Master OET Speaking", TargetPackageId = "pkg-full-en-2026", Status = ContentStatus.Published, DisplayOrder = 3, CreatedAt = now },
            new FreePreviewAsset { Id = "fp-reading-sample", Title = "OET Reading Sample", PreviewType = "sample_lesson", ConversionCtaText = "Improve your OET Reading", TargetPackageId = "pkg-full-en-2026", Status = ContentStatus.Published, DisplayOrder = 4, CreatedAt = now },
            new FreePreviewAsset { Id = "fp-listening-sample", Title = "OET Listening Sample", PreviewType = "sample_lesson", ConversionCtaText = "Sharpen your Listening skills", TargetPackageId = "pkg-full-en-2026", Status = ContentStatus.Published, DisplayOrder = 5, CreatedAt = now }
        );
    }

    private static void SeedStrategyGuides(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        StrategyGuide Guide(
            string id,
            string slug,
            string? subtestCode,
            string title,
            string summary,
            string category,
            int minutes,
            int sortOrder,
            string overview,
            string[] actions,
            string[] takeaways,
            string[] mistakes)
            => new()
            {
                Id = id,
                Slug = slug,
                ExamTypeCode = "oet",
                SubtestCode = subtestCode,
                Title = title,
                Summary = summary,
                Category = category,
                ReadingTimeMinutes = minutes,
                SortOrder = sortOrder,
                Status = "active",
                IsPreviewEligible = true,
                ContentHtml = StrategyHtml(overview, actions, takeaways),
                ContentJson = StrategyContentJson(overview, actions, takeaways, mistakes),
                SourceProvenance = "Original OET Prep editorial content reviewed for learner guidance.",
                RightsStatus = "owned",
                FreshnessConfidence = "current",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now
            };

        db.StrategyGuides.AddRange(
            Guide(
                "strategy-oet-overview",
                "oet-strategy-overview",
                null,
                "OET Strategy Overview",
                "A simple plan for turning four subtests into a weekly study system.",
                "exam_overview",
                5,
                10,
                "Treat OET preparation as four linked skills: language control, task awareness, time management, and healthcare communication.",
                [
                    "Start with a diagnostic or recent mock score.",
                    "Choose one weak subtest and one maintenance subtest for each week.",
                    "Review mistakes within 24 hours so patterns are still fresh."
                ],
                [
                    "Do not study every resource at once.",
                    "Use your weakest subtest to choose the next strategy guide."
                ],
                [
                    "Collecting materials without a weekly plan.",
                    "Ignoring review because the next mock feels more urgent."
                ]),
            Guide(
                "strategy-writing-case-notes",
                "writing-case-notes-selection",
                "writing",
                "Writing: Case Notes Selection",
                "Select relevant case notes by reader need, not by the order shown on the paper.",
                "writing",
                6,
                20,
                "High-scoring writing starts before the first sentence: decide the purpose, reader, and clinically relevant notes first.",
                [
                    "Underline the task line and identify the reader.",
                    "Mark notes as must include, useful if space allows, or omit.",
                    "Group notes by purpose: reason for writing, background, current status, and requested action."
                ],
                [
                    "Relevance is more important than quantity.",
                    "The task line decides what belongs in the letter."
                ],
                [
                    "Copying every date and medication detail.",
                    "Writing for the patient instead of the named reader."
                ]),
            Guide(
                "strategy-writing-letter-structure",
                "writing-letter-structure",
                "writing",
                "Writing: Letter Structure",
                "Build a clear referral, discharge, or transfer letter with a purposeful paragraph plan.",
                "writing",
                6,
                30,
                "A strong OET letter uses clear paragraph roles so the reader can act quickly.",
                [
                    "Open with the purpose and patient identity.",
                    "Use one paragraph for relevant background only.",
                    "Put current clinical status and requested action near the end."
                ],
                [
                    "Paragraph order should help the reader make a decision.",
                    "Avoid long chronological retelling unless the task requires it."
                ],
                [
                    "Starting with irrelevant social history.",
                    "Ending without a clear request or follow-up action."
                ]),
            Guide(
                "strategy-speaking-roleplay",
                "speaking-roleplay-flow",
                "speaking",
                "Speaking: Roleplay Flow",
                "Keep the interaction patient-centred while covering the task card naturally.",
                "speaking",
                5,
                40,
                "OET Speaking rewards natural healthcare communication, not memorised speeches.",
                [
                    "Open with a warm greeting and confirm the concern.",
                    "Ask before explaining, then chunk information into short turns.",
                    "Check understanding and invite questions before closing."
                ],
                [
                    "The interlocutor is a patient, carer, or colleague, not an examiner.",
                    "Empathy and structure must work together."
                ],
                [
                    "Reading the card aloud.",
                    "Explaining for too long without checking understanding."
                ]),
            Guide(
                "strategy-reading-part-a",
                "reading-part-a-speed",
                "reading",
                "Reading Part A: Speed And Accuracy",
                "Use the 15 minutes to locate facts quickly without sacrificing exact wording.",
                "reading",
                5,
                50,
                "Reading Part A is a controlled information hunt: headings, keywords, and exact answer format matter.",
                [
                    "Scan all headings before answering.",
                    "Match synonyms, then copy the exact needed word or phrase.",
                    "Leave hard items and return after easier facts are secured."
                ],
                [
                    "Your first job is location, not deep reading.",
                    "Answer format errors lose easy marks."
                ],
                [
                    "Reading every text from top to bottom.",
                    "Changing spelling or grammar when copying short answers."
                ]),
            Guide(
                "strategy-reading-bc",
                "reading-parts-b-c",
                "reading",
                "Reading Parts B/C: Reasoning",
                "Part B handles short extracts from different healthcare contexts; Part C handles 2 long articles with evidence-based elimination.",
                "reading",
                6,
                60,
                "Parts B and C test meaning, purpose, opinion, and inference. The correct option must be supported by the text.",
                [
                    "Read the question stem before the options.",
                    "Find the line that proves or rejects each option.",
                    "Eliminate options that are true but do not answer the question."
                ],
                [
                    "Evidence beats intuition.",
                    "A partially true option is still wrong if it misses the question."
                ],
                [
                    "Choosing familiar medical vocabulary instead of textual proof.",
                    "Letting one difficult paragraph consume the clock."
                ]),
            Guide(
                "strategy-listening-part-a",
                "listening-part-a-notes",
                "listening",
                "Listening Part A: Notes",
                "Capture patient consultation details with abbreviations and prediction.",
                "listening",
                5,
                70,
                "Listening Part A rewards prediction and fast note completion. You must use the pauses actively.",
                [
                    "Read headings and predict the type of answer before audio starts.",
                    "Write short medical abbreviations during the first pass.",
                    "Use grammar around the blank to decide singular, plural, or adjective form."
                ],
                [
                    "The pause is part of the task.",
                    "Prediction reduces panic when the audio begins."
                ],
                [
                    "Waiting until the speaker finishes before writing.",
                    "Missing plural endings and units."
                ]),
            Guide(
                "strategy-listening-bc",
                "listening-parts-b-c",
                "listening",
                "Listening Parts B/C: Decision Making",
                "Identify speaker purpose, attitude, and the reason behind each answer.",
                "listening",
                6,
                80,
                "Parts B and C often test why a speaker says something, not just what words you hear.",
                [
                    "Preview the stem and options for the decision you need to make.",
                    "Listen for contrast markers such as however, but, and actually.",
                    "Choose after the full exchange, not after one matching word."
                ],
                [
                    "Same-word matches are often traps.",
                    "Tone and purpose can decide the answer."
                ],
                [
                    "Selecting an option as soon as you hear a keyword.",
                    "Ignoring the final correction or qualification."
                ]),
            Guide(
                "strategy-time-management",
                "oet-time-management",
                null,
                "Time Management Across The OET",
                "Use repeatable timing rules for mocks, practice blocks, and exam day.",
                "time_management",
                4,
                90,
                "Good timing is trained before exam day through consistent cut-off rules.",
                [
                    "Practise each subtest under real section timing at least weekly.",
                    "Set a maximum time for hard items, then move on.",
                    "Review where time was lost after every mock."
                ],
                [
                    "A timing rule prevents emotional decisions under pressure.",
                    "Review time loss as carefully as wrong answers."
                ],
                [
                    "Doing untimed practice for too long.",
                    "Changing timing strategy on exam day."
                ]),
            Guide(
                "strategy-common-mistakes",
                "common-oet-mistakes",
                null,
                "Common OET Mistakes",
                "Avoid the recurring errors that cost marks across writing, speaking, reading, and listening.",
                "common_mistakes",
                5,
                100,
                "Most score plateaus come from repeated small errors, not from one missing secret technique.",
                [
                    "Keep an error log grouped by subtest.",
                    "Fix one recurring error at a time.",
                    "Check whether each mistake is language, strategy, timing, or attention."
                ],
                [
                    "Patterns matter more than isolated mistakes.",
                    "The error log should drive your next study session."
                ],
                [
                    "Only checking the final score.",
                    "Repeating full mocks without targeted repair."
                ]),
            Guide(
                "strategy-exam-day-checklist",
                "oet-exam-day-checklist",
                null,
                "Exam-Day Checklist",
                "Prepare documents, timing, equipment, and mindset before the test begins.",
                "exam_day",
                4,
                110,
                "Exam day should feel familiar. Reduce avoidable stress before you enter the test room or online check-in.",
                [
                    "Confirm identification, test time, venue or online setup, and travel plan.",
                    "Prepare permitted items the evening before.",
                    "Use a short warm-up, not a last-minute cram session."
                ],
                [
                    "Preparation reduces cognitive load.",
                    "Your goal is calm execution of familiar routines."
                ],
                [
                    "Studying new material on the morning of the exam.",
                    "Arriving without checking ID or technical requirements."
                ])
        );
    }

    private static string StrategyContentJson(string overview, string[] actions, string[] takeaways, string[] mistakes)
        => JsonSupport.Serialize(new
        {
            version = 1,
            overview,
            sections = new[]
            {
                new { heading = "What to do", body = overview, bullets = actions },
                new { heading = "Common traps", body = "Avoid these recurring mistakes while practising.", bullets = mistakes }
            },
            keyTakeaways = takeaways
        });

    private static string StrategyHtml(string overview, string[] actions, string[] takeaways)
        => $"<p>{overview}</p><h2>Action steps</h2><ul>{string.Join("", actions.Select(action => $"<li>{action}</li>"))}</ul><h2>Key takeaways</h2><ul>{string.Join("", takeaways.Select(takeaway => $"<li>{takeaway}</li>"))}</ul>";
}
