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

/// <summary>Classifier decision. <c>RuleCode</c>/<c>Evidence</c> record which
/// §8B question fired and the matched phrase, for the classification-preview
/// endpoint and the corpus reclassification audit manifest.</summary>
public sealed record SpeakingCardClassification(
    string Primary,
    string[] SecondaryTags,
    bool NeedsReview,
    string RuleCode,
    string? Evidence);

/// <summary>
/// FINAL 2026-09-09 / Speaking §8B deterministic primary-category classifier.
/// Port of <c>lib/speaking/category-taxonomy.ts</c> (taxonomy constants only —
/// the classification engine itself now lives here alone; see that file's
/// header for why).
/// Priority: exam start → ED arrival → known/inpatient → first visit → follow-up → behavioural-only → Other+review.
/// </summary>
public static class SpeakingCardClassifier
{
    /// <summary>Bumped whenever the rule set changes — persisted on each row
    /// as <c>CategoryClassifierVersion</c> so a reclassification sweep can
    /// tell which rows were touched by which rule generation.</summary>
    public const string ClassifierVersion = "2026-09-09.1";

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

    // NOTE (2026-09-09 repair): bare "surgery"/"operation" and the generic
    // "for hours|for N days" duration phrases were removed — they false-
    // positived on ordinary first-visit history-taking ("cough for 3 days")
    // and on any mention of past/planned surgery unrelated to a current
    // admission. Pre-op/post-op remain: those phrases inherently describe a
    // current peri-operative admission, not a historical reference.
    private static readonly Regex KnownCarePattern = new(
        @"observed (for|over)|under observation|already (known|managed|under|admitted)|under (our|your|hospital|their) care|managed (in|for|on the)|known to (us|the)|admit(ted)? (to|for|on)|transfer(red)? to.{0,30}(unit|ward|hospital|palliative)|palliative (unit|ward|care)|inpatient|in-patient|\bward\b|discharge|pre-?op(erative)?|post-?op|ICU|intensive care|hospital stay",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // NOTE (2026-09-09 repair): "first"/"initial"/"new" now tolerate up to 3
    // intervening words before the visit noun, so profession-specific
    // phrasing ("first physiotherapy appointment", "initial dietary
    // consultation") matches instead of falling through to Other Cards.
    private static readonly Regex FirstVisitPattern = new(
        @"first\s+(?:\w+\s+){0,3}(visit|time|presentation|attendance|consultation|appointment|session)|present(s|ed|ing)? for the first time|new\s+(?:\w+\s+){0,2}(patient|referral)|initial\s+(?:\w+\s+){0,3}(visit|consultation|presentation|assessment|appointment|session)|never (seen|visited|attended) before|first-?ever",
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

    // Shared "is this trigger word describing the CURRENT state, or a
    // negated / historical / resolved reference?" guard (2026-09-09 repair).
    // Applied uniformly to every trigger pattern: a match preceded within 40
    // characters by a negation ("not", "denies") or historical marker
    // ("previous", "3 years ago", "in remission") is skipped rather than
    // counted, so "no complaints", "denies being upset", or "had surgery
    // 3 years ago" stop false-triggering Angry / Already Known Patient / etc.
    private static readonly Regex ExclusionContextPattern = new(
        @"\bnot\b|\bno\b|\bnever\b|\bwithout\b|\bdenies?\b|\bdenying\b|isn.?t|wasn.?t|aren.?t|weren.?t|\bprevious(ly)?\b|\bprior\b|history of|in the past|\blast (week|month|year)\b|\d+\s+(days?|weeks?|months?|years?)\s+ago|had been|used to be|\bformerly\b|in remission|\bresolved\b|\bno longer\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Returns the matched text of the first occurrence of
    /// <paramref name="pattern"/> that is NOT preceded (within a 40-char
    /// window) by a negation/historical marker, or null if every occurrence
    /// is excluded (or there is none).</summary>
    private static string? FindActiveSignal(Regex pattern, string text)
    {
        foreach (Match match in pattern.Matches(text))
        {
            var windowStart = Math.Max(0, match.Index - 40);
            var window = text[windowStart..match.Index];
            if (!ExclusionContextPattern.IsMatch(window))
            {
                return match.Value;
            }
        }
        return null;
    }

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

        // Patient names never carry encounter semantics ("Mrs Ward" must not
        // read as a hospital ward). Strip the full name and the surname token.
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

    private readonly record struct BehaviouralHit(string Tag, string Evidence);

    private static List<BehaviouralHit> DetectBehaviouralWithEvidence(SpeakingCardClassifiable input)
    {
        var text = BehaviouralText(input);
        var hits = new List<BehaviouralHit>();
        var badNews = FindActiveSignal(BadNewsPattern, text);
        if (badNews is not null) hits.Add(new BehaviouralHit("Breaking Bad News", badNews));
        var angry = FindActiveSignal(AngryPattern, text);
        if (angry is not null) hits.Add(new BehaviouralHit("Angry", angry));
        var reluctant = FindActiveSignal(ReluctantPattern, text);
        if (reluctant is not null) hits.Add(new BehaviouralHit("Reluctant", reluctant));
        return hits;
    }

    public static List<string> DetectBehavioural(SpeakingCardClassifiable input)
        => DetectBehaviouralWithEvidence(input).Select(h => h.Tag).ToList();

    public static SpeakingCardClassification Classify(SpeakingCardClassifiable input)
    {
        var text = CandidateText(input);
        var behavioural = DetectBehaviouralWithEvidence(input);

        SpeakingCardClassification WithSecondary(string primary, string ruleCode, string? evidence) => new(
            Primary: primary,
            SecondaryTags: behavioural
                .Select(h => h.Tag)
                .Where(tag => !string.Equals(tag, primary, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            NeedsReview: false,
            RuleCode: ruleCode,
            Evidence: evidence);

        // Q1 — role play explicitly framed as beginning AFTER a completed exam.
        var examEvidence = FindActiveSignal(ExamStartPattern, text) ?? FindActiveSignal(ExamOpeningPattern, text);
        if (examEvidence is not null)
        {
            return WithSecondary("Examination Card", "Q1-exam-start", examEvidence);
        }

        // Q2 — patient has JUST arrived in Emergency now (not already managed there).
        if (EmergencySettingPattern.IsMatch(text))
        {
            var arrivalEvidence = FindActiveSignal(EmergencyArrivalPattern, text);
            var knownCareGate = FindActiveSignal(KnownCarePattern, text);
            if (arrivalEvidence is not null && knownCareGate is null)
            {
                return WithSecondary("Emergency / Emergency Department", "Q2-ed-arrival", arrivalEvidence);
            }
        }

        // Q3 — already under active inpatient / known care (ward, ICU, pre-op,
        // discharge, or already managed in ED).
        var knownCareEvidence = FindActiveSignal(KnownCarePattern, text);
        if (knownCareEvidence is not null)
        {
            return WithSecondary("Already Known Patient", "Q3-known-care", knownCareEvidence);
        }

        // Q4 — first-ever attendance.
        var firstVisitEvidence = FindActiveSignal(FirstVisitPattern, text);
        if (firstVisitEvidence is not null)
        {
            return WithSecondary("First Visit", "Q4-first-visit", firstVisitEvidence);
        }

        // Q5 — return / follow-up / results / side effects / treatment progress.
        var followUpEvidence = FindActiveSignal(FollowUpPattern, text);
        if (followUpEvidence is not null)
        {
            return WithSecondary("Second Visit / Follow-up", "Q5-follow-up", followUpEvidence);
        }

        // Q6 — no encounter category fits: a behavioural scenario on its own, else Other.
        var badNewsHit = behavioural.FirstOrDefault(h => h.Tag == "Breaking Bad News");
        if (badNewsHit.Tag is not null)
        {
            return new SpeakingCardClassification("Breaking Bad News", Array.Empty<string>(), false, "Q6-behavioural-bad-news", badNewsHit.Evidence);
        }
        var angryHit = behavioural.FirstOrDefault(h => h.Tag == "Angry");
        if (angryHit.Tag is not null)
        {
            return new SpeakingCardClassification("Angry Patient", Array.Empty<string>(), false, "Q6-behavioural-angry", angryHit.Evidence);
        }
        var reluctantHit = behavioural.FirstOrDefault(h => h.Tag == "Reluctant");
        if (reluctantHit.Tag is not null)
        {
            return new SpeakingCardClassification("Reluctant Patient", Array.Empty<string>(), false, "Q6-behavioural-reluctant", reluctantHit.Evidence);
        }

        // Low-confidence fallback: visible Other Cards + review flag rather than a
        // forced wrong category.
        return new SpeakingCardClassification("Other Cards", Array.Empty<string>(), true, "Q6-other-low-confidence", null);
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

    /// <summary>Stamps a classifier decision onto a card: category, tags,
    /// review flag, and the provenance trio (CategorySource="classifier",
    /// CategoryClassifierVersion, CategoryClassifiedAt). Shared by
    /// <see cref="ApplyIfUnclassified"/> and any admin-service call site that
    /// classifies a freshly-built card.</summary>
    public static void StampClassifierProvenance(RolePlayCard card, SpeakingCardClassification classification)
    {
        card.PrimaryCategory = classification.Primary;
        card.SecondaryTagsJson = JsonSerializer.Serialize(classification.SecondaryTags);
        card.CategoryNeedsReview = classification.NeedsReview;
        card.CategorySource = "classifier";
        card.CategoryClassifierVersion = ClassifierVersion;
        card.CategoryClassifiedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Classifies only if PrimaryCategory is blank/Other Cards OR CategoryNeedsReview is true.
    /// Never overwrites a confirmed non-Other category with NeedsReview == false, and never
    /// touches a row with confirmed provenance (manual/reviewed/seed) at all — even "Other
    /// Cards" can be a deliberate, confirmed conclusion there, not a placeholder awaiting the
    /// automatic sweep. Sets PrimaryCategory, SecondaryTagsJson, CategoryNeedsReview, and the
    /// classifier provenance fields.
    /// </summary>
    public static bool ApplyIfUnclassified(RolePlayCard card)
    {
        if (card is null) return false;

        if (card.CategorySource is "manual" or "reviewed" or "seed")
        {
            return false;
        }

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
            && string.Equals(card.SecondaryTagsJson ?? "[]", tagsJson, StringComparison.Ordinal)
            && string.Equals(card.CategorySource, "classifier", StringComparison.Ordinal))
        {
            return false;
        }

        StampClassifierProvenance(card, classification);
        return true;
    }
}
