using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Writing Addendum Rev8 §17/§19.6 — the 100% task-load integrity gate
/// (GET /v1/admin/writing/tasks/load-integrity). Every published task must
/// pass the exact learner projection and every load-time dependency; the
/// Model Answer is reported separately and never counted as a load failure.
/// </summary>
public sealed class WritingRev8TaskLoadIntegrityTests
{
    private const string PdfKey = "writing/stimulus/complete.pdf";

    private static LearnerDbContext NewContext()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static WritingScenario Scenario(
        LearnerDbContext db,
        string title,
        string status = "published",
        string? taskPrompt = "Using the case notes, write a discharge letter to the community nurse.",
        int sentenceCount = 2,
        string? pdfAssetId = null)
    {
        var scenario = new WritingScenario
        {
            Id = Guid.NewGuid(),
            Title = title,
            LetterType = "LT-DG",
            Profession = "Nursing",
            Difficulty = 3,
            Status = status,
            AuthorId = "admin",
            TaskPromptMarkdown = taskPrompt,
            RecipientRawText = "Ms Jane Doe, Community Nurse",
            StimulusPdfMediaAssetId = pdfAssetId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.WritingScenarios.Add(scenario);
        for (var i = 1; i <= sentenceCount; i++)
        {
            db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
            {
                Id = Guid.NewGuid(),
                ScenarioId = scenario.Id,
                Ordinal = i,
                SentenceText = $"Case note sentence {i}.",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        return scenario;
    }

    private static MediaAsset PdfAsset(string id, string storagePath)
        => new()
        {
            Id = id,
            OriginalFilename = "stimulus.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            SizeBytes = 1024,
            StoragePath = storagePath,
            Status = MediaAssetStatus.Ready,
            MediaKind = "document",
            UploadedBy = "admin",
            UploadedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Scan_PassesCompleteTasks_AndFlagsMissingPromptCaseNotesAndMedia()
    {
        await using var db = NewContext();
        db.WritingAssessmentPackVersions.Add(new WritingAssessmentPackVersion
        {
            Id = Guid.NewGuid(),
            Profession = "nursing",
            LetterType = "discharge",
            VersionKey = "nursing-discharge-test",
            Status = WritingAssessmentReleaseStatus.Approved,
            CandidateFacing = true,
            ApprovedAt = DateTimeOffset.UtcNow,
        });
        db.MediaAssets.Add(PdfAsset("pdf-complete", PdfKey));
        db.MediaAssets.Add(PdfAsset("pdf-object-gone", "writing/stimulus/deleted.pdf"));

        var complete = Scenario(db, "A complete with PDF", pdfAssetId: "pdf-complete");
        var completeNoPdf = Scenario(db, "B complete prompt only");
        var noPrompt = Scenario(db, "C missing prompt", taskPrompt: null);
        var noCaseNotes = Scenario(db, "D zero case notes", sentenceCount: 0);
        var missingAsset = Scenario(db, "E missing media asset", pdfAssetId: "pdf-never-uploaded");
        var missingObject = Scenario(db, "F missing storage object", pdfAssetId: "pdf-object-gone");
        Scenario(db, "G draft is not scanned", status: "draft", taskPrompt: null, sentenceCount: 0);

        db.WritingTaskModelAnswers.Add(new WritingTaskModelAnswer
        {
            Id = Guid.NewGuid(),
            ScenarioId = complete.Id,
            Status = WritingAssessmentModelAnswerStatus.Ready,
            IsCandidateVisible = true,
            ValidatorVersion = WritingRuleEngine.ValidatorVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = new WritingTaskLoadIntegrityService(
            db, new KeySetFileStorage(PdfKey), new RulebookLoader(), TimeProvider.System);

        var report = await service.ScanPublishedAsync(CancellationToken.None);

        Assert.Equal(WritingRuleEngine.ValidatorVersion, report.ValidatorVersion);
        Assert.Equal(6, report.Total);
        Assert.Equal(2, report.LoadPassed);
        Assert.Equal(4, report.LoadFailed);
        Assert.Equal(1, report.ModelAnswerVerified);
        Assert.Equal(new WritingTaskLoadIntegrityProfessionSummary(6, 2, 4), report.ByProfession["nursing"]);

        var byId = report.Rows.ToDictionary(r => r.ScenarioId);

        var passed = byId[complete.Id];
        Assert.True(passed.LoadOk);
        Assert.Empty(passed.Failures);
        Assert.Null(passed.Exception);
        Assert.True(passed.Checks.LearnerProjection);
        Assert.True(passed.Checks.StimulusPdf);
        Assert.True(passed.Checks.Rulebook);
        Assert.True(passed.Checks.AssessmentPack);
        Assert.True(passed.Checks.ModelAnswerVerified);

        // No Model Answer is a separate gate: still loadable.
        var passedNoAnswer = byId[completeNoPdf.Id];
        Assert.True(passedNoAnswer.LoadOk);
        Assert.False(passedNoAnswer.Checks.ModelAnswerVerified);

        Assert.False(byId[noPrompt.Id].LoadOk);
        Assert.Contains("task_prompt_missing", byId[noPrompt.Id].Failures);
        Assert.False(byId[noPrompt.Id].Checks.TaskPrompt);

        Assert.False(byId[noCaseNotes.Id].LoadOk);
        Assert.Contains("case_notes_missing", byId[noCaseNotes.Id].Failures);
        Assert.False(byId[noCaseNotes.Id].Checks.CaseNotes);

        Assert.Equal("stimulus_pdf_asset_missing", Assert.Single(byId[missingAsset.Id].Failures));
        Assert.Equal("stimulus_pdf_object_missing", Assert.Single(byId[missingObject.Id].Failures));
    }

    /// <summary>Storage double: only the given keys exist.</summary>
    private sealed class KeySetFileStorage(params string[] keys) : IFileStorage
    {
        private readonly HashSet<string> _keys = new(keys, StringComparer.Ordinal);

        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => Task.FromResult(0L);
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(_keys.Contains(key));
        public Task<bool> DeleteAsync(string key, CancellationToken ct) => Task.FromResult(false);
        public Task<long> LengthAsync(string key, CancellationToken ct) => Task.FromResult(0L);
        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct) => Task.CompletedTask;
        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct) => Task.FromResult(0);
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
    }
}
