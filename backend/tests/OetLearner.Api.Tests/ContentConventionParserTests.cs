using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public class ContentConventionParserTests
{
    private readonly ContentConventionParser _parser = new();

    [Fact]
    public void Defaults_provenance_for_imported_papers()
    {
        var paths = new[]
        {
            "Reading ( IMPORTANT NOTE = Same for All Professions )/Reading Sample 1/Part A Reading ( Diarrhea & Dehydration in Children ).pdf",
        };

        var m = _parser.Parse(paths);
        var paper = Assert.Single(m.Papers);
        Assert.Equal(ContentDefaults.DefaultSourceProvenance, paper.SourceProvenance);
    }

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
        var a = p.Assets.First(x => x.Part == "A");
        Assert.Equal(PaperAssetRole.QuestionPaper, a.Role);
        Assert.Contains(p.Assets, asset => asset.Role == PaperAssetRole.QuestionPaper
            && (asset.Part == "B+C" || asset.Part == "B" || asset.Part == "C"));
    }

    [Fact]
    public void Recognises_real_medicine_reading_answer_question_text_booklet_and_part_bc_files()
    {
        var paths = new[]
        {
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Reading ( IMPORTANT NOTE = Same for All Professions )/Reading Sample 1/Answers ( Part A Reading ).pdf",
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Reading ( IMPORTANT NOTE = Same for All Professions )/Reading Sample 1/Question Paper ( Part A Reading ).pdf",
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Reading ( IMPORTANT NOTE = Same for All Professions )/Reading Sample 1/Text Booklet ( Part A Reading ).pdf",
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Reading ( IMPORTANT NOTE = Same for All Professions )/Reading Sample 1/Reading Part B&C _.pdf",
        };

        var manifest = _parser.Parse(paths);
        var paper = Assert.Single(manifest.Papers);

        Assert.Equal("reading", paper.SubtestCode);
        Assert.True(paper.AppliesToAllProfessions);
        Assert.Contains(paper.Assets, a => a.Role == PaperAssetRole.AnswerKey && a.Part == "A");
        Assert.Contains(paper.Assets, a => a.Role == PaperAssetRole.QuestionPaper && a.Part == "A");
        Assert.Contains(paper.Assets, a => a.Role == PaperAssetRole.Supplementary && a.Part == "A");
        Assert.Contains(paper.Assets, a => a.Role == PaperAssetRole.QuestionPaper && (a.Part == "B+C" || a.Part == "B" || a.Part == "C"));
        Assert.DoesNotContain(paper.ReadinessIssues, issue => issue.Severity == "error");
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
        Assert.Contains(w3.Assets, a => a.Role == PaperAssetRole.CaseNotes);
        Assert.Contains(w3.Assets, a => a.Role == PaperAssetRole.ModelAnswer);
        Assert.DoesNotContain(w3.ReadinessIssues, issue => issue.Severity == "error");

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
    public void Recognises_real_medicine_reference_files_and_uses_shared_speaking_resources_for_readiness()
    {
        var paths = new[]
        {
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Listening ( IMPORTANT NOTE =  Same for All Professions )/Listening Rulebook for Paper & Computer Based Exams ( IMPORTANT NOTE=THE SAME FOR ALL PROFESSIONS )/OET Listening Rulebook for Both Paper & Computer Based ( IMPORTANT NOTE-THE SAME FOR ALL PROFESSIONS ).pdf",
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Reading ( IMPORTANT NOTE = Same for All Professions )/Reading Rulebook for Paper & Computer Based Exams ( IMPORTANT NOTE=THE SAME FOR ALL PROFESSIONS )/OET Reading Rulebook for Both Paper & Computer Based ( IMPORTANT NOTE-THE SAME FOR ALL PROFESSIONS ).pdf",
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Speaking_/Speaking Assessment Criteria  ( IMPORTANT NOTE = same for all professions ).pdf",
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Speaking_/Speaking Intro Questions - Warm Up Questions - ( IMPORTANT NOTE = same for all professions ).pdf",
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Speaking_/Speaking Rulebook ( Medicine Only )/OET_Speaking_Rulebook ( For Medicine only ).pdf",
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Writing_/Writing RuleBook ( Medicine only )/OET_Writing_Rulebook_FINAL ( For Medicine Only ).pdf",
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Scoring System.txt",
            "OET with Dr. Ahmed Hesham ( Medicine Only )/Speaking_/Card 4 ( Examination Card )_ MOST IMPORTANT TYPE/4.pdf",
        };

        var manifest = _parser.Parse(paths);
        var speakingPaper = Assert.Single(manifest.Papers);

        Assert.Equal("speaking", speakingPaper.SubtestCode);
        Assert.Equal("examination", speakingPaper.CardType);
        Assert.DoesNotContain(speakingPaper.ReadinessIssues, issue => issue.Severity == "error");
        Assert.Contains(manifest.References, r => r.Target == ImportReferenceTargets.SpeakingSharedResource && r.SharedResourceKind == SpeakingSharedResourceKinds.AssessmentCriteria);
        Assert.Contains(manifest.References, r => r.Target == ImportReferenceTargets.SpeakingSharedResource && r.SharedResourceKind == SpeakingSharedResourceKinds.WarmUpQuestions);
        Assert.Contains(manifest.References, r => r.Target == ImportReferenceTargets.RulebookReferencePdf && r.Kind == "listening");
        Assert.Contains(manifest.References, r => r.Target == ImportReferenceTargets.RulebookReferencePdf && r.Kind == "reading");
        Assert.Contains(manifest.References, r => r.Target == ImportReferenceTargets.RulebookReferencePdf && r.Kind == "speaking");
        Assert.Contains(manifest.References, r => r.Target == ImportReferenceTargets.RulebookReferencePdf && r.Kind == "writing");
        Assert.Contains(manifest.References, r => r.Target == ImportReferenceTargets.ScoringPolicyBody);
        Assert.Equal(paths.Length, manifest.Inventory.TotalFiles);
        Assert.Equal(8, manifest.Inventory.ClassifiedFileCount);
        Assert.Equal(0, manifest.Inventory.UnclassifiedFileCount);
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
