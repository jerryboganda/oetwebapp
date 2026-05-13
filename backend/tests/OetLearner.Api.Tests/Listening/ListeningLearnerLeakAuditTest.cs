using System.Text.RegularExpressions;
using Xunit;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// Listening V2 — pre-submit DTO leak audit. Source-scans every class whose
/// name contains "Learner" + "Dto" / "Question" + "Dto" / "Practice" + "Dto"
/// inside the Listening services tree. Fails CI if any such DTO has a
/// property name matching the blacklist (which would leak the answer key
/// to in-flight candidates and cripple grading integrity).
///
/// Why: the whole Reading + Listening leak-prevention discipline relies on
/// projection-layer hygiene. A reviewer-only check is insufficient because
/// the most common regression is "I added a small field and forgot it ships
/// the answer". This test catches that at compile/test time.
/// </summary>
public class ListeningLearnerLeakAuditTest
{
    private static readonly string[] LeakFields = new[]
    {
        "IsCorrect",
        "CorrectAnswer",
        "CorrectAnswerJson",
        "AcceptedSynonyms",
        "AcceptedSynonymsJson",
        "ExplanationMarkdown",
        "WhyWrong",
        "WhyWrongMarkdown",
        "TranscriptEvidence",
        "TranscriptEvidenceText",
        "TranscriptEvidenceStartMs",
        "TranscriptEvidenceEndMs",
        "DistractorCategory",
    };

    [Fact]
    public void Listening_pre_submit_DTOs_do_not_carry_answer_key_fields()
    {
        var listeningDir = LocateListeningServicesDir();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            listeningDir, "*.cs", SearchOption.AllDirectories))
        {
            // Audit excludes:
            //   - the audit test itself
            //   - the grading service (legitimately reads CorrectAnswerJson)
            //   - the authoring service (admin write path — IS the answer key)
            //   - any "Submitted*Dto" or "*ReviewDto" — post-submit views
            //     legitimately surface the answer key once Status=Submitted.
            var name = Path.GetFileName(file);
            if (name.Contains("LeakAudit", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("Grading", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("Authoring", StringComparison.OrdinalIgnoreCase)) continue;

            var src = File.ReadAllText(file);

            // Scope to declared DTO records / classes.
            var dtoBlocks = Regex.Matches(src,
                @"(public|internal)\s+(sealed\s+)?(record|class)\s+(\w*(Dto|LearnerView|Projection))\b[^{]*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}",
                RegexOptions.Compiled | RegexOptions.Singleline);

            foreach (Match dto in dtoBlocks)
            {
                var dtoName = dto.Groups[4].Value;

                // Skip post-submit / review DTOs — they LEGITIMATELY ship
                // the answer key once Status=Submitted.
                if (dtoName.Contains("Submitted", StringComparison.OrdinalIgnoreCase)) continue;
                if (dtoName.Contains("Review", StringComparison.OrdinalIgnoreCase)) continue;
                if (dtoName.Contains("Result", StringComparison.OrdinalIgnoreCase)) continue;
                if (dtoName.Contains("Admin", StringComparison.OrdinalIgnoreCase)) continue;
                if (dtoName.Contains("Expert", StringComparison.OrdinalIgnoreCase)) continue;

                var body = dto.Groups["body"].Value;
                foreach (var leak in LeakFields)
                {
                    if (Regex.IsMatch(body, $@"\b{Regex.Escape(leak)}\b"))
                    {
                        offenders.Add($"{name}: pre-submit DTO {dtoName} carries '{leak}'");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Pre-submit Listening DTOs must not carry answer-key fields. " +
            "Add 'Submitted'/'Review'/'Result' to the DTO name if it is a " +
            "post-submit view, otherwise strip the field. Offenders:\n" +
            string.Join("\n", offenders));
    }

    private static string LocateListeningServicesDir()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "backend", "src", "OetLearner.Api", "Services", "Listening");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        throw new DirectoryNotFoundException(
            "Could not locate backend/src/OetLearner.Api/Services/Listening from test bin dir.");
    }
}
