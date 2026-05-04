namespace OetLearner.Api.Services;

/// <summary>
/// PTE Academic scoring engine. Per docs/product-strategy/06_feature_strategy_and_blueprint.md:
/// PTE is deferred, but the scoring foundation (10-90 scale, item-type partial credit,
/// communicative/enabling skills, and overall score computation) is required for the
/// shared-core exam-family abstraction to be complete.
/// </summary>
public interface IPteScoring
{
    int ClampScore(int rawScore);
    int ScaleToPte(double rawScore, int maxRaw, int minPte = 10, int maxPte = 90);
    PteCommunicativeSkills EvaluateCommunicativeSkills(PteModuleRawScores raw);
    PteEnablingSkills EvaluateEnablingSkills(PteEnablingRawScores raw);
    PteOverallResult ComputeOverall(PteCommunicativeSkills communicative, PteEnablingSkills enabling);
    string GetSkillLevel(int pteScore);
    bool IsPassing(int pteScore, int threshold = 65);
}

public sealed record PteModuleRawScores(
    double ListeningRaw,      // out of 90
    double ReadingRaw,        // out of 90
    double SpeakingRaw,       // out of 90
    double WritingRaw);       // out of 90

public sealed record PteEnablingRawScores(
    double GrammarRaw,        // out of 90
    double OralFluencyRaw,    // out of 90
    double PronunciationRaw,  // out of 90
    double SpellingRaw,       // out of 90
    double VocabularyRaw,     // out of 90
    double WrittenDiscourseRaw); // out of 90

public sealed record PteCommunicativeSkills(
    int Listening,
    int Reading,
    int Speaking,
    int Writing);

public sealed record PteEnablingSkills(
    int Grammar,
    int OralFluency,
    int Pronunciation,
    int Spelling,
    int Vocabulary,
    int WrittenDiscourse);

public sealed record PteOverallResult(
    int Overall,
    PteCommunicativeSkills Communicative,
    PteEnablingSkills Enabling,
    string Level,
    bool IsPassing);

public sealed class PteScoring(ILogger<PteScoring> logger) : IPteScoring
{
    public int ClampScore(int rawScore)
    {
        return Math.Clamp(rawScore, 10, 90);
    }

    public int ScaleToPte(double rawScore, int maxRaw, int minPte = 10, int maxPte = 90)
    {
        if (maxRaw <= 0) return minPte;
        var normalized = rawScore / maxRaw;
        var scaled = minPte + (normalized * (maxPte - minPte));
        return ClampScore((int)Math.Round(scaled));
    }

    public PteCommunicativeSkills EvaluateCommunicativeSkills(PteModuleRawScores raw)
    {
        var listening = ScaleToPte(raw.ListeningRaw, 90);
        var reading = ScaleToPte(raw.ReadingRaw, 90);
        var speaking = ScaleToPte(raw.SpeakingRaw, 90);
        var writing = ScaleToPte(raw.WritingRaw, 90);

        logger.LogInformation("PTE communicative skills: L={Listening} R={Reading} S={Speaking} W={Writing}",
            listening, reading, speaking, writing);

        return new PteCommunicativeSkills(listening, reading, speaking, writing);
    }

    public PteEnablingSkills EvaluateEnablingSkills(PteEnablingRawScores raw)
    {
        var grammar = ScaleToPte(raw.GrammarRaw, 90);
        var fluency = ScaleToPte(raw.OralFluencyRaw, 90);
        var pronunciation = ScaleToPte(raw.PronunciationRaw, 90);
        var spelling = ScaleToPte(raw.SpellingRaw, 90);
        var vocabulary = ScaleToPte(raw.VocabularyRaw, 90);
        var discourse = ScaleToPte(raw.WrittenDiscourseRaw, 90);

        return new PteEnablingSkills(grammar, fluency, pronunciation, spelling, vocabulary, discourse);
    }

    public PteOverallResult ComputeOverall(PteCommunicativeSkills communicative, PteEnablingSkills enabling)
    {
        // PTE overall is the average of all 10 scores (4 communicative + 6 enabling)
        var allScores = new[]
        {
            communicative.Listening, communicative.Reading, communicative.Speaking, communicative.Writing,
            enabling.Grammar, enabling.OralFluency, enabling.Pronunciation, enabling.Spelling,
            enabling.Vocabulary, enabling.WrittenDiscourse
        };

        var overall = (int)Math.Round(allScores.Average());
        overall = ClampScore(overall);

        var level = GetSkillLevel(overall);
        var passing = IsPassing(overall);

        return new PteOverallResult(overall, communicative, enabling, level, passing);
    }

    public string GetSkillLevel(int pteScore)
    {
        return pteScore switch
        {
            >= 85 => "Expert",
            >= 79 => "Very Good",
            >= 65 => "Good",
            >= 51 => "Moderate",
            >= 43 => "Competent",
            >= 30 => "Limited",
            _ => "Beginner"
        };
    }

    public bool IsPassing(int pteScore, int threshold = 65)
    {
        return pteScore >= threshold;
    }
}
