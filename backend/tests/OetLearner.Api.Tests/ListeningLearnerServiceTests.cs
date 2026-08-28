using System.Reflection;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests;

/// <summary>
/// Unit tests for the static helpers on <see cref="ListeningLearnerService"/>.
/// Today this only covers <c>ResolveAccessTier</c>; the broader integration
/// surface lives in <see cref="ListeningRelationalRuntimeTests"/>.
/// </summary>
public class ListeningLearnerServiceTests
{
    [Theory]
    [InlineData("access:free", "free")]
    [InlineData("medicine,access:free", "free")]
    [InlineData("access:preview-first-extract,medicine", "preview")]
    [InlineData("access:preview", "preview")]
    [InlineData("ACCESS:FREE", "free")]
    [InlineData("medicine,nursing", "premium")]
    [InlineData("", "premium")]
    [InlineData(null, "premium")]
    public void ResolveAccessTier_MapsTokensToTiers(string? tagsCsv, string expected)
    {
        Assert.Equal(expected, ListeningLearnerService.ResolveAccessTier(tagsCsv));
    }

    [Fact]
    public void PaperHomeDto_IncludesSubscriptionGateAndAccessTier()
    {
        var paper = new ContentPaper
        {
            Id = "listen-paper-locked",
            Title = "Premium Listening Paper",
            Slug = "premium-listening-paper",
            Difficulty = "medium",
            EstimatedDurationMinutes = 42,
            TagsCsv = "medicine,access:premium",
            ExtractedTextJson = """{"listeningQuestions":[{"id":"q1"}]}""",
            Assets =
            [
                new ContentPaperAsset { Role = PaperAssetRole.Audio, IsPrimary = true },
                new ContentPaperAsset { Role = PaperAssetRole.QuestionPaper, IsPrimary = true },
                new ContentPaperAsset { Role = PaperAssetRole.AnswerKey, IsPrimary = true },
                new ContentPaperAsset { Role = PaperAssetRole.AudioScript, IsPrimary = true }
            ]
        };

        var method = typeof(ListeningLearnerService).GetMethod("PaperHomeDto", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var dto = method.Invoke(null, [paper, null, 0, true, 0, 0, 0]);
        Assert.NotNull(dto);

        var dtoType = dto.GetType();
        Assert.True((bool)dtoType.GetProperty("requiresSubscription")!.GetValue(dto)!);
        Assert.Equal("premium", dtoType.GetProperty("accessTier")!.GetValue(dto));
        Assert.Equal(1, dtoType.GetProperty("questionCount")!.GetValue(dto));
        Assert.Equal("medicine,access:premium", dtoType.GetProperty("tagsCsv")!.GetValue(dto));
        Assert.Equal(1, dtoType.GetProperty("partACount")!.GetValue(dto));
        Assert.Equal(0, dtoType.GetProperty("partBCount")!.GetValue(dto));
        Assert.Equal(0, dtoType.GetProperty("partCCount")!.GetValue(dto));
        Assert.Equal("/listening/paper/listen-paper-locked", dtoType.GetProperty("route")!.GetValue(dto));
    }

    [Fact]
    public void PaperHomeDto_UsesCompleteJsonQuestionSetWhenRelationalCountsArePartial()
    {
        var questions = Enumerable.Range(1, 42)
            .Select(number => new
            {
                id = $"q{number}",
                number,
                partCode = number <= 24 ? "A1" : number <= 30 ? "B1" : number <= 36 ? "C1" : "C2",
                stem = $"Authored question {number}",
                type = "multiple_choice_3",
                options = new[] { "A", "B", "C" },
                correctAnswer = "A"
            })
            .ToArray();
        var paper = new ContentPaper
        {
            Id = "listen-paper-partial-relational",
            Title = "Listening paper with complete source JSON",
            Slug = "listening-paper-partial-relational",
            ExtractedTextJson = JsonSerializer.Serialize(new { listeningQuestions = questions }),
            Assets = []
        };

        var method = typeof(ListeningLearnerService).GetMethod("PaperHomeDto", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        // Simulate the legacy partial relational projection that previously
        // made the Part B/C dispatchers advertise only one item each.
        var dto = method.Invoke(null, [paper, null, 1, false, 24, 1, 1]);
        Assert.NotNull(dto);

        var dtoType = dto.GetType();
        Assert.Equal(42, dtoType.GetProperty("questionCount")!.GetValue(dto));
        Assert.Equal(24, dtoType.GetProperty("partACount")!.GetValue(dto));
        Assert.Equal(6, dtoType.GetProperty("partBCount")!.GetValue(dto));
        Assert.Equal(12, dtoType.GetProperty("partCCount")!.GetValue(dto));
    }

    [Theory]
    [InlineData("====== PAGE 4 ======\nPractice Test 1\nWhat is the nurse discussing with the patient?", "What is the nurse discussing with the patient?")]
    [InlineData("PAGE 5\nWhat is the nurse discussing with the patient?", "What is the nurse discussing with the patient?")]
    [InlineData("Practice Test 1\nWhat is the nurse discussing with the patient?", "What is the nurse discussing with the patient?")]
    [InlineData("See PDF", "")]
    [InlineData("CPDF", "")]
    [InlineData("PDF", "")]
    [InlineData("View PDF", "")]
    [InlineData("====== PAGE 4 ======", "")]
    [InlineData("PAGE 5", "")]
    [InlineData("Practice Test 1", "")]
    [InlineData("PAGE 4 Question 25 What is the patient's condition?", "Question 25 What is the patient's condition?")]
    [InlineData("Practice Test 1 : You hear a doctor talking to a nurse.", "You hear a doctor talking to a nurse.")]
    [InlineData("In general practice, hypertension is common.", "In general practice, hypertension is common.")]
    [InlineData("The patient presented with Paget disease.", "The patient presented with Paget disease.")]
    public void SanitizeQuestionPrompt_StripsArtifactsAndSentinels(string? input, string expected)
    {
        var result = ListeningLearnerService.SanitizeQuestionPrompt(input);
        Assert.Equal(expected, result);
        Assert.Equal(expected, ListeningLearnerService.CleanListeningPrompt(input));
    }

    [Theory]
    [InlineData("Option A", "")]
    [InlineData("Option B", "")]
    [InlineData("Option C", "")]
    [InlineData("See PDF", "")]
    [InlineData("====== PAGE 4 ======\nTake 500mg paracetamol", "Take 500mg paracetamol")]
    [InlineData("Take 500mg paracetamol orally", "Take 500mg paracetamol orally")]
    public void SanitizeOptionText_StripsPlaceholdersAndArtifacts(string? input, string expected)
    {
        var result = ListeningLearnerService.SanitizeOptionText(input);
        Assert.Equal(expected, result);
        Assert.Equal(expected, ListeningLearnerService.CleanListeningOption(input));
    }

    [Fact]
    public void MapRelationalQuestion_SanitizesStemAndOptions()
    {
        var relationalQuestion = new ListeningQuestion
        {
            Id = "lq-1",
            PaperId = "paper-1",
            QuestionNumber = 25,
            Stem = "====== PAGE 4 ======\nPractice Test 1\nWhat is the nurse discussing with the patient?",
            QuestionType = ListeningQuestionType.MultipleChoice3,
            Options =
            [
                new ListeningQuestionOption { OptionKey = "A", DisplayOrder = 0, Text = "Option A", IsCorrect = false },
                new ListeningQuestionOption { OptionKey = "B", DisplayOrder = 1, Text = "====== PAGE 4 ======\nSchedule follow-up", IsCorrect = true },
                new ListeningQuestionOption { OptionKey = "C", DisplayOrder = 2, Text = "See PDF", IsCorrect = false }
            ],
            CorrectAnswerJson = "\"B\""
        };

        var method = typeof(ListeningLearnerService).GetMethod("MapRelationalQuestion", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var mapped = method.Invoke(null, [relationalQuestion]);
        Assert.NotNull(mapped);

        var mappedType = mapped.GetType();
        var text = (string)mappedType.GetProperty("Text")!.GetValue(mapped)!;
        var options = (IReadOnlyList<string>)mappedType.GetProperty("Options")!.GetValue(mapped)!;

        Assert.Equal("What is the nurse discussing with the patient?", text);
        Assert.Equal(3, options.Count);
        Assert.Equal("", options[0]);
        Assert.Equal("Schedule follow-up", options[1]);
        Assert.Equal("", options[2]);
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(3, 4, true)]
    [InlineData(4, 3, true)]
    [InlineData(4, 4, true)]
    [InlineData(2, 3, false)]
    [InlineData(3, 2, false)]
    [InlineData(3, -1, false)]
    public void PartCQuestionScope_AllowsCrossExtractAnswerEditsOnlyWhilePartCIsActive(
        int currentCursor,
        int questionCursor,
        bool expected)
    {
        var method = typeof(ListeningLearnerService).GetMethod(
            "IsPartCQuestionScope",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (bool)method.Invoke(null, [currentCursor, questionCursor])!;
        Assert.Equal(expected, result);
    }
}
