using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

public sealed class ListeningLearnerGradingPolicyTests
{
    [Fact]
    public async Task Snapshot_controls_internal_whitespace_without_creating_fuzzy_credit()
    {
        var grader = new ListeningLearnerGradingService(
            NullLogger<ListeningLearnerGradingService>.Instance);
        var question = new ListeningQuestion
        {
            Id = "q-1",
            QuestionType = ListeningQuestionType.ShortAnswer,
            CorrectAnswerJson = "\"asp irin\"",
            CaseSensitive = true,
        };
        var attempt = new ListeningQuestionAttempt { LearnerAnswer = "asp  irin" };

        var strict = await grader.GradeAttemptAsync(
            attempt,
            question,
            CancellationToken.None,
            new AssessmentMarkingPolicyDocument(CollapseInternalWhitespace: false));

        Assert.False(strict.IsCorrect);
        Assert.False(attempt.IsCorrect);

        var relaxedWhitespaceAttempt = new ListeningQuestionAttempt { LearnerAnswer = "asp  irin" };
        var collapsed = await grader.GradeAttemptAsync(
            relaxedWhitespaceAttempt,
            question,
            CancellationToken.None,
            new AssessmentMarkingPolicyDocument(CollapseInternalWhitespace: true));

        Assert.True(collapsed.IsCorrect);
    }

    [Fact]
    public async Task Only_canonical_or_explicit_variant_receives_credit()
    {
        var grader = new ListeningLearnerGradingService(
            NullLogger<ListeningLearnerGradingService>.Instance);
        var question = new ListeningQuestion
        {
            Id = "q-2",
            QuestionType = ListeningQuestionType.ShortAnswer,
            CorrectAnswerJson = "\"paracetamol\"",
            AcceptedSynonymsJson = "[\"acetaminophen\"]",
        };

        var variantAttempt = new ListeningQuestionAttempt { LearnerAnswer = "acetaminophen" };
        var variant = await grader.GradeAttemptAsync(variantAttempt, question, CancellationToken.None);
        Assert.True(variant.IsCorrect);

        var typoAttempt = new ListeningQuestionAttempt { LearnerAnswer = "paracetamoll" };
        var typo = await grader.GradeAttemptAsync(typoAttempt, question, CancellationToken.None);
        Assert.False(typo.IsCorrect);
        Assert.True(typo.IsMeaningCorrectSpellingWrong);
    }
}
