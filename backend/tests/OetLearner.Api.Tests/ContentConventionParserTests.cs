using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public class ContentConventionParserTests
{
    private readonly ContentConventionParser _parser = new();

    [Fact]
    public void Recognises_listening_sample_folder_and_roles()
    {
        var paths = new[]
        {
            "Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 1/Audio 1/Audio 1.mp3",
            "Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 1/Listening Sample 1 Question-Paper.pdf",
            "Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 1/Listening Sample 1 Audio-Script.pdf",
            "Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Sample 1/Listening Sample 1 Answer-Key.pdf",
        };
        var m = _parser.Parse(paths);
        Assert.Single(m.Papers);
        var p = m.Papers[0];
        Assert.Equal("listening", p.SubtestCode);
        Assert.True(p.AppliesToAllProfessions);
        Assert.Equal(4, p.Assets.Count);
        Assert.Contains(p.Assets, a => a.Role == PaperAssetRole.Audio);
        Assert.Contains(p.Assets, a => a.Role == PaperAssetRole.QuestionPaper);
        Assert.Contains(p.Assets, a => a.Role == PaperAssetRole.AudioScript);
        Assert.Contains(p.Assets, a => a.Role == PaperAssetRole.AnswerKey);
    }

    [Fact]
    public void Recognises_reading_part_A_and_part_BC()
    {
        var paths = new[]
        {
            "Reading ( IMPORTANT NOTE = Same for All Professions )/Reading Sample 1/Part A Reading ( Diarrhea & Dehydration in Children ).pdf",
            "Reading ( IMPORTANT NOTE = Same for All Professions )/Reading Sample 1/Reading Part B&C.pdf",
        };
        var m = _parser.Parse(paths);
        Assert.Single(m.Papers);
        var p = m.Papers[0];
        Assert.Equal("reading", p.SubtestCode);
        Assert.Equal(2, p.Assets.Count);
        var a = p.Assets.First(x => x.Part == "A");
        var bc = p.Assets.First(x => x.Part == "B+C");
        Assert.Equal(PaperAssetRole.QuestionPaper, a.Role);
        Assert.Equal(PaperAssetRole.QuestionPaper, bc.Role);
    }

    [Fact]
    public void Recognises_writing_letter_type_and_role_pairs()
    {
        var paths = new[]
        {
            "Writing_/Writing 1 ( Routine Referral )/Ms Sarah Miller - Case Notes.pdf",
            "Writing_/Writing 1 ( Routine Referral )/Ms Sarah Miller - Answer Sheet.pdf",
            "Writing_/Writing 3 ( Urgent Referral )/Leo Bennett - Urgent Referral.pdf",
            "Writing_/Writing 3 ( Urgent Referral )/Leo Bennett ( Urgent Referral ) - Answer Sheet.pdf",
            "Writing_/Writing 6 ( Transfer Letter )/Case Notes.pdf",
        };
        var m = _parser.Parse(paths);
        Assert.Equal(3, m.Papers.Count);

        var w1 = m.Papers.First(p => p.Title.Contains("Writing 1"));
        Assert.Equal("writing", w1.SubtestCode);
        Assert.Equal("routine_referral", w1.LetterType);
        Assert.Equal("medicine", w1.ProfessionId);
        Assert.Contains(w1.Assets, a => a.Role == PaperAssetRole.CaseNotes);
        Assert.Contains(w1.Assets, a => a.Role == PaperAssetRole.ModelAnswer);

        var w3 = m.Papers.First(p => p.Title.Contains("Writing 3"));
        Assert.Equal("urgent_referral", w3.LetterType);

        var w6 = m.Papers.First(p => p.Title.Contains("Writing 6"));
        Assert.Equal("transfer_letter", w6.LetterType);
    }

    [Fact]
    public void Recognises_speaking_card_types()
    {
        var paths = new[]
        {
            "Speaking_/Card 1 ( Already known Pt )/1.pdf",
            "Speaking_/Card 4 ( Examination Card )_ MOST IMPORTANT TYPE/4.pdf",
            "Speaking_/Card 5 ( First visit - Emergency Card )/5.pdf",
            "Speaking_/Card 6 ( First Visit )/Card 6.pdf",
        };
        var m = _parser.Parse(paths);
        Assert.Equal(4, m.Papers.Count);
        Assert.Equal("already_known_pt", m.Papers.First(p => p.Title.Contains("Card 1")).CardType);
        Assert.Equal("examination", m.Papers.First(p => p.Title.Contains("Card 4")).CardType);
        Assert.Equal("first_visit_emergency", m.Papers.First(p => p.Title.Contains("Card 5")).CardType);
        Assert.Equal("first_visit_routine", m.Papers.First(p => p.Title.Contains("Card 6")).CardType);
        Assert.All(m.Papers, p => Assert.Equal("medicine", p.ProfessionId));
    }

    [Fact]
    public void Flags_unclassified_flat_files_as_issues()
    {
        var paths = new[] { "mystery.pdf" };
        var m = _parser.Parse(paths);
        Assert.Empty(m.Papers);
        Assert.Single(m.Issues);
    }
}
