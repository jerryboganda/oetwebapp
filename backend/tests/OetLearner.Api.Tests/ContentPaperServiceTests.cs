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
        await AddMediaAsync(db, "reading-question-paper");
        await AddMediaAsync(db, "reading-answer-key");
        await svc.AttachAssetAsync(paperId, new ContentPaperAssetAttach(
            PaperAssetRole.QuestionPaper, "reading-question-paper", null, null, 0, true),
            "admin-1", default);
        await svc.AttachAssetAsync(paperId, new ContentPaperAssetAttach(
            PaperAssetRole.AnswerKey, "reading-answer-key", null, null, 1, true),
            "admin-1", default);
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
            await structure.UpsertQuestionAsync(new ReadingQuestionUpsert(
                null, partA.Id, textsA[(i - 1) % textsA.Count].Id, i, 1, ReadingQuestionType.ShortAnswer,
                $"Part A question {i}", "[]", $"\"answer-{i}\"", null, false, null, "detail"),
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
    public async Task Publish_succeeds_when_requirements_met()
    {
        var (db, svc) = Build();
        var p = await svc.CreateAsync(new ContentPaperCreate(
            "writing", "W1", null, "medicine", false, null, 45, null, "routine_referral",
            0, null, DefaultSourceProvenance), "admin-1", default);

        await AddMediaAsync(db, "m-cn");
        await svc.AttachAssetAsync(p.Id, new ContentPaperAssetAttach(
            PaperAssetRole.CaseNotes, "m-cn", null, null, 0, MakePrimary: true),
            "admin-1", default);

        await svc.PublishAsync(p.Id, "admin-1", default);

        var reload = await db.ContentPapers.FirstAsync(x => x.Id == p.Id);
        Assert.Equal(ContentStatus.Published, reload.Status);
        Assert.NotNull(reload.PublishedAt);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Publish_fails_when_reading_structure_is_not_ready()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "reading", "Reading 1", null, null, true, null, 60, null, null, 0, null,
            DefaultSourceProvenance), "admin-1", default);
        await AttachRequiredReadingAssetsAsync(db, svc, paper.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.PublishAsync(paper.Id, "admin-1", default));
        Assert.Contains("Reading structure", ex.Message);
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
    public async Task Publish_fails_when_speaking_structure_is_not_ready()
    {
        var (db, svc) = Build();
        var paper = await svc.CreateAsync(new ContentPaperCreate(
            "speaking", "Speaking 1", null, "nursing", false, null, 10, "roleplay", null, 0,
            "relationshipBuilding", DefaultSourceProvenance), "admin-1", default);
        await AttachRequiredSpeakingAssetsAsync(db, svc, paper.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.PublishAsync(paper.Id, "admin-1", default));
        Assert.Contains("Speaking structure", ex.Message);
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
