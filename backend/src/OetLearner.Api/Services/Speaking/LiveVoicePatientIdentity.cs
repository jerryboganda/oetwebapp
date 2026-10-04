using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Who the live AI plays (owner spec 4 Oct 2026): a real, stable name and a voice that fits that person.
/// <see cref="SpeakerName"/> is the name the speaker answers to: the patient's, or, when the speaker is someone else
/// (a parent, a carer, a relative), that person's own name. <see cref="PatientName"/> is the patient when the speaker is
/// someone else.
/// </summary>
public sealed record LiveVoicePatientIdentity(
    string SpeakerName, string? PatientName, string Gender, string AgeBand, bool PlaysAThirdParty);

/// <summary>
/// Works the identity out from the card alone, never from the provider or the moment, so the same card is the same person
/// on every provider, after a reconnect and after a failover. A card with a missing or blank name gets a plain, stable
/// one (James or Jack, Anne or Sarah) instead of the model inventing a different name each time.
/// </summary>
public static class LiveVoicePatientIdentityResolver
{
    public const string Male = "male";
    public const string Female = "female";

    /// <summary>Under 45: the younger voice of the pair.</summary>
    public const string Younger = "younger";

    /// <summary>45 and over: the older voice of the pair.</summary>
    public const string Older = "older";

    private const int OlderFromAge = 45;
    private static readonly string[] MaleNames = ["James", "Jack"];
    private static readonly string[] FemaleNames = ["Anne", "Sarah"];

    private static readonly Regex MaleWords = new(
        @"\b(he|him|his|himself|man|boy|male|gentleman|mr|sir|father|dad|husband|son|brother|grandfather|uncle)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FemaleWords = new(
        @"\b(she|her|hers|herself|woman|girl|female|lady|mrs|ms|miss|madam|mother|mum|mom|wife|daughter|sister|grandmother|aunt)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ThirdPartyRole = new(
        @"\b(mother|mum|mom|father|dad|parent|carer|caregiver|guardian|wife|husband|partner|son|daughter|relative|sibling|brother|sister|grandmother|grandfather|friend|neighbou?r|colleague|nurse|teacher)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FirstNumber = new(@"\d+", RegexOptions.Compiled);

    public static LiveVoicePatientIdentity Resolve(RolePlayCard card)
    {
        var role = card.InterlocutorRole ?? string.Empty;
        var thirdParty = ThirdPartyRole.IsMatch(role);
        var cardName = string.IsNullOrWhiteSpace(card.PatientName) ? null : card.PatientName.Trim();

        var gender = (thirdParty ? GenderOf(role) : null)
            ?? (!thirdParty ? GenderOfTitle(cardName) ?? GenderOf($"{card.Background} {card.ScenarioTitle} {string.Join(' ', card.Tasks)}") : null)
            ?? (HashByte(card.Id) % 2 == 0 ? Male : Female);

        var speakerName = thirdParty || cardName is null
            ? Fallback(gender, card.Id)
            : cardName;

        return new LiveVoicePatientIdentity(
            speakerName,
            thirdParty ? cardName : null,
            gender,
            thirdParty ? Younger : AgeBandOf(card.PatientAge),
            thirdParty);
    }

    /// <summary>The configured voice for this person on this provider (a two-by-two table: gender and age band), or null
    /// when none is configured: the provider then uses its own default voice.</summary>
    public static string? VoiceFor(LiveVoicePatientIdentity identity, string provider, LiveVoiceOptions options)
    {
        var female = identity.Gender == Female;
        var older = identity.AgeBand == Older;
        var voice = string.Equals(provider, LiveVoiceProviders.Gemini, StringComparison.Ordinal)
            ? (female
                ? (older ? options.GeminiVoiceFemaleOlder : options.GeminiVoiceFemaleYounger)
                : (older ? options.GeminiVoiceMaleOlder : options.GeminiVoiceMaleYounger))
            : (female
                ? (older ? options.OpenAiVoiceFemaleOlder : options.OpenAiVoiceFemaleYounger)
                : (older ? options.OpenAiVoiceMaleOlder : options.OpenAiVoiceMaleYounger));
        return string.IsNullOrWhiteSpace(voice) ? null : voice.Trim();
    }

    /// <summary>The identity paragraph of the live instructions: who you are, and that a question about a name never
    /// starts the story.</summary>
    public static string Describe(LiveVoicePatientIdentity identity)
        => identity.PlaysAThirdParty
            ? $"YOUR IDENTITY: your name is {identity.SpeakerName}, you are a {identity.Gender}, and you are speaking for the patient"
                + (identity.PatientName is { } patient ? $" (the patient's name is {patient})" : string.Empty)
                + $". If the candidate asks your name or how to address you, say {identity.SpeakerName}. A question about a name never starts your story: give the name in a few words and wait."
            : $"YOUR IDENTITY: your name is {identity.SpeakerName} and you are a {identity.Gender}. If the candidate asks your name, how to address you, or to confirm who you are, say so in a few words and wait. A question about your name never starts your story.";

    private static string? GenderOfTitle(string? name)
    {
        if (name is null) return null;
        var first = name.Split([' ', '.'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        return first switch
        {
            "mr" or "sir" or "master" => Male,
            "mrs" or "ms" or "miss" or "madam" or "lady" => Female,
            _ => null,
        };
    }

    private static string? GenderOf(string text)
    {
        var male = MaleWords.Matches(text).Count;
        var female = FemaleWords.Matches(text).Count;
        return male == female ? null : male > female ? Male : Female;
    }

    private static string AgeBandOf(string? age)
    {
        if (string.IsNullOrWhiteSpace(age)) return Younger;
        // A baby or a child's age in months, weeks or days is never "older".
        if (Regex.IsMatch(age, @"\b(month|week|day)s?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return Younger;
        var match = FirstNumber.Match(age);
        return match.Success && int.TryParse(match.Value, out var years) && years >= OlderFromAge ? Older : Younger;
    }

    private static string Fallback(string gender, string cardId)
    {
        var pool = gender == Female ? FemaleNames : MaleNames;
        // The second hash byte: the first one decides the gender, and reusing it would pin one name to each gender.
        return pool[HashByte(cardId, 1) % pool.Length];
    }

    private static int HashByte(string value, int index = 0)
        => SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))[index];
}
