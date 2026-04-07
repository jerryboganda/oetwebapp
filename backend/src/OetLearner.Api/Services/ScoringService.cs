using OetLearner.Api.Data;

namespace OetLearner.Api.Services;

/// <summary>
/// Resolves score display formats and scale information by exam family.
/// OET: 0–500 grade scale (A–E).  IELTS: 0–9 band score.  PTE: 10–90.
/// </summary>
public class ScoringService
{
    /// <summary>
    /// Format a raw numeric score for display in the context of a specific exam family.
    /// </summary>
    public string FormatScoreDisplay(string examFamilyCode, double rawScore)
    {
        var code = (examFamilyCode ?? "oet").Trim().ToLowerInvariant();
        return code switch
        {
            "oet" => $"{Math.Round(rawScore)} / {OetGrade(rawScore)}",
            "ielts" => $"{IeltsBand(rawScore)} Band Score",
            "pte" => $"{Math.Round(rawScore)} / 90",
            _ => $"{Math.Round(rawScore)}"
        };
    }

    /// <summary>
    /// Return the score scale information for an exam family.
    /// </summary>
    public object GetScoreScale(string examFamilyCode)
    {
        var code = (examFamilyCode ?? "oet").Trim().ToLowerInvariant();
        return code switch
        {
            "oet" => new
            {
                examFamily = "OET",
                minScore = 0,
                maxScore = 500,
                gradeScale = "A–E",
                passingScore = 350,
                passingGrade = "B",
                grades = new[]
                {
                    new { grade = "A", minScore = 450, maxScore = 500, label = "Very High" },
                    new { grade = "B", minScore = 350, maxScore = 449, label = "High (Pass)" },
                    new { grade = "C+", minScore = 300, maxScore = 349, label = "Satisfactory" },
                    new { grade = "C", minScore = 200, maxScore = 299, label = "Adequate" },
                    new { grade = "D", minScore = 100, maxScore = 199, label = "Limited" },
                    new { grade = "E", minScore = 0, maxScore = 99, label = "Minimal" }
                }
            },
            "ielts" => new
            {
                examFamily = "IELTS",
                minScore = 0,
                maxScore = 9,
                gradeScale = "0–9 Band",
                passingScore = 7.0,
                passingGrade = "Band 7",
                grades = new[]
                {
                    new { grade = "Band 9", minScore = 9, maxScore = 9, label = "Expert" },
                    new { grade = "Band 8", minScore = 8, maxScore = 8, label = "Very Good" },
                    new { grade = "Band 7", minScore = 7, maxScore = 7, label = "Good (Pass)" },
                    new { grade = "Band 6", minScore = 6, maxScore = 6, label = "Competent" },
                    new { grade = "Band 5", minScore = 5, maxScore = 5, label = "Modest" },
                    new { grade = "Band 4", minScore = 4, maxScore = 4, label = "Limited" }
                }
            },
            "pte" => new
            {
                examFamily = "PTE",
                minScore = 10,
                maxScore = 90,
                gradeScale = "10–90",
                passingScore = 65,
                passingGrade = "65+",
                grades = new[]
                {
                    new { grade = "80–90", minScore = 80, maxScore = 90, label = "Expert" },
                    new { grade = "65–79", minScore = 65, maxScore = 79, label = "Advanced (Pass)" },
                    new { grade = "50–64", minScore = 50, maxScore = 64, label = "Upper Intermediate" },
                    new { grade = "36–49", minScore = 36, maxScore = 49, label = "Intermediate" },
                    new { grade = "10–35", minScore = 10, maxScore = 35, label = "Beginner" }
                }
            },
            _ => new
            {
                examFamily = examFamilyCode?.ToUpperInvariant() ?? "UNKNOWN",
                minScore = 0,
                maxScore = 100,
                gradeScale = "0–100",
                passingScore = 60,
                passingGrade = "Pass",
                grades = Array.Empty<object>()
            }
        };
    }

    /// <summary>
    /// Convert a raw OET score (0–500) to an OET grade letter.
    /// </summary>
    public static string OetGrade(double score) => score switch
    {
        >= 450 => "Grade A",
        >= 350 => "Grade B",
        >= 300 => "Grade C+",
        >= 200 => "Grade C",
        >= 100 => "Grade D",
        _ => "Grade E"
    };

    /// <summary>
    /// Convert a raw IELTS score to a band score (round to nearest 0.5).
    /// </summary>
    public static double IeltsBand(double rawScore)
    {
        var clamped = Math.Clamp(rawScore, 0, 9);
        return Math.Round(clamped * 2, MidpointRounding.AwayFromZero) / 2.0;
    }
}
