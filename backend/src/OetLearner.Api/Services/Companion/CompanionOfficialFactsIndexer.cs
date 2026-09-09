using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionOfficialFactsIndexer
{
    Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct);
}

/// <summary>
/// Seeds the official OET factual layer (Manifest 1.D) — <b>staged, not
/// published</b>.
///
/// <para>
/// This is the most severe gap in the knowledge base and also the one that must
/// not be closed by guessing. The companion is required to keep verified
/// official exam facts in a different authority class from Dr Hesham's teaching
/// method, and to prefer the official fact when they disagree. With no source in
/// that class at all, the whole precedence architecture was unreachable at
/// runtime: <see cref="CompanionRetriever"/> can only report a conflict when an
/// official fact is present, so it never could.
/// </para>
///
/// <para>
/// <b>Why everything here is <see cref="CompanionSourceState.PendingApproval"/>.</b>
/// The owner's instruction was to seed but stage: an official-fact claim is only
/// worth having if somebody has checked it against the official source on a
/// known date, and nobody has yet. Pending sources are invisible to the
/// retriever's prefilter, so until Dr Hesham approves them the companion keeps
/// saying it has no verified information — which is the correct answer, and
/// exactly what golden case GC-002 requires. Approving them is a deliberate act
/// through the knowledge admin endpoint, and it records who approved what and
/// when.
/// </para>
///
/// <para>
/// The facts below are structural and slow-moving — the shape of the test, what
/// the grades are, how long a result lasts — and each carries the source it
/// should be verified against. Anything volatile (fees, local test-centre dates,
/// a specific regulator's current minimum) is deliberately absent: staging a
/// price that changes quarterly would produce a confidently stale answer, and
/// "I don't have verified current information on that" is the better one.
/// </para>
/// </summary>
public sealed class CompanionOfficialFactsIndexer(
    LearnerDbContext db,
    IEmbeddingService embeddings,
    ILogger<CompanionOfficialFactsIndexer> logger) : ICompanionOfficialFactsIndexer
{
    internal const string SourceKey = "official:oet-exam-facts";
    internal const string Version = "2026-09-09.1";

    /// <summary>Where every claim in this source must be checked before approval.</summary>
    internal const string VerificationSource = "https://oet.com";

    public async Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct)
    {
        var warnings = new List<string>();

        // Never demote a source an admin has already approved. Reindexing is a
        // routine operation; silently reverting a sign-off because the seed says
        // "pending" would make approval meaningless and would pull verified
        // facts out of the corpus without anyone asking for it.
        var approved = await db.CompanionSources
            .AsNoTracking()
            .AnyAsync(s => s.SourceKey == SourceKey
                           && s.Version == Version
                           && s.State == CompanionSourceState.Approved, ct);

        var result = await CompanionIndexWriter.WriteAsync(
            db, embeddings, logger, SourceKey, Version,
            source =>
            {
                source.SourceType = "official_fact";
                source.Title = "OET official exam facts (awaiting verification)";
                source.AuthorityClass = CompanionAuthorityClass.OfficialCurrentFact;
                source.State = approved ? CompanionSourceState.Approved : CompanionSourceState.PendingApproval;
                source.ExamTypeCode = "OET";
                source.ProfessionId = null;
                source.SubtestCode = null;
                source.IsProprietary = false;
                source.RequiredEntitlementScope = null;
                source.PackageScope = null;
                source.SourceUrl = VerificationSource;
                source.StorageLocator = "backend/src/OetLearner.Api/Services/Companion/CompanionOfficialFactsIndexer.cs";
            },
            BuildChunks(), embed, warnings, ct);

        if (!approved && result.ChunksWritten + result.ChunksUnchanged > 0)
        {
            warnings.Add(
                $"Official OET facts are staged as PendingApproval and are NOT retrievable. Sami will correctly answer " +
                $"\"I don't have verified information\" for official exam facts until they are checked against " +
                $"{VerificationSource} and approved via POST /v1/admin/companion/knowledge/sources/{SourceKey}/approve.");
        }

        return result;
    }

    internal static IReadOnlyList<CompanionChunkDraft> BuildChunks() =>
    [
        new("OET — what the test is and who it is for",
            "The Occupational English Test (OET) is an English language test for healthcare professionals, assessed in a " +
            "healthcare context rather than a general one. It has twelve profession-specific versions; Reading and " +
            "Listening are shared across all professions, while Writing and Speaking use materials specific to the " +
            "candidate's profession.\n" +
            "OET is accepted by healthcare regulators, councils and employers in a number of countries, and by some " +
            "immigration authorities. Which score a candidate needs is set by the organisation they are applying to, " +
            "NOT by OET. Never tell a candidate what score their regulator requires — direct them to that regulator, " +
            "because the requirement differs by country, by profession and over time."),

        new("OET — the four sub-tests and the order they are taken",
            "OET has four sub-tests, taken in this order on test day: Listening, Reading, Writing, then Speaking. " +
            "Speaking may be scheduled on the same day or a different day depending on the candidate's timetable.\n" +
            "Listening: 42 marks in three parts, approximately 45 minutes.\n" +
            "Reading: 42 marks in three parts, 60 minutes — Part A is a hard-locked 15-minute section, and Parts B and C " +
            "share 45 minutes.\n" +
            "Writing: one profession-specific letter task, 45 minutes including 5 minutes' reading time in which the " +
            "candidate may read the case notes but must not write.\n" +
            "Speaking: two profession-specific role plays with an interlocutor, around 20 minutes in total including a " +
            "short unassessed warm-up."),

        new("OET — grades, scores and how results are reported",
            "Each sub-test is reported on a scale of 0 to 500, in steps of 10, and is given a letter grade. There is no " +
            "single overall OET score: the four sub-tests are reported separately, and organisations set their own " +
            "requirement for each.\n" +
            "The grade bands are A (450–500), B (350–440), C+ (300–340), C (200–290), D (100–190) and E (0–90).\n" +
            "Grade B is the level most commonly required by healthcare regulators, but the requirement belongs to the " +
            "receiving organisation and a candidate must confirm it with them.\n" +
            "A candidate can retake individual sub-tests. Whether an organisation accepts results combined from more " +
            "than one sitting is that organisation's decision, not OET's."),

        new("OET — booking, identification and test day",
            "Candidates book through their OET account on the official OET website, choosing a test date, a location or " +
            "the online option, and their profession. Fees, available dates and available venues vary by country and " +
            "change over time — always send a candidate to the official OET site for current fees and dates rather than " +
            "quoting a figure.\n" +
            "Identification: candidates must bring the same valid photographic identity document they used to book, and " +
            "it must not be expired. A passport is the document accepted everywhere; other documents may be accepted in " +
            "some locations. A mismatch between the booking and the document presented can mean being refused entry.\n" +
            "Personal items including phones, watches and notes are not permitted at the desk. Candidates should arrive " +
            "early enough to complete identity and admission checks."),

        new("OET — delivery modes",
            "OET is offered on paper at a test venue, on computer at a test venue, and as OET@Home, taken remotely under " +
            "remote supervision. The test content and the way it is marked are the same across modes; what differs is " +
            "how answers are given and what the candidate needs on the day.\n" +
            "OET@Home has its own technical and environment requirements, including equipment and room checks, and it is " +
            "not available in every country. A candidate considering it must check the current requirements on the " +
            "official OET site before booking.\n" +
            "Which modes are available at a given time and place is set by OET and changes. Do not tell a candidate a " +
            "particular mode is available to them; tell them where to check."),

        new("OET — result timing, validity and re-marks",
            "Results are published to the candidate's OET account. The timeframe differs between computer-based and " +
            "paper-based sittings, and OET publishes the current timeframe for each; do not quote a number of days " +
            "unless it has been verified.\n" +
            "How long a result stays valid is decided by the organisation receiving it, not by OET. Many healthcare " +
            "regulators treat English test results as valid for a limited period, commonly two years, but this varies " +
            "and the candidate must confirm it with the body they are applying to.\n" +
            "Candidates who believe a result is wrong may apply for a re-mark within the window OET sets, for a fee. " +
            "The process, window and fee are published by OET.\n" +
            "Neither this platform nor this assistant can see, verify, change or predict an official OET result. Any " +
            "score or feedback given here is practice feedback only."),
    ];
}
