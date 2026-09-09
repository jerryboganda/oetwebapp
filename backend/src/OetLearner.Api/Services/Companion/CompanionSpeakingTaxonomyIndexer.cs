using System.Text;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionSpeakingTaxonomyIndexer
{
    Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct);
}

/// <summary>
/// Publishes the approved Speaking card taxonomy into the companion corpus.
///
/// <para>
/// The taxonomy is one of the most-tested things in the whole acceptance set —
/// Testing Pack 1 asks the companion to classify a card and open correctly
/// (scenario 12), to recognise an already-known patient rather than a first
/// visit (21), to classify Breaking Bad News (22), and — the sharp one — NOT to
/// reach for the Examination Card just because the word "examination" appears
/// (23). Yet none of it was retrievable: the rulebooks contain no card taxonomy
/// at all, so the companion had to answer those from general knowledge, which is
/// exactly the invented-Dr-Hesham-rule failure the packs score as zero.
/// </para>
///
/// <para>
/// <b>Why generate rather than hand-write.</b> The categories and the priority
/// order are already stated authoritatively, in code, by
/// <see cref="SpeakingCardClassifier"/> — the same engine that labels every real
/// card in the catalogue. Writing a second prose copy would create the classic
/// two-sources problem where a rule change lands in one and not the other, and
/// the companion confidently teaches a taxonomy the platform no longer uses.
/// So the category list, the priority order and the source version all come from
/// the classifier, and <c>CompanionSpeakingTaxonomyIndexerTests</c> fails the
/// build if a category ever gains no candidate-facing explanation.
/// </para>
///
/// <para>
/// The <see cref="CompanionSource.Version"/> is the classifier's own version
/// stamp, so bumping a rule and reindexing supersedes the previous taxonomy
/// automatically rather than leaving two live copies.
/// </para>
/// </summary>
public sealed class CompanionSpeakingTaxonomyIndexer(
    LearnerDbContext db,
    IEmbeddingService embeddings,
    ILogger<CompanionSpeakingTaxonomyIndexer> logger) : ICompanionSpeakingTaxonomyIndexer
{
    internal const string SourceKey = "speaking:card-taxonomy";

    /// <summary>
    /// How a candidate recognises each category and how the opening changes.
    ///
    /// <para>
    /// Keyed by <see cref="SpeakingCardClassifier.PrimaryCategories"/>. This is
    /// the teaching layer the classifier deliberately does not carry: the engine
    /// decides what a card <i>is</i>, this explains what the candidate should
    /// <i>do</i> about it.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> CategoryGuidance = new(StringComparer.Ordinal)
    {
        ["First Visit"] =
            "The patient is attending for the first time — first visit, first appointment, new referral, or explicitly never seen before. " +
            "Open by introducing yourself and your role, confirm who you are speaking to, then take the history from the beginning. " +
            "Do not assume any shared background: everything you need must be established in the consultation.",

        ["Second Visit / Follow-up"] =
            "The patient is returning: a follow-up, a review, test results, treatment progress, side effects, or a check-up. " +
            "Do not repeat a full first-visit introduction. Open by acknowledging the previous contact and the reason for return " +
            "(\"I understand you have come back about…\"), confirm what has changed since, then move to the new information. " +
            "The consultation flow is shorter at the start because history already exists.",

        ["Already Known Patient"] =
            "The patient is already under active care — an inpatient, on a ward, in ICU, pre- or post-operative, or being discharged. " +
            "You already know them and they already know the team, so a first-introduction sequence wastes time and reads as odd. " +
            "Open by orienting to the current episode and the purpose of this conversation (for example discharge planning), " +
            "not by asking who they are or why they came.",

        ["Examination Card"] =
            "The role play begins immediately AFTER you have completed a physical examination. " +
            "Open by thanking the patient for letting you examine them and then explain what you found, in plain language, before moving on. " +
            "This category depends on the card explicitly placing you after the examination — see the wording rule below.",

        ["Emergency / Emergency Department"] =
            "The patient has just arrived in an emergency setting — brought in, presenting now, by ambulance, or triaged. " +
            "Open efficiently: identify yourself, establish the immediate problem quickly, and prioritise urgent concerns and safety over a full leisurely history. " +
            "A patient already being managed in the department is an Already Known Patient, not an arrival.",

        ["Breaking Bad News"] =
            "The task requires delivering serious news — a malignancy, a grave diagnosis, a death, or a poor prognosis. " +
            "Prioritise warning the patient that difficult news is coming, delivering it plainly and without jargon, then pausing. " +
            "Give silence room, check understanding before adding detail, and respond to emotion before moving to plans. " +
            "This is a communication task: do not turn it into a clinical management lecture.",

        ["Angry Patient"] =
            "The patient is angry, upset, dissatisfied, or making a complaint. " +
            "Acknowledge the feeling explicitly and early, allow them to say what happened without interrupting, apologise for the experience where appropriate, " +
            "and only then move to explanation and resolution. Defending the service before acknowledging the person escalates rather than settles.",

        ["Reluctant Patient"] =
            "The patient refuses, resists, hesitates, or is unwilling to accept the recommendation. " +
            "Explore the reason for the reluctance before you argue for the plan — the concern is usually specific and often something they have read or been told. " +
            "Address that concern directly, present options honestly including risks, and work towards a safe agreed plan. " +
            "Respect their autonomy: persuasion, not coercion.",

        ["Other Cards"] =
            "The card does not match a common encounter category. " +
            "Classify from the wider context of the card rather than forcing it into a category it does not fit, " +
            "and open with the standard approach: introduce yourself and your role, confirm the patient, and establish the purpose of the conversation.",
    };

    public async Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct)
    {
        var warnings = new List<string>();

        var chunks = BuildChunks()
            .Select(c => new CompanionChunkDraft(c.Heading, c.Text))
            .ToList();

        return await CompanionIndexWriter.WriteAsync(
            db, embeddings, logger, SourceKey, SpeakingCardClassifier.ClassifierVersion,
            source =>
            {
                source.SourceType = "speaking_taxonomy";
                source.Title = "Speaking card taxonomy and opening logic";
                source.AuthorityClass = CompanionAuthorityClass.DrHeshamApprovedMethod;
                source.State = CompanionSourceState.Approved;
                source.ExamTypeCode = "OET";
                // Applies to every profession: the encounter categories are the same for
                // a nurse and a doctor even though the clinical content is not.
                source.ProfessionId = null;
                source.SubtestCode = "speaking";
                // Candidate-facing reference, already published at /speaking/rulebook and
                // the intro-questions/assessment-criteria pages, so it is not gated and
                // not subject to the extraction budget.
                source.IsProprietary = false;
                source.RequiredEntitlementScope = null;
                source.PackageScope = null;
                source.StorageLocator = "backend/src/OetLearner.Api/Services/Speaking/SpeakingCardClassifier.cs";
                source.ApprovedAt ??= DateTimeOffset.UtcNow;
            },
            chunks, embed, warnings, ct);
    }

    /// <summary>
    /// One chunk per retrievable idea: the catalogue, the priority order, the
    /// examination-wording rule, then one per category.
    /// </summary>
    internal static IEnumerable<(string Heading, string Text)> BuildChunks()
    {
        yield return (
            "Speaking card categories",
            "OET Speaking role-play cards are classified into these primary categories: " +
            string.Join(", ", SpeakingCardClassifier.PrimaryCategories) + ". " +
            "A card also carries behavioural tags where they apply: " +
            string.Join(", ", SpeakingCardClassifier.BehaviouralTags) + ". " +
            "The primary category describes the type of encounter; a behavioural tag describes how the patient is behaving within it. " +
            "Identify the category first, because it decides how you open.");

        yield return (
            "How the category is decided — priority order",
            "When more than one description fits, the encounter type wins over the patient's behaviour, and the questions are asked in this order:\n" +
            "1. Does the card place you immediately after a completed physical examination? Then it is an Examination Card.\n" +
            "2. Has the patient just arrived in an emergency setting? Then it is Emergency / Emergency Department.\n" +
            "3. Is the patient already under active care — inpatient, ward, ICU, pre- or post-operative, discharge? Then it is an Already Known Patient.\n" +
            "4. Is this the patient's first ever attendance? Then it is a First Visit.\n" +
            "5. Is the patient returning — follow-up, results, progress, side effects? Then it is a Second Visit / Follow-up.\n" +
            "6. If no encounter type fits, use the behaviour (Breaking Bad News, Angry Patient, Reluctant Patient); otherwise it is Other Cards.\n" +
            "So an angry patient on a ward is an Already Known Patient tagged Angry — not an Angry Patient card. " +
            "The behaviour changes how you handle the consultation, not how you open it.");

        yield return (
            "Examination Card — the wording rule",
            "Use the Examination Card opening ONLY when the card explicitly places you after a completed examination — " +
            "wording such as \"you have just examined the patient\", \"you have now finished examining\", or an opening line like " +
            "\"thank you for letting me examine you\".\n" +
            "The word \"examination\" appearing somewhere on the card is NOT enough. " +
            "Background phrasing such as \"after examination you find that the patient has mild ankle swelling\" is describing a finding you already hold, " +
            "not instructing you to begin after an examination you have just performed. " +
            "In that case classify the visit from the wider context — first visit, follow-up, already known patient — and use that opening instead. " +
            "Over-classifying as an Examination Card produces an opening that does not match the scenario and costs marks for appropriateness.");

        yield return (
            "Historical and negated wording does not set the category",
            "A trigger word only counts when it describes the CURRENT situation. " +
            "Negations and past references are ignored: \"no complaints\" is not an angry patient, " +
            "\"denies being upset\" is not an angry patient, and \"had surgery three years ago\" is not a current admission. " +
            "Read whether the phrase describes what is happening now or what happened before, and classify on the present encounter.");

        foreach (var category in SpeakingCardClassifier.PrimaryCategories)
        {
            if (!CategoryGuidance.TryGetValue(category, out var guidance)) continue;
            yield return ($"{category} — how to recognise it and how to open", $"{category}. {guidance}");
        }
    }
}
