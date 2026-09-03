using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests;

public sealed class M2AdversarialChallengerTests
{
    // =========================================================================
    // 1. SCORE CONVERSION TABLES & OBJECTIVE SCORING ADVERSARIAL TESTS
    // =========================================================================

    [Fact]
    public void ScoreTableValidator_Valid43Rows_Passes()
    {
        var rows = Enumerable.Range(0, 43)
            .Select(r => new AssessmentScoreTableRowInput(
                RawScore: r,
                ConvertedScore: OetScoring.OetRawToScaled(r),
                Grade: OetScoring.OetGradeLetterFromScaled(OetScoring.OetRawToScaled(r)),
                Passed: r >= 30))
            .ToList();

        var result = AssessmentScoreTableValidator.Validate("listening", rows);
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorCode);

        var resultReading = AssessmentScoreTableValidator.Validate("reading", rows);
        Assert.True(resultReading.IsValid);
    }

    [Theory]
    [InlineData("writing")]
    [InlineData("speaking")]
    [InlineData("toefl")]
    [InlineData("")]
    [InlineData(" ")]
    public void ScoreTableValidator_UnsupportedAssessment_Fails(string assessment)
    {
        var rows = Enumerable.Range(0, 43)
            .Select(r => new AssessmentScoreTableRowInput(r, r * 10, "B", true))
            .ToList();

        var result = AssessmentScoreTableValidator.Validate(assessment, rows);
        Assert.False(result.IsValid);
        Assert.Equal("assessment_unsupported", result.ErrorCode);
    }

    [Fact]
    public void ScoreTableValidator_MissingRows_Fails()
    {
        // 42 rows instead of 43 (missing rawScore 15)
        var rows = Enumerable.Range(0, 43)
            .Where(r => r != 15)
            .Select(r => new AssessmentScoreTableRowInput(r, OetScoring.OetRawToScaled(r), "B", r >= 30))
            .ToList();

        var result = AssessmentScoreTableValidator.Validate("listening", rows);
        Assert.False(result.IsValid);
        Assert.Equal("score_table_requires_43_rows", result.ErrorCode);
    }

    [Fact]
    public void ScoreTableValidator_ExtraRows_Fails()
    {
        // 44 rows (including rawScore 43)
        var rows = Enumerable.Range(0, 44)
            .Select(r => new AssessmentScoreTableRowInput(r, Math.Min(500, r * 12), "B", true))
            .ToList();

        var result = AssessmentScoreTableValidator.Validate("reading", rows);
        Assert.False(result.IsValid);
        Assert.Equal("score_table_requires_43_rows", result.ErrorCode);
    }

    [Fact]
    public void ScoreTableValidator_DuplicateRawScores_Fails()
    {
        // 43 rows, but two rows with rawScore = 10, and missing rawScore = 11
        var rows = Enumerable.Range(0, 43).Select(r =>
        {
            var raw = r == 11 ? 10 : r;
            return new AssessmentScoreTableRowInput(raw, OetScoring.OetRawToScaled(raw), "B", raw >= 30);
        }).ToList();

        var result = AssessmentScoreTableValidator.Validate("listening", rows);
        Assert.False(result.IsValid);
        Assert.Equal("score_table_duplicate_raw_score", result.ErrorCode);
    }

    [Fact]
    public void ScoreTableValidator_OutOfOrderRawScores_StillValidIfComplete()
    {
        // Permuted order of all 43 rows (0..42)
        var rng = new Random(42);
        var rows = Enumerable.Range(0, 43)
            .Select(r => new AssessmentScoreTableRowInput(r, OetScoring.OetRawToScaled(r), "B", r >= 30))
            .OrderBy(_ => rng.Next())
            .ToList();

        var result = AssessmentScoreTableValidator.Validate("listening", rows);
        Assert.True(result.IsValid, "Validator must correctly validate all 0..42 rows regardless of input ordering");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(501)]
    [InlineData(999)]
    public void ScoreTableValidator_ConvertedScoreOutOfRange_Fails(int badConvertedScore)
    {
        var rows = Enumerable.Range(0, 43)
            .Select(r => new AssessmentScoreTableRowInput(r, r == 20 ? badConvertedScore : OetScoring.OetRawToScaled(r), "B", r >= 30))
            .ToList();

        var result = AssessmentScoreTableValidator.Validate("listening", rows);
        Assert.False(result.IsValid);
        Assert.Equal("score_table_converted_score_out_of_range", result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ScoreTableValidator_MissingGrade_Fails(string? badGrade)
    {
        var rows = Enumerable.Range(0, 43)
            .Select(r => new AssessmentScoreTableRowInput(r, OetScoring.OetRawToScaled(r), r == 10 ? badGrade : "B", r >= 30))
            .ToList();

        var result = AssessmentScoreTableValidator.Validate("listening", rows);
        Assert.False(result.IsValid);
        Assert.Equal("score_table_requires_grade_and_pass_decision", result.ErrorCode);
    }

    [Fact]
    public void ScoreTableValidator_MissingPassedFlag_Fails()
    {
        var rows = Enumerable.Range(0, 43)
            .Select(r => new AssessmentScoreTableRowInput(r, OetScoring.OetRawToScaled(r), "B", r == 10 ? null : (bool?)(r >= 30)))
            .ToList();

        var result = AssessmentScoreTableValidator.Validate("listening", rows);
        Assert.False(result.IsValid);
        Assert.Equal("score_table_requires_grade_and_pass_decision", result.ErrorCode);
    }

    [Fact]
    public void OetScoring_MonotonicityAndInvariants_AcrossEntire0To42Range()
    {
        Assert.Equal(0, OetScoring.OetRawToScaled(0));
        Assert.Equal(350, OetScoring.OetRawToScaled(30));
        Assert.Equal(500, OetScoring.OetRawToScaled(42));

        var prev = -1;
        for (int r = 0; r <= 42; r++)
        {
            var scaled = OetScoring.OetRawToScaled(r);
            Assert.True(scaled >= prev, $"Monotonicity failed at raw score {r}: scaled={scaled} < prev={prev}");
            Assert.True(scaled >= 0 && scaled <= 500, $"Scaled score {scaled} at raw {r} out of bounds [0, 500]");

            if (r < 30)
                Assert.True(scaled < 350, $"Raw {r} (< 30) produced scaled {scaled} >= 350");
            else
                Assert.True(scaled >= 350, $"Raw {r} (>= 30) produced scaled {scaled} < 350");

            prev = scaled;
        }
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(-1, 0)]
    [InlineData(43, 500)]
    [InlineData(100, 500)]
    public void OetScoring_ClampingBoundaryValues(int raw, int expectedScaled)
    {
        Assert.Equal(expectedScaled, OetScoring.OetRawToScaled(raw));
    }

    // =========================================================================
    // 2. AI GATEWAY SHA-256 DETERMINISM & PII DEFENSE
    // =========================================================================

    [Fact]
    public void AiGateway_BuildRequestHash_IsDeterministicAcrossIdenticalCalls()
    {
        var request1 = new AiGatewayRequest
        {
            FeatureCode = "writing.grade",
            UserId = "user_12345",
            TenantId = "tenant_main",
            Provider = "anthropic",
            Model = "claude-sonnet-4-6",
            Temperature = 0.2f,
            MaxTokens = 2048,
            AssessmentContext = AiAssessmentContext.Practice,
            PromptTemplateId = "tpl_writing_v1",
            Prompt = new AiGroundedPrompt
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\nRole: Grader",
                TaskInstruction = "Grade candidate writing sample.",
                Metadata = new AiGroundedPromptMetadata { RulebookVersion = "v2.1", AppliedRulesCount = 6 }
            },
            UserInput = "Dear Doctor, I am writing to refer...",
            ResourceId = "paper_01",
            ResourceType = "writing_task",
            ResourceVersion = 1
        };

        var request2 = new AiGatewayRequest
        {
            FeatureCode = "writing.grade",
            UserId = "user_12345",
            TenantId = "tenant_main",
            Provider = "anthropic",
            Model = "claude-sonnet-4-6",
            Temperature = 0.2f,
            MaxTokens = 2048,
            AssessmentContext = AiAssessmentContext.Practice,
            PromptTemplateId = "tpl_writing_v1",
            Prompt = new AiGroundedPrompt
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\nRole: Grader",
                TaskInstruction = "Grade candidate writing sample.",
                Metadata = new AiGroundedPromptMetadata { RulebookVersion = "v2.1", AppliedRulesCount = 6 }
            },
            UserInput = "Dear Doctor, I am writing to refer...",
            ResourceId = "paper_01",
            ResourceType = "writing_task",
            ResourceVersion = 1
        };

        var hash1 = CoordinatedAiGatewayService.BuildRequestHash(request1);
        var hash2 = CoordinatedAiGatewayService.BuildRequestHash(request2);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // 64-char hex SHA-256
    }

    [Fact]
    public void AiGateway_BuildRequestHash_IsSensitiveToByteLevelMutation()
    {
        var baseRequest = new AiGatewayRequest
        {
            FeatureCode = "writing.grade",
            UserId = "user_12345",
            Provider = "anthropic",
            Model = "claude-sonnet-4-6",
            Temperature = 0.2f,
            Prompt = new AiGroundedPrompt
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\nRole: Grader",
                TaskInstruction = "Grade candidate writing sample."
            },
            UserInput = "Dear Doctor, I am writing to refer Mr. Smith."
        };

        var mutatedRequest = new AiGatewayRequest
        {
            FeatureCode = "writing.grade",
            UserId = "user_12345",
            Provider = "anthropic",
            Model = "claude-sonnet-4-6",
            Temperature = 0.2f,
            Prompt = new AiGroundedPrompt
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt\nRole: Grader",
                TaskInstruction = "Grade candidate writing sample."
            },
            UserInput = "Dear Doctor, I am writing to refer Mr. Smith!" // 1 char changed
        };

        var hash1 = CoordinatedAiGatewayService.BuildRequestHash(baseRequest);
        var hash2 = CoordinatedAiGatewayService.BuildRequestHash(mutatedRequest);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void AiGateway_BuildRequestHash_NormalizesWhitespaceAndCasing()
    {
        var reqA = new AiGatewayRequest
        {
            FeatureCode = "WRITING.GRADE",
            Provider = "  ANTHROPIC  ",
            Model = "CLAUDE-SONNET-4-6",
            Prompt = new AiGroundedPrompt { SystemPrompt = "OET AI — Rulebook-Grounded System Prompt" }
        };

        var reqB = new AiGatewayRequest
        {
            FeatureCode = "writing.grade",
            Provider = "anthropic",
            Model = "claude-sonnet-4-6",
            Prompt = new AiGroundedPrompt { SystemPrompt = "OET AI — Rulebook-Grounded System Prompt" }
        };

        Assert.Equal(CoordinatedAiGatewayService.BuildRequestHash(reqA), CoordinatedAiGatewayService.BuildRequestHash(reqB));
    }

    // =========================================================================
    // 3. STORAGE ABSTRACTION TRAVERSAL DEFENSE ADVERSARIAL TESTS
    // =========================================================================

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\..\\windows\\system32\\cmd.exe")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("C:/Windows/System32")]
    [InlineData("\\\\server\\share\\file")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("uploads/../../evil.txt")]
    [InlineData("uploads/../evil.txt")]
    [InlineData("uploads/./staging/file.bin")]
    [InlineData("uploads/staging/../evil.txt")]
    [InlineData("uploads/staging/../../etc/passwd")]
    [InlineData("uploads/file:stream.bin")]
    [InlineData("uploads/C:/test.bin")]
    [InlineData("uploads/safe/../../../../../../boot.ini")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LocalFileStorage_RejectsAdversarialTraversalPayloads(string maliciousKey)
    {
        using var temp = new TempDirectory();
        var environment = new MockHostingEnvironment(temp.Path);
        var options = Options.Create(new StorageOptions
        {
            Provider = "local",
            LocalRootPath = temp.Path
        });
        var storage = new LocalFileStorage(environment, options);
        await using var payload = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            storage.WriteAsync(maliciousKey, payload, default));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            storage.OpenReadAsync(maliciousKey, default));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            storage.OpenWriteAsync(maliciousKey, default));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            storage.ExistsAsync(maliciousKey, default));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            storage.LengthAsync(maliciousKey, default));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            storage.DeleteAsync(maliciousKey, default));
    }

    // Helper double
    private sealed class MockHostingEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = contentRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "OetLearner.Api";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Development";
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"oet-challenger-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
