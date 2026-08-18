using OetLearner.Api.Domain;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Tests;

public sealed class ReadingPartALayoutTests
{
    private const string Sample5Headings = """
        Questions 1- 8
        For each question, 1-8, decide which text (A, B, C or D) the information comes from.
        Questions 9-14
        Answer each of the questions, 9-14, with a word or short phrase from one of the texts.
        Questions 15-20
        Complete each of the sentences, 15-20, with a word or short phrase from one of the texts.
        """;

    [Fact]
    public void Detects_sample5_one_to_eight_matching_from_booklet_text()
    {
        Assert.True(ReadingPartALayoutDetector.TryDetectFromBookletText(Sample5Headings, out var layout, out var error), error);
        Assert.Equal(8, layout.MatchingEnd);
        Assert.Equal(15, layout.LastStart);
        Assert.Equal(ReadingQuestionType.ShortAnswer, layout.MiddleType);
        Assert.Equal(ReadingQuestionType.SentenceCompletion, layout.LastType);
        Assert.Equal(ReadingQuestionType.MatchingTextReference, layout.TypeFor(8));
        Assert.Equal(ReadingQuestionType.ShortAnswer, layout.TypeFor(9));
    }

    [Fact]
    public void Detects_swapped_complete_then_answer_blocks()
    {
        const string swapped = """
            Questions 1-7
            For each question, 1-7, decide which text (A, B, C or D) the information comes from.
            Questions 8-14
            Complete each of the sentences, 8-14, with a word or short phrase from one of the texts.
            Questions 15-20
            Answer each of the questions, 15-20, with a word or short phrase from one of the texts.
            """;

        Assert.True(ReadingPartALayoutDetector.TryDetectFromBookletText(swapped, out var layout, out var error), error);
        Assert.Equal(7, layout.MatchingEnd);
        Assert.Equal(ReadingQuestionType.SentenceCompletion, layout.MiddleType);
        Assert.Equal(ReadingQuestionType.ShortAnswer, layout.LastType);
    }

    [Theory]
    [InlineData(7, ReadingQuestionType.ShortAnswer, ReadingQuestionType.SentenceCompletion)]
    [InlineData(7, ReadingQuestionType.SentenceCompletion, ReadingQuestionType.ShortAnswer)]
    [InlineData(8, ReadingQuestionType.ShortAnswer, ReadingQuestionType.SentenceCompletion)]
    [InlineData(8, ReadingQuestionType.SentenceCompletion, ReadingQuestionType.ShortAnswer)]
    public void Accepts_all_four_official_typed_layouts(
        int matchingEnd,
        ReadingQuestionType middle,
        ReadingQuestionType last)
    {
        var questions = Enumerable.Range(1, 20).Select(order => (
            order,
            order <= matchingEnd
                ? ReadingQuestionType.MatchingTextReference
                : order <= 14 ? middle : last));

        Assert.True(ReadingPartALayoutDetector.TryDetectFromQuestions(questions, out var layout, out var error), error);
        Assert.Equal(matchingEnd, layout.MatchingEnd);
        Assert.Equal(middle, layout.MiddleType);
        Assert.Equal(last, layout.LastType);
    }

    [Fact]
    public void Detects_official_sample2_last_block_starting_at_14()
    {
        const string headings = """
            Questions 1-7
            For each question, 1-7, decide which text (A, B, C or D) the information comes from.
            Questions 8-13
            Answer each of the questions, 8-13, with a word or short phrase from one of the texts.
            Questions 14-20
            Complete each of the sentences, 14-20, with a word or short phrase from one of the texts.
            """;

        Assert.True(ReadingPartALayoutDetector.TryDetectFromBookletText(headings, out var layout, out var error), error);
        Assert.Equal(7, layout.MatchingEnd);
        Assert.Equal(14, layout.LastStart);
        Assert.Equal(ReadingQuestionType.ShortAnswer, layout.MiddleType);
        Assert.Equal(ReadingQuestionType.SentenceCompletion, layout.LastType);
    }

    [Theory]
    [InlineData(5, 14)]
    [InlineData(6, 15)]
    public void Detects_shorter_matching_blocks(int matchingEnd, int lastStart)
    {
        var questions = Enumerable.Range(1, 20).Select(order => (
            order,
            order <= matchingEnd
                ? ReadingQuestionType.MatchingTextReference
                : order < lastStart
                    ? ReadingQuestionType.ShortAnswer
                    : ReadingQuestionType.SentenceCompletion));

        Assert.True(ReadingPartALayoutDetector.TryDetectFromQuestions(questions, out var layout, out var error), error);
        Assert.Equal(matchingEnd, layout.MatchingEnd);
        Assert.Equal(lastStart, layout.LastStart);
    }

    [Fact]
    public void Rejects_mixed_middle_block()
    {
        var questions = Enumerable.Range(1, 20).Select(order => (
            order,
            order <= 7
                ? ReadingQuestionType.MatchingTextReference
                : order == 8
                    ? ReadingQuestionType.SentenceCompletion
                    : order <= 14
                        ? ReadingQuestionType.ShortAnswer
                        : ReadingQuestionType.SentenceCompletion));

        Assert.False(ReadingPartALayoutDetector.TryDetectFromQuestions(questions, out _, out var error));
        Assert.Contains("15 or 16", error);
    }

    [Fact]
    public void Detects_jayden_eight_to_fifteen_short_answer_layout()
    {
        const string headings = """
            Questions 1-7
            For each question, 1-7, decide which text (A, B, C or D) the information comes from.
            Questions 8-15
            Answer each of the questions, 8-15, with a word or short phrase from one of the texts.
            Questions 16-20
            Complete each of the sentences, 16-20, with a word or short phrase from one of the texts.
            """;

        Assert.True(ReadingPartALayoutDetector.TryDetectFromBookletText(headings, out var fromBooklet, out var bookletError), bookletError);
        Assert.Equal(7, fromBooklet.MatchingEnd);
        Assert.Equal(16, fromBooklet.LastStart);
        Assert.Equal(ReadingQuestionType.ShortAnswer, fromBooklet.MiddleType);
        Assert.Equal(ReadingQuestionType.SentenceCompletion, fromBooklet.LastType);
        Assert.Equal(ReadingQuestionType.ShortAnswer, fromBooklet.TypeFor(15));
        Assert.Equal(ReadingQuestionType.SentenceCompletion, fromBooklet.TypeFor(16));

        var questions = Enumerable.Range(1, 20).Select(order => (
            order,
            order <= 7
                ? ReadingQuestionType.MatchingTextReference
                : order <= 15
                    ? ReadingQuestionType.ShortAnswer
                    : ReadingQuestionType.SentenceCompletion));
        Assert.True(ReadingPartALayoutDetector.TryDetectFromQuestions(questions, out var fromQuestions, out var questionError), questionError);
        Assert.Equal(16, fromQuestions.LastStart);
    }
}
