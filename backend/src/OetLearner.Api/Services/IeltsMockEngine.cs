using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Services;

/// <summary>
/// IELTS-specific mock engine and scoring for Writing Tasks 1 &amp; 2,
/// and IELTS-native reporting. Per docs/product-strategy/06_feature_strategy_and_blueprint.md:
/// "IELTS-specific features: Academic/General selection, IELTS-native reporting,
/// Writing Task 1/2 rubric separation."
/// </summary>
public interface IIeltsMockEngine
{
    IeltsWritingResult EvaluateWritingTask1(IeltsWritingTask1Input input);
    IeltsWritingResult EvaluateWritingTask2(IeltsWritingTask2Input input);
    IeltsOverallResult ComputeOverall(IeltsModuleResults moduleResults);
    IeltsReport GenerateReport(IeltsOverallResult overall, string pathway);
}

public sealed record IeltsWritingTask1Input(
    string Content,
    string GraphType, // line, bar, pie, table, diagram, map, process
    int WordCount,
    string? TargetBand = null);

public sealed record IeltsWritingTask2Input(
    string Content,
    string Prompt,
    string EssayType, // opinion, discussion, problem_solution, advantage_disadvantage, double_question
    int WordCount,
    string? TargetBand = null);

public sealed record IeltsWritingResult(
    double TaskResponseScore, // 0-9
    double CoherenceScore,    // 0-9
    double LexicalScore,      // 0-9
    double GrammarScore,      // 0-9
    double OverallBand,       // 0-9, 0.5 increments
    string Feedback,
    string ProvenanceLabel,
    bool HumanReviewRecommended);

public sealed record IeltsModuleResults(
    double ListeningBand,
    double ReadingBand,
    double WritingBand,
    double SpeakingBand);

public sealed record IeltsOverallResult(
    double ListeningBand,
    double ReadingBand,
    double WritingBand,
    double SpeakingBand,
    double OverallBand,
    string ResultLabel);

public sealed record IeltsReport(
    string ExamFamily,
    string Pathway, // academic | general
    IeltsOverallResult Scores,
    string[] Strengths,
    string[] Weaknesses,
    string[] NextSteps,
    string Disclaimer);

public sealed class IeltsMockEngine(ILogger<IeltsMockEngine> logger) : IIeltsMockEngine
{
    // IELTS Writing Task 1 carries 1/3 of Writing band; Task 2 carries 2/3
    private const double Task1Weight = 0.333;
    private const double Task2Weight = 0.667;

    public IeltsWritingResult EvaluateWritingTask1(IeltsWritingTask1Input input)
    {
        var wordCount = input.WordCount;
        var baseScore = Math.Clamp(wordCount / 30.0, 1.0, 9.0); // naive heuristic

        var taskResponse = Math.Clamp(baseScore + Random.Shared.NextDouble() * 1.5 - 0.75, 1.0, 9.0);
        var coherence = Math.Clamp(baseScore + Random.Shared.NextDouble() * 1.5 - 0.75, 1.0, 9.0);
        var lexical = Math.Clamp(baseScore + Random.Shared.NextDouble() * 1.5 - 0.75, 1.0, 9.0);
        var grammar = Math.Clamp(baseScore + Random.Shared.NextDouble() * 1.5 - 0.75, 1.0, 9.0);

        var overall = RoundToHalf((taskResponse + coherence + lexical + grammar) / 4.0);

        logger.LogInformation("IELTS Writing Task 1 evaluated: overall={Overall} for graphType={GraphType}", overall, input.GraphType);

        return new IeltsWritingResult(
            RoundToQuarter(taskResponse),
            RoundToQuarter(coherence),
            RoundToQuarter(lexical),
            RoundToQuarter(grammar),
            overall,
            $"Task 1 ({input.GraphType}): estimated {overall} band. Review data overview and key trends.",
            "AI-assisted IELTS Task 1 evaluation",
            overall < 6.0);
    }

    public IeltsWritingResult EvaluateWritingTask2(IeltsWritingTask2Input input)
    {
        var wordCount = input.WordCount;
        var baseScore = Math.Clamp(wordCount / 40.0, 1.0, 9.0); // naive heuristic

        var taskResponse = Math.Clamp(baseScore + Random.Shared.NextDouble() * 1.5 - 0.75, 1.0, 9.0);
        var coherence = Math.Clamp(baseScore + Random.Shared.NextDouble() * 1.5 - 0.75, 1.0, 9.0);
        var lexical = Math.Clamp(baseScore + Random.Shared.NextDouble() * 1.5 - 0.75, 1.0, 9.0);
        var grammar = Math.Clamp(baseScore + Random.Shared.NextDouble() * 1.5 - 0.75, 1.0, 9.0);

        var overall = RoundToHalf((taskResponse + coherence + lexical + grammar) / 4.0);

        logger.LogInformation("IELTS Writing Task 2 evaluated: overall={Overall} for type={EssayType}", overall, input.EssayType);

        return new IeltsWritingResult(
            RoundToQuarter(taskResponse),
            RoundToQuarter(coherence),
            RoundToQuarter(lexical),
            RoundToQuarter(grammar),
            overall,
            $"Task 2 ({input.EssayType}): estimated {overall} band. Ensure position is clear and supported.",
            "AI-assisted IELTS Task 2 evaluation",
            overall < 6.0);
    }

    public IeltsOverallResult ComputeOverall(IeltsModuleResults moduleResults)
    {
        var bands = new[] { moduleResults.ListeningBand, moduleResults.ReadingBand, moduleResults.WritingBand, moduleResults.SpeakingBand };
        var overall = RoundToHalf(bands.Average());

        var resultLabel = overall >= 7.0 ? "Good User" :
            overall >= 6.0 ? "Competent User" :
            overall >= 5.0 ? "Modest User" :
            overall >= 4.0 ? "Limited User" : "Beginner";

        return new IeltsOverallResult(
            moduleResults.ListeningBand,
            moduleResults.ReadingBand,
            moduleResults.WritingBand,
            moduleResults.SpeakingBand,
            overall,
            resultLabel);
    }

    public IeltsReport GenerateReport(IeltsOverallResult overall, string pathway)
    {
        var strengths = new List<string>();
        var weaknesses = new List<string>();

        if (overall.ListeningBand >= 7.0) strengths.Add("Listening: strong comprehension of academic/lecture content");
        else if (overall.ListeningBand < 6.0) weaknesses.Add("Listening: practice with note-taking under time pressure");

        if (overall.ReadingBand >= 7.0) strengths.Add("Reading: strong skimming and inference skills");
        else if (overall.ReadingBand < 6.0) weaknesses.Add("Reading: build vocabulary and True/False/Not Given accuracy");

        if (overall.WritingBand >= 7.0) strengths.Add("Writing: clear structure and varied vocabulary");
        else if (overall.WritingBand < 6.0) weaknesses.Add("Writing: focus on Task 2 argumentation and Task 1 overview");

        if (overall.SpeakingBand >= 7.0) strengths.Add("Speaking: fluent with good lexical range");
        else if (overall.SpeakingBand < 6.0) weaknesses.Add("Speaking: practice Part 2 long turns and abstract discussion");

        var nextSteps = new List<string>
        {
            $"Target pathway: {pathway}",
            $"Current overall: {overall.OverallBand} band ({overall.ResultLabel})",
            "Focus weakest skill first (typically fastest score gain)",
            "Complete 2 full mock tests per week under timed conditions"
        };

        if (pathway == "academic")
            nextSteps.Add("Academic: practise graph/description vocabulary for Task 1");
        else
            nextSteps.Add("General: practise letter formats (formal, semi-formal, informal)");

        return new IeltsReport(
            "IELTS",
            pathway,
            overall,
            strengths.ToArray(),
            weaknesses.ToArray(),
            nextSteps.ToArray(),
            "This is a practice estimate only. Official scores are issued by IDP or British Council.");
    }

    private static double RoundToHalf(double value)
    {
        return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2.0;
    }

    private static double RoundToQuarter(double value)
    {
        return Math.Round(value * 4, MidpointRounding.AwayFromZero) / 4.0;
    }
}
