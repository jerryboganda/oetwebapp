using System.Text.Json;
using System.Text.RegularExpressions;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

public sealed record SpeakingCardClassifiable(
    string? ScenarioTitle = null,
    string? Setting = null,
    string? Background = null,
    IReadOnlyList<string>? Tasks = null,
    string? ClinicalTopic = null,
    string? PatientEmotion = null,
    string? PatientName = null,
    string? CandidateRole = null,
    string? InterlocutorRole = null,
    string? CommunicationGoal = null);

public sealed record SpeakingCardClassification(
    string Primary,
    string[] SecondaryTags,
    bool NeedsReview);

/// <summary>
/// FINAL 2026-09-06 / Speaking §8B deterministic primary-category classifier.
/// Port of <c>lib/speaking/category-taxonomy.ts</c>.
/// Priority: exam start → ED arrival → known/inpatient → first visit → follow-up → behavioural-only → Other+review.
/// </summary>
public static class SpeakingCardClassifier
{
    public static readonly string[] PrimaryCategories =
    {
        "First Visit",
        "Second Visit / Follow-up",
        "Already Known Patient",
        "Examination Card",
        "Emergency / Emergency Department",
        "Breaking Bad News",
        "Angry Patient",
        "Reluctant Patient",
        "Other Cards",
    };

    public static readonly string[] BehaviouralTags =
    {
        "Breaking Bad News",
        "Angry",
        "Reluctant",
    };

    private static readonly Regex ExamStartPattern = new(
        @"you have (just|finished|completed|now finished)[^.\r\n]{0,60}examin",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExamOpeningPattern = new(
        @"thank you for letting me examine you",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmergencySettingPattern = new(
        @"emergency department|\bED\b|emergency room|\bA&E\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmergencyArrivalPattern = new(
        @"just arriv|recently arriv|arrived (just |recently |with|to|at)|presents? (now|today|with|to|at)|just (came|came in|presented|walked in)|new arrival|brought in|rushed in|by ambulance|triaged",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex KnownCarePattern = new(
        @"for hours|for \d+ (hours|days)|observed (for|over)|under observation|already (known|managed|under|admitted)|under (our|your|hospital|their) care|managed (in|for|on the)|known to (us|the)|admit(ted)? (to|for|on)|transfer(red)? to.{0,30}(unit|ward|hospital|palliative)|palliative (unit|ward|care)|inpatient|in-patient|\bward\b|discharge|pre-?op(erative)?|post-?op|ICU|intensive care|surgery|operation|undergo(ing|ne)? (surgery|an? operation)|hospital stay",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FirstVisitPattern = new(
        @"first (visit|time|presentation|attendance|consultation|appointment)|present(s|ed|ing)? for the first time|new patient|new referral|initial (visit|consultation|presentation|assessment)|never (seen|visited|attended) before|first-?ever",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FollowUpPattern = new(
        @"follow.?up|return(ing|ed|s)? (for|to|visit|appointment)|(last|previous) visit|since the last|review (of|appointment)|test results?|results?.{0,20}(are|show|confirm|of)|side effects?|treatment progress|progress since|progression|came back|coming back|second visit|re-?attendance|ongoing (treatment|care|management)|continu(e[sd]?|ing) (treatment|management|care)|check-?up|recall (visit|appointment)|monitoring",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BadNewsPattern = new(
        @"break(ing)? bad news|cancer|malignan|terminal|serious diagnosis|grave news|has died|death|life.?threatening|palliative|chemotherapy|oncology|poor prognosis|bad news",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AngryPattern = new(
        @"\bangry\b|anger|furious|\bupset\b|complain(t|ed|ing|s)?|dissatisf|annoyed|irritat|raised a complaint|formal complaint|unhappy with|aggressive",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ReluctantPattern = new(
        @"refus|reluctant|declin|resist|does ?n[o']t want|do not want|unwilling|hesitant|against (medical )?advice|won.?t (take|attend|have|go|accept|agree|come)|will not (take|attend|have|go|accept|agree|come)|non-?complian",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GoalPattern = new(
        @"negotiat|persua",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string CandidateText(SpeakingCardClassifiable input)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(input.ScenarioTitle)) lines.Add(input.ScenarioTitle);
        if (!string.IsNullOrWhiteSpace(input.Setting)) lines.Add(input.Setting);
        if (!string.IsNullOrWhiteSpace(input.Background)) lines.Add(input.Background);
        if (input.Tasks is not null)
        {
            foreach (var task in input.Tasks)
            {
                if (!string.IsNullOrWhiteSpace(task)) lines.Add(task);
            }
        }
        if (!string.IsNullOrWhiteSpace(input.ClinicalTopic)) lines.Add(input.ClinicalTopic);

        var text = string.Join("\n", lines);

        // Patient names never carry encounter semantics ("Mrs Ward" must not read
        // as a hospital ward). Strip the full name and the surname token.
        var name = (input.PatientName ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(name))
        {
            var parts = new List<string> { name };
            parts.AddRange(Regex.Split(name, @"\s+").Where(p => !string.IsNullOrWhiteSpace(p)));
            foreach (var part in parts.Where(p => p.Length > 2))
            {
                text = Regex.Replace(text, Regex.Escape(part), string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }

        return text;
    }

    public static string BehaviouralText(SpeakingCardClassifiable input)
    {
        var goal = GoalPattern.IsMatch(input.CommunicationGoal ?? string.Empty)
            ? "patient resists recommendation"
            : string.Empty;

        var lines = new List<string>();
        var candidate = CandidateText(input);
        if (!string.IsNullOrWhiteSpace(candidate)) lines.Add(candidate);
        if (!string.IsNullOrWhiteSpace(input.PatientEmotion)) lines.Add(input.PatientEmotion);
        if (!string.IsNullOrWhiteSpace(goal)) lines.Add(goal);

        return string.Join("\n", lines);
    }

    public static List<string> DetectBehavioural(SpeakingCardClassifiable input)
    {
        var text = BehaviouralText(input);
        var tags = new List<string>();
        if (BadNewsPattern.IsMatch(text)) tags.Add("Breaking Bad News");
        if (AngryPattern.IsMatch(text)) tags.Add("Angry");
        if (ReluctantPattern.IsMatch(text)) tags.Add("Reluctant");
        return tags;
    }

    public static SpeakingCardClassification Classify(SpeakingCardClassifiable input)
    {
        var text = CandidateText(input);
        var behavioural = DetectBehavioural(input);

        SpeakingCardClassification WithSecondary(string primary) => new(
            Primary: primary,
            SecondaryTags: behavioural.Where(tag => !string.Equals(tag, primary, StringComparison.OrdinalIgnoreCase)).ToArray(),
            NeedsReview: false);

        // Q1 — role play explicitly framed as beginning AFTER a completed exam.
        if (ExamStartPattern.IsMatch(text) || ExamOpeningPattern.IsMatch(text))
        {
            return WithSecondary("Examination Card");
        }

        // Q2 — patient has JUST arrived in Emergency now (not already managed there).
        if (EmergencySettingPattern.IsMatch(text) &&
            EmergencyArrivalPattern.IsMatch(text) &&
            !KnownCarePattern.IsMatch(text))
        {
            return WithSecondary("Emergency / Emergency Department");
        }

        // Q3 — already under active inpatient / known care (ward, ICU, pre-op,
        // discharge, or already managed in ED).
        if (KnownCarePattern.IsMatch(text))
        {
            return WithSecondary("Already Known Patient");
        }

        // Q4 — first-ever attendance.
        if (FirstVisitPattern.IsMatch(text))
        {
            return WithSecondary("First Visit");
        }

        // Q5 — return / follow-up / results / side effects / treatment progress.
        if (FollowUpPattern.IsMatch(text))
        {
            return WithSecondary("Second Visit / Follow-up");
        }

        // Q6 — no encounter category fits: a behavioural scenario on its own, else Other.
        if (behavioural.Contains("Breaking Bad News"))
        {
            return new SpeakingCardClassification("Breaking Bad News", Array.Empty<string>(), false);
        }
        if (behavioural.Contains("Angry"))
        {
            return new SpeakingCardClassification("Angry Patient", Array.Empty<string>(), false);
        }
        if (behavioural.Contains("Reluctant"))
        {
            return new SpeakingCardClassification("Reluctant Patient", Array.Empty<string>(), false);
        }

        // Low-confidence fallback: visible Other Cards + review flag rather than a
        // forced wrong category.
        return new SpeakingCardClassification("Other Cards", Array.Empty<string>(), true);
    }

    public static SpeakingCardClassification Classify(RolePlayCard card)
    {
        if (card is null) throw new ArgumentNullException(nameof(card));
        return Classify(new SpeakingCardClassifiable(
            ScenarioTitle: card.ScenarioTitle,
            Setting: card.Setting,
            Background: card.Background,
            Tasks: card.Tasks?.ToArray(),
            ClinicalTopic: card.ClinicalTopic,
            PatientEmotion: card.PatientEmotion,
            PatientName: card.PatientName,
            CandidateRole: card.CandidateRole,
            InterlocutorRole: card.InterlocutorRole,
            CommunicationGoal: card.CommunicationGoal));
    }

    public static SpeakingCardClassification Classify(
        string? scenarioTitle,
        string? setting,
        string? background,
        IEnumerable<string>? tasks,
        string? clinicalTopic,
        string? patientEmotion,
        string? patientName,
        string? communicationGoal)
        => Classify(new SpeakingCardClassifiable(
            ScenarioTitle: scenarioTitle,
            Setting: setting,
            Background: background,
            Tasks: tasks?.ToArray(),
            ClinicalTopic: clinicalTopic,
            PatientEmotion: patientEmotion,
            PatientName: patientName,
            CommunicationGoal: communicationGoal));

    public static SpeakingCardClassification ClassifySpeakingCard(SpeakingCardClassifiable input)
        => Classify(input);

    /// <summary>
    /// Classifies only if PrimaryCategory is blank/Other Cards OR CategoryNeedsReview is true.
    /// Never overwrites a confirmed non-Other category with NeedsReview == false.
    /// Sets PrimaryCategory, SecondaryTagsJson, CategoryNeedsReview.
    /// </summary>
    public static bool ApplyIfUnclassified(RolePlayCard card)
    {
        if (card is null) return false;

        var trimmedPrimary = card.PrimaryCategory?.Trim();
        var isBlankOrOther = string.IsNullOrWhiteSpace(trimmedPrimary)
            || string.Equals(trimmedPrimary, "Other Cards", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmedPrimary, "Other", StringComparison.OrdinalIgnoreCase);

        if (!isBlankOrOther && !card.CategoryNeedsReview)
        {
            return false;
        }

        var classification = Classify(card);
        var tagsJson = JsonSerializer.Serialize(classification.SecondaryTags);
        if (string.Equals(card.PrimaryCategory, classification.Primary, StringComparison.Ordinal)
            && card.CategoryNeedsReview == classification.NeedsReview
            && string.Equals(card.SecondaryTagsJson ?? "[]", tagsJson, StringComparison.Ordinal))
        {
            return false;
        }

        card.PrimaryCategory = classification.Primary;
        card.SecondaryTagsJson = tagsJson;
        card.CategoryNeedsReview = classification.NeedsReview;
        return true;
    }
}
