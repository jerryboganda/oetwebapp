using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

/// <summary>
/// Ensures the pronunciation rulebooks ship embedded and load correctly for
/// every supported profession. A failure here means the rulebook folder
/// structure or the schema's <c>kind</c> enum extension broke.
/// </summary>
public class PronunciationRulebookLoaderTests
{
    private readonly IRulebookLoader _loader = new RulebookLoader();

    [Theory]
    [InlineData(ExamProfession.Medicine)]
    [InlineData(ExamProfession.Nursing)]
    [InlineData(ExamProfession.Dentistry)]
    [InlineData(ExamProfession.Pharmacy)]
    [InlineData(ExamProfession.Physiotherapy)]
    [InlineData(ExamProfession.OccupationalTherapy)]
    [InlineData(ExamProfession.SpeechPathology)]
    public void Loads_Pronunciation_Rulebook_For_Each_Supported_Profession(ExamProfession profession)
    {
        var book = _loader.Load(RuleKind.Pronunciation, profession);
        Assert.NotNull(book);
        Assert.Equal(RuleKind.Pronunciation, book.Kind);
        Assert.Equal(profession, book.Profession);
        Assert.False(string.IsNullOrWhiteSpace(book.Version));
        Assert.NotEmpty(book.Sections);
        Assert.NotEmpty(book.Rules);
    }

    [Fact]
    public void Medicine_Rulebook_Contains_Core_Phoneme_Rules()
    {
        var book = _loader.Load(RuleKind.Pronunciation, ExamProfession.Medicine);
        Assert.Contains(book.Rules, r => r.Id == "P01.1"); // /θ/
        Assert.Contains(book.Rules, r => r.Id == "P01.3"); // /v/
        Assert.Contains(book.Rules, r => r.Id == "P02.3"); // trap vowel /æ/
        Assert.Contains(book.Rules, r => r.Id == "P04.1"); // antepenultimate stress
        Assert.Contains(book.Rules, r => r.Id == "P06.1"); // yes/no intonation
    }

    [Fact]
    public void FindRule_Returns_The_Matching_Rule_By_Id()
    {
        var rule = _loader.FindRule(RuleKind.Pronunciation, ExamProfession.Medicine, "P01.1");
        Assert.NotNull(rule);
        Assert.Equal(RuleSeverity.Critical, rule!.Severity);
    }
}
