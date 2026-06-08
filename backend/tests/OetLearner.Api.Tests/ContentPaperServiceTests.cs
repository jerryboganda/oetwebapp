using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Tests;

public class ContentPaperServiceTests
{
    private const string DefaultSourceProvenance = ContentDefaults.DefaultSourceProvenance;

    private static (LearnerDbContext db, ContentPaperService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new ContentPaperService(db));
    }

    private static async Task<string> AddMediaAsync(LearnerDbContext db, string id = "media-1", string? sha = null)
    {
        db.MediaAssets.Add(new MediaAsset
        {
            Id = id,
            OriginalFilename = $"{id}.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            SizeBytes = 1024,
            StoragePath = $"uploads/published/aa/bb/{id}.pdf",
            Status = MediaAssetStatus.Ready,
            Sha256 = sha ?? "aabbccddeeff" + id.PadRight(52, '0'),
            UploadedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task AttachRequiredReadingAssetsAsync(
        LearnerDbContext db,
        ContentPaperService svc,
        string paperId)
    {
        foreach (var (part, displayOrder) in new[] { ("A", 0), ("B", 1), ("C", 2) })
        {
            var mediaId = $"reading-question-paper-{part}";
            await AddMediaAsync(db, mediaId);
            await svc.AttachAssetAsync(paperId, new ContentPaperAssetAttach(
                PaperAssetRole.QuestionPaper, mediaId, part, $"Part {part} PDF", displayOrder, true),
                "admin-1", default);
        }
    }

    private static async Task AttachRequiredSpeakingAssetsAsync(
        LearnerDbContext db,
        ContentPaperService svc,
        string paperId)
    {
        await AddMediaAsync(db, "speaking-role-card");
        await AddMediaAsync(db, "speaking-assessment-criteria");
        await AddMediaAsync(db, "speaking-warm-up");
        await svc.AttachAssetAsync(paperId, new ContentPaperAssetAttach(
                PaperAssetRole.RoleCard, "speaking-role-card", null, null, 0, true),
            "admin-1", default);
        await svc.AttachAssetAsync(paperId, new ContentPaperAssetAttach(
                PaperAssetRole.AssessmentCriteria, "speaking-assessment-criteria", null, null, 1, true),
            "admin-1", default);
        await svc.AttachAssetAsync(paperId, new ContentPaperAssetAttach(
                PaperAssetRole.WarmUpQuestions, "speaking-warm-up", null, null, 2, true),
            "admin-1", default);
    }

    private static async Task AttachRequiredWritingAssetsAsync(
        LearnerDbContext db,
        ContentPaperService svc,
        string paperId)
    {
        await AddMediaAsync(db, "writing-case-notes");
        await AddMediaAsync(db, "writing-model-answer");
        await svc.AttachAssetAsync(paperId, new ContentPaperAssetAttach(
            PaperAssetRole.CaseNotes, "writing-case-notes", null, null, 0, true),
            "admin-1", default);
        await svc.AttachAssetAsync(paperId, new ContentPaperAssetAttach(
            PaperAssetRole.ModelAnswer, "writing-model-answer", null, null, 1, true),
            "admin-1", default);
    }

    private static async Task FullyAuthorSpeakingPaperAsync(LearnerDbContext db, string paperId)
    {
        var paper = await db.ContentPapers.FirstAsync(p => p.Id == paperId);
        paper.ExtractedTextJson = JsonSupport.Serialize(new
        {
            speakingStructure = new
            {
                candidateCard = new
                {
                    candidateRole = "Nurse",
                    setting = "Community clinic",
                    patientRole = "Parent of a child with asthma",
                    task = "Explain inhaler technique and safety-netting.",
                    background = "The parent is anxious after a night-time wheeze episode.",
                    tasks = new[]
                    {
                        "Find out the parent's main concern.",
                        "Explain inhaler technique in lay language.",
                        "Agree a clear follow-up plan."
                    }
                },
                interlocutorCard = new
                {
                    patientProfile = "You are worried the medication is too strong for your child.",
                    cuePrompts = new[]
                    {
                        "Ask whether the inhaler is addictive.",
                        "Say you are afraid of another night attack."
                    },
                    privateNotes = "Escalate concern if the candidate uses jargon."
                },
                warmUpQuestions = new[]
                {
                    "How are you feeling today?",
                    "What would you like help with first?"
                },
                prepTimeSeconds = 180,
                roleplayTimeSeconds = 300,
                patientEmotion = "anxious",
                communicationGoal = "Reassure the parent while checking understanding.",
                clinicalTopic = "paediatric asthma inhaler education",
                criteriaFocus = new[] { "relationshipBuilding", "informationGiving" },
                complianceNotes = "Practice content only."
            }
        });
        await db.SaveChangesAsync();
    }

    private static async Task FullyAuthorWritingPaperAsync(LearnerDbContext db, string paperId)
    {
        var paper = await db.ContentPapers.FirstAsync(p => p.Id == paperId);
        paper.ExtractedTextJson = JsonSupport.Serialize(new
        {
            writingStructure = new
            {
                taskPrompt = "Using the case notes, write a referral letter to the patient's GP.",
                taskDate = "25 Mar 2023",
                writerRole = "Doctor",
                recipient = "Dr Smith, General Practitioner",
                purpose = "Referral for ongoing management",
                caseNotes = "Patient: Mr John Roberts, 58.\nDiagnosis: type 2 diabetes.\nReason for referral: review medication adherence.",
                modelAnswerText = "Dear Dr Smith,\n\nThank you for reviewing Mr John Roberts, a 58-year-old patient with type 2 diabetes who requires support with medication adherence.\n\nYours sincerely,",
                criteriaFocus = new[] { "purpose", "content", "conciseness" }
            }
        });
        await db.SaveChangesAsync();
    }

    private static async Task FullyAuthorReadingPaperAsync(
        LearnerDbContext db,
        ReadingStructureService structure,
        string paperId)
    {
        var parts = await db.ReadingParts.Where(p => p.PaperId == paperId).ToListAsync();
        var partA = parts.First(p => p.PartCode == ReadingPartCode.A);
        var partB = parts.First(p => p.PartCode == ReadingPartCode.B);
        var partC = parts.First(p => p.PartCode == ReadingPartCode.C);

        var textsA = new List<ReadingText>();
        var textsB = new List<ReadingText>();
        var textsC = new List<ReadingText>();
        for (var i = 1; i <= 4; i++)
        {
            textsA.Add(await structure.UpsertTextAsync(new ReadingTextUpsert(
                null, partA.Id, i, $"Part A text {i}", "NHS", "<p>Part A</p>", 80, null), "admin-1", default));
        }
        for (var i = 1; i <= 6; i++)
        {
            textsB.Add(await structure.UpsertTextAsync(new ReadingTextUpsert(
                null, partB.Id, i, $"Part B extract {i}", "BMJ", "<p>Part B</p>", 80, null), "admin-1", default));
        }
        for (var i = 1; i <= 2; i++)
        {
            textsC.Add(await structure.UpsertTextAsync(new ReadingTextUpsert(
                null, partC.Id, i, $"Part C text {i}", "Lancet", "<p>Part C</p>", 320, null), "admin-1", default));
        }

        for (var i = 1; i <= 20; i++)
        {
            // Match the strict OET Part A pattern enforced by ReadingStructureService:
            //   1–7  MatchingTextReference (single-letter A–D), 8–14 ShortAnswer,
            //   15–20 SentenceCompletion. Texts A1–A4 map to letters A–D.
            var (qType, correct) = i switch
            {
                <= 7 => (ReadingQuestionType.MatchingTextReference, $"\"{(char)('A' + ((i - 1) % textsA.Count))}\""),
                <= 14 => (ReadingQuestionType.ShortAnswer, $"\"answer-{i}\""),
                _ => (ReadingQuestionType.SentenceCompletion, $"\"answer-{i}\""),
            };
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partA.Id, textsA[(i - 1) % textsA.Count].Id, i, 1, qType,
                $"Part A question {i}", "[]", correct, null, false, null, "detail"),
                "admin-1", default);
        }

        for (var i = 1; i <= 6; i++)
        {
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partB.Id, textsB[i - 1].Id, i, 1, ReadingQuestionType.MultipleChoice3,
                $"Part B question {i}", "[\"A\",\"B\",\"C\"]", "\"A\"", null, false, null, "purpose"),
                "admin-1", default);
        }

        for (var i = 1; i <= 16; i++)
        {
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partC.Id, textsC[(i - 1) / 8].Id, i, 1, ReadingQuestionType.MultipleChoice4,
                $"Part C question {i}", "[\"A\",\"B\",\"C\",\"D\"]", "\"B\"", null, false, null, "inference"),
                "admin-1", default);
        }

        // Phase 4 — fast-forward all questions to Published so the publish
        // gate doesn't block tests that author paper structure directly.
        var partIds = parts.Select(p => p.Id).ToList();
        var qs = await db.ReadingQuestions.Where(q => partIds.Contains(q.ReadingPartId)).ToListAsync();
        foreach (var q in qs) q.ReviewState = ReadingReviewState.Published;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_generates_slug_and_rejects_duplicates()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            SubtestCode: "listening",
            Title: "Listening Sample 1",
            Slug: null,
            ProfessionId: null,
            AppliesToAllProfessions: true,
            Difficulty: null,
            EstimatedDurationMinutes: 40,
            CardType: null, LetterType: null, Priority: 0, TagsCsv: null,
            SourceProvenance: DefaultSourceProvenance), "admin-1", default);
        Assert.Equal("listening-sample-1", p.Slug);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(new ContentPaperCreate(
                "listening", "Listening Sample 1", null, null, true,
                null, 40, null, null, 0, null, "A"), "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Create_rejects_ambiguous_profession_scope()
    {
        var (db, svc) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync(new ContentPaperCreate(
                "writing", "W1", null, "medicine", true,
                null, 45, null, "routine_referral", 0, null, "A"), "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RequiredRolesFor_matches_spec()
    {
        var (db, svc) = Build();
        Assert.Contains(PaperAssetRole.Audio, svc.RequiredRolesFor("listening"));
        Assert.Contains(PaperAssetRole.QuestionPaper, svc.RequiredRolesFor("listening"));
        Assert.Contains(PaperAssetRole.AnswerKey, svc.RequiredRolesFor("listening"));
        Assert.Contains(PaperAssetRole.AudioScript, svc.RequiredRolesFor("listening"));
        Assert.Contains(PaperAssetRole.CaseNotes, svc.RequiredRolesFor("writing"));
        Assert.Contains(PaperAssetRole.ModelAnswer, svc.RequiredRolesFor("writing"));
        Assert.Contains(PaperAssetRole.RoleCard, svc.RequiredRolesFor("speaking"));
        Assert.Contains(PaperAssetRole.AssessmentCriteria, svc.RequiredRolesFor("speaking"));
        Assert.Contains(PaperAssetRole.WarmUpQuestions, svc.RequiredRolesFor("speaking"));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_fails_when_required_roles_missing()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "listening", "L1", null, null, true, null, 40, null, null, 0, null,
            DefaultSourceProvenance), "admin-1", default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.PublishAsync(p.Id, "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_fails_without_source_provenance()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1", null, "medicine", false, null, 45, null, "routine_referral",
            0, null, SourceProvenance: null), "admin-1", default);

        await AddMediaAsync(db, "m-cn");
        await svc.AttachAssetAsync(p.Id, new ContentPaperAssetAttach(
            PaperAssetRole.CaseNotes, "m-cn", null, null, 0, MakePrimary: true),
            "admin-1", default);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.PublishAsync(p.Id, "admin-1", default));
        Assert.Contains("SourceProvenance", ex.Message);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_records_warning_when_writing_structure_is_not_ready()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1", null, "medicine", false, null, 45, null, "routine_referral",
            0, null, DefaultSourceProvenance), "admin-1", default);

        await AttachRequiredWritingAssetsAsync(db, svc, p.Id);

        // Decision 2 — publishing is NEVER blocked on conformance grounds; the
        // structural problem is recorded as a non-blocking warning instead.
        await svc.PublishAsync(p.Id, "admin-1", default);

        var reload = await db.ContentPapers.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);
        var actions = await db.AuditEvents.Select(a => a.Action).ToListAsync();
        Assert.Contains("ContentPaperPublishConformanceWarning", actions);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_records_warning_when_writing_letter_type_is_not_canonical()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1", null, "medicine", false, null, 45, null, "informal_note",
            0, null, DefaultSourceProvenance), "admin-1", default);

        await AttachRequiredWritingAssetsAsync(db, svc, p.Id);
        await FullyAuthorWritingPaperAsync(db, p.Id);

        // Decision 2 — a non-canonical letter type is a non-blocking warning.
        await svc.PublishAsync(p.Id, "admin-1", default);

        var reload = await db.ContentPapers.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);
        var actions = await db.AuditEvents.Select(a => a.Action).ToListAsync();
        Assert.Contains("ContentPaperPublishConformanceWarning", actions);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_succeeds_for_writing_when_structure_and_assets_are_ready()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1", null, "medicine", false, null, 45, null, "routine_referral",
            0, "purpose,content", DefaultSourceProvenance), "admin-1", default);

        await AttachRequiredWritingAssetsAsync(db, svc, p.Id);
        await FullyAuthorWritingPaperAsync(db, p.Id);

        await svc.PublishAsync(p.Id, "admin-1", default);

        var reload = await db.ContentPapers.FirstAsync(x => x.Id == p.Id);
        var content = await db.ContentItems.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);
        Assert.NotNull(reload.PublishedAt);
        Assert.Equal("writing_task", content.ContentType);
        Assert.Equal("writing", content.SubtestCode);
        Assert.Equal(ContentStatus.Published, content.Status);
        Assert.Equal("routine_referral", content.ScenarioType);
        Assert.Contains("Mr John Roberts", content.CaseNotes);
        Assert.Contains("Dear Dr Smith", content.ModelAnswerJson);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Archive_hides_projected_writing_content_item()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "writing", "Writing archive", null, "medicine", false, null, 45, null, "urgent_referral",
            0, "purpose", DefaultSourceProvenance), "admin-1", default);
        await AttachRequiredWritingAssetsAsync(db, svc, paper.Id);
        await FullyAuthorWritingPaperAsync(db, paper.Id);
        await svc.PublishAsync(paper.Id, "admin-1", default);

        await svc.ArchiveAsync(paper.Id, "admin-1", default);

        var content = await db.ContentItems.FirstAsync(x => x.Id == paper.Id);
        Assert.Equal(ContentStatus.Archived, content.Status);
        Assert.NotNull(content.ArchivedAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Update_returns_attached_assets_for_admin_projection()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "writing", "Writing metadata", null, "medicine", false, null, 45, null, "routine_referral",
            0, null, DefaultSourceProvenance), "admin-1", default);
        await AttachRequiredWritingAssetsAsync(db, svc, paper.Id);

        var updated = await svc.UpdateAsync(paper.Id, new ContentPaperUpdate(
            "Writing metadata updated", null, null, null, null, null, null, null, null, null),
            "admin-1", default);

        Assert.Equal("Writing metadata updated", updated.Title);
        Assert.Contains(updated.Assets, asset => asset.Role == PaperAssetRole.CaseNotes && asset.MediaAsset is not null);
        Assert.Contains(updated.Assets, asset => asset.Role == PaperAssetRole.ModelAnswer && asset.MediaAsset is not null);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_records_warning_when_reading_structure_is_not_ready()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "reading", "Reading 1", null, null, true, null, 60, null, null, 0, null,
            DefaultSourceProvenance), "admin-1", default);
        await AttachRequiredReadingAssetsAsync(db, svc, paper.Id);

        // Decision 2 — a non-publish-ready reading structure is a non-blocking warning.
        await svc.PublishAsync(paper.Id, "admin-1", default);

        var reload = await db.ContentPapers.FirstAsync(x => x.Id == paper.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);
        var actions = await db.AuditEvents.Select(a => a.Action).ToListAsync();
        Assert.Contains("ContentPaperPublishConformanceWarning", actions);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_records_warning_when_reading_part_pdf_media_is_not_ready_pdf()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "reading", "Reading PDF guard", null, null, true, null, 60, null, null, 0, null,
            DefaultSourceProvenance), "admin-1", default);
        await AttachRequiredReadingAssetsAsync(db, svc, paper.Id);

        var partBMedia = await db.MediaAssets.FirstAsync(m => m.Id == "reading-question-paper-B");
        partBMedia.MimeType = "text/plain";
        partBMedia.Format = "txt";
        await db.SaveChangesAsync();

        var structure = new ReadingStructureService(db);
        await structure.EnsureCanonicalPartsAsync(paper.Id, default);
        await FullyAuthorReadingPaperAsync(db, structure, paper.Id);

        // Decision 2 — a missing/invalid Part B PDF is a non-blocking warning.
        await svc.PublishAsync(paper.Id, "admin-1", default);

        var reload = await db.ContentPapers.FirstAsync(x => x.Id == paper.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);
        var actions = await db.AuditEvents.Select(a => a.Action).ToListAsync();
        Assert.Contains("ContentPaperPublishConformanceWarning", actions);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_succeeds_when_reading_structure_is_ready()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "reading", "Reading 2", null, null, true, null, 60, null, null, 0, null,
            DefaultSourceProvenance), "admin-1", default);
        await AttachRequiredReadingAssetsAsync(db, svc, paper.Id);

        var structure = new ReadingStructureService(db);
        await structure.EnsureCanonicalPartsAsync(paper.Id, default);
        await FullyAuthorReadingPaperAsync(db, structure, paper.Id);

        await svc.PublishAsync(paper.Id, "admin-1", default);

        var reload = await db.ContentPapers.FirstAsync(x => x.Id == paper.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_records_warning_when_speaking_structure_is_not_ready()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "speaking", "Speaking 1", null, "nursing", false, null, 10, "roleplay", null, 0,
            "relationshipBuilding", DefaultSourceProvenance), "admin-1", default);
        await AttachRequiredSpeakingAssetsAsync(db, svc, paper.Id);

        // Decision 2 — a non-publish-ready speaking structure is a non-blocking warning.
        await svc.PublishAsync(paper.Id, "admin-1", default);

        var reload = await db.ContentPapers.FirstAsync(x => x.Id == paper.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);
        var actions = await db.AuditEvents.Select(a => a.Action).ToListAsync();
        Assert.Contains("ContentPaperPublishConformanceWarning", actions);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_succeeds_for_speaking_when_structure_and_assets_are_ready()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "speaking", "Speaking 2", null, "nursing", false, "B", 10, "roleplay", null, 0,
            "relationshipBuilding,informationGiving", DefaultSourceProvenance), "admin-1", default);
        await AttachRequiredSpeakingAssetsAsync(db, svc, paper.Id);
        await FullyAuthorSpeakingPaperAsync(db, paper.Id);

        await svc.PublishAsync(paper.Id, "admin-1", default);

        var reload = await db.ContentPapers.FirstAsync(x => x.Id == paper.Id);
        var content = await db.ContentItems.FirstAsync(x => x.Id == paper.Id);
        var detail = SpeakingContentStructure.ExtractStructure(content.DetailJson);
        Assert.Equal(ContentStatus.Published, reload.Status);
        Assert.Equal("speaking", content.SubtestCode);
        Assert.Equal(ContentStatus.Published, content.Status);
        Assert.Equal("Nurse", SpeakingContentStructure.ReadString(
            SpeakingContentStructure.ToDictionary(SpeakingContentStructure.ReadValue(detail, "candidateCard")),
            "candidateRole"));
        Assert.NotNull(SpeakingContentStructure.ReadValue(detail, "interlocutorCard"));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AttachAsset_flips_previous_primary_for_same_role_part()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "listening", "L1", null, null, true, null, 40, null, null, 0, null,
            DefaultSourceProvenance), "admin-1", default);
        await AddMediaAsync(db, "m1", "sha-one");
        await AddMediaAsync(db, "m2", "sha-two");

        await svc.AttachAssetAsync(p.Id, new ContentPaperAssetAttach(
            PaperAssetRole.Audio, "m1", null, null, 0, true), "admin-1", default);
        await svc.AttachAssetAsync(p.Id, new ContentPaperAssetAttach(
            PaperAssetRole.Audio, "m2", null, null, 1, true), "admin-1", default);

        var assets = await db.ContentPaperAssets.Where(a => a.PaperId == p.Id).ToListAsync();
        Assert.Equal(2, assets.Count);
        Assert.Single(assets, a => a.IsPrimary);
        Assert.Equal("m2", assets.Single(a => a.IsPrimary).MediaAssetId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Archive_sets_archived_status_and_timestamp()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "listening", "L1", null, null, true, null, 40, null, null, 0, null, "A"),
            "admin-1", default);
        await svc.ArchiveAsync(p.Id, "admin-1", default);
        var reload = await db.ContentPapers.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(ContentStatus.Archived, reload.Status);
        Assert.NotNull(reload.ArchivedAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Create_writing_task_requires_integrity_acknowledgement()
    {
        var (db, svc) = Build();
        await Assert.ThrowsAsync<ContentIntegrityAcknowledgementRequiredException>(() =>
            svc.CreateWritingTaskAsync(new WritingTaskCreate(
                Title: "Medicine — Routine Referral",
                Slug: null,
                ProfessionId: "medicine",
                LetterType: "routine_referral",
                Difficulty: null,
                EstimatedDurationMinutes: 45,
                Priority: 0,
                TagsCsv: null,
                SourceProvenance: DefaultSourceProvenance,
                IntegrityAcknowledged: false), "admin-1", default));
        Assert.Equal(0, await db.ContentPapers.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Create_writing_task_persists_integrity_metadata_when_acknowledged()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateWritingTaskAsync(new WritingTaskCreate(
            Title: "Medicine — Routine Referral",
            Slug: null,
            ProfessionId: "medicine",
            LetterType: "routine_referral",
            Difficulty: null,
            EstimatedDurationMinutes: 45,
            Priority: 0,
            TagsCsv: null,
            SourceProvenance: DefaultSourceProvenance,
            IntegrityAcknowledged: true), "admin-7", default);

        Assert.Equal("admin-7", paper.IntegrityAcknowledgedByAdminId);
        Assert.NotNull(paper.IntegrityAcknowledgedAt);
        Assert.Equal(ContentStatus.Draft, paper.Status);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Create_writing_task_rejects_letter_type_not_allowed_for_profession()
    {
        var (db, svc) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateWritingTaskAsync(new WritingTaskCreate(
                Title: "Veterinary — bad letter type",
                Slug: null,
                ProfessionId: "veterinary",
                // non_medical_referral is excluded for veterinary per WritingContentStructure allow-list.
                LetterType: "non_medical_referral",
                Difficulty: null,
                EstimatedDurationMinutes: 45,
                Priority: 0,
                TagsCsv: null,
                SourceProvenance: DefaultSourceProvenance,
                IntegrityAcknowledged: true), "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Submit_for_review_requires_draft_state()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateWritingTaskAsync(new WritingTaskCreate(
            Title: "Wf-task", Slug: null, ProfessionId: "medicine", LetterType: "routine_referral",
            Difficulty: null, EstimatedDurationMinutes: 45, Priority: 0, TagsCsv: null,
            SourceProvenance: DefaultSourceProvenance, IntegrityAcknowledged: true), "admin-1", default);

        await svc.SubmitForReviewAsync(paper.Id, "admin-1", default);
        var reload = await db.ContentPapers.AsNoTracking().FirstAsync(p => p.Id == paper.Id);
        Assert.Equal(ContentStatus.InReview, reload.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitForReviewAsync(paper.Id, "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Approve_publish_requires_in_review_state()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateWritingTaskAsync(new WritingTaskCreate(
            Title: "Wf-task", Slug: null, ProfessionId: "medicine", LetterType: "routine_referral",
            Difficulty: null, EstimatedDurationMinutes: 45, Priority: 0, TagsCsv: null,
            SourceProvenance: DefaultSourceProvenance, IntegrityAcknowledged: true), "admin-1", default);

        // Direct Approve from Draft should be rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApproveAndPublishAsync(paper.Id, "admin-2", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Approve_publish_transitions_in_review_to_published()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateWritingTaskAsync(new WritingTaskCreate(
            Title: "Wf-task", Slug: null, ProfessionId: "medicine", LetterType: "routine_referral",
            Difficulty: null, EstimatedDurationMinutes: 45, Priority: 0, TagsCsv: null,
            SourceProvenance: DefaultSourceProvenance, IntegrityAcknowledged: true), "admin-1", default);
        await AttachRequiredWritingAssetsAsync(db, svc, paper.Id);
        await FullyAuthorWritingPaperAsync(db, paper.Id);
        await svc.SubmitForReviewAsync(paper.Id, "admin-1", default);

        await svc.ApproveAndPublishAsync(paper.Id, "admin-2", default);

        var reload = await db.ContentPapers.AsNoTracking().FirstAsync(p => p.Id == paper.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);
        Assert.NotNull(reload.PublishedAt);

        var actions = await db.AuditEvents.AsNoTracking()
            .Where(a => a.ResourceId == paper.Id)
            .Select(a => a.Action)
            .ToListAsync();
        Assert.Contains("ContentPaperSubmittedForReview", actions);
        Assert.Contains("ContentPaperApprovedForPublish", actions);
        Assert.Contains("ContentPaperPublished", actions);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Reject_requires_in_review_state_and_records_reason()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateWritingTaskAsync(new WritingTaskCreate(
            Title: "Wf-task", Slug: null, ProfessionId: "medicine", LetterType: "routine_referral",
            Difficulty: null, EstimatedDurationMinutes: 45, Priority: 0, TagsCsv: null,
            SourceProvenance: DefaultSourceProvenance, IntegrityAcknowledged: true), "admin-1", default);

        // Reject before InReview should fail.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RejectAsync(paper.Id, "admin-2", "Too short", default));

        await svc.SubmitForReviewAsync(paper.Id, "admin-1", default);
        await svc.RejectAsync(paper.Id, "admin-2", "Case notes incomplete", default);

        var reload = await db.ContentPapers.AsNoTracking().FirstAsync(p => p.Id == paper.Id);
        Assert.Equal(ContentStatus.Rejected, reload.Status);

        var rejectAudit = await db.AuditEvents.AsNoTracking()
            .FirstAsync(a => a.ResourceId == paper.Id && a.Action == "ContentPaperRejected");
        Assert.Contains("Case notes incomplete", rejectAudit.Details);
        Assert.Equal("admin-2", rejectAudit.ActorId);

        // Empty reason rejected.
        var paper2 = await svc.CreateWritingTaskAsync(new WritingTaskCreate(
            Title: "Wf-task-2", Slug: null, ProfessionId: "medicine", LetterType: "routine_referral",
            Difficulty: null, EstimatedDurationMinutes: 45, Priority: 0, TagsCsv: null,
            SourceProvenance: DefaultSourceProvenance, IntegrityAcknowledged: true), "admin-1", default);
        await svc.SubmitForReviewAsync(paper2.Id, "admin-1", default);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.RejectAsync(paper2.Id, "admin-2", "   ", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task List_filters_by_subtest_and_profession_including_applies_to_all()
    {
        var (db, svc) = Build();
        await svc.CreateAsync(new ContentPaperCreate(
            "listening", "L1", null, null, true, null, 40, null, null, 0, null, "A"),
            "admin-1", default);
        await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1-med", null, "medicine", false, null, 45, null, "routine_referral",
            0, null, "A"), "admin-1", default);
        await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1-nurse", null, "nursing", false, null, 45, null, "routine_referral",
            0, null, "A"), "admin-1", default);

        var medicineWriting = await svc.ListAsync(new ContentPaperQuery(
            SubtestCode: "writing", ProfessionId: "medicine"), default);
        Assert.Single(medicineWriting);
        Assert.Equal("W1-med", medicineWriting[0].Title);

        var medicineAll = await svc.ListAsync(new ContentPaperQuery(
            ProfessionId: "medicine"), default);
        // Should include both W1-med (profession match) and L1 (applies-to-all)
        Assert.Equal(2, medicineAll.Count);
        await db.DisposeAsync();
    }
}
