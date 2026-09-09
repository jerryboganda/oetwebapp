using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// What kind of place a destination is. Used to bias search and to let the
/// companion phrase the suggestion correctly ("open", "practise", "upgrade").
/// </summary>
public enum CompanionDestinationKind
{
    Practice,
    Learning,
    Progress,
    Library,
    Account,
    Commerce,
    Support,
}

/// <summary>
/// One learner-reachable place in the product.
///
/// <para>
/// This registry is the <b>only</b> vocabulary the companion has for navigation.
/// The model never emits a URL; it emits an <see cref="Id"/> from this table and
/// the server builds the link (F-023, and the hard rule behind F-098…F-101).
/// A hallucinated id resolves to "unknown destination" instead of a broken or —
/// worse — a plausible-looking wrong link.
/// </para>
/// </summary>
public sealed record CompanionDestination
{
    /// <summary>Stable id the model cites. Never renamed; retire instead.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Web-relative path. Resolved through <c>PlatformLinkService</c>.</summary>
    public required string Path { get; init; }

    public required CompanionDestinationKind Kind { get; init; }

    /// <summary>One line the model may quote when suggesting this place.</summary>
    public required string Description { get; init; }

    /// <summary>OET subtest this belongs to, when it is subtest-specific.</summary>
    public string? SubtestCode { get; init; }

    /// <summary>Subscription module key checked with <c>IsModuleEnabled</c>.</summary>
    public string? RequiredModuleKey { get; init; }

    /// <summary>Platform feature flag that must be on for the surface to exist.</summary>
    public string? RequiredFeatureFlag { get; init; }

    /// <summary>Null means every profession.</summary>
    public IReadOnlyList<ExamProfession>? Professions { get; init; }

    /// <summary>
    /// Set when this id is kept only so old references still resolve. Resolution
    /// follows the pointer and returns the canonical destination, so a retired id
    /// never produces a dead end.
    /// </summary>
    public string? SupersededById { get; init; }

    /// <summary>
    /// True for surfaces that put the learner straight into a timed or immersive
    /// activity — an exam runner, a practice player, a mock.
    ///
    /// <para>
    /// These were originally excluded from the catalog altogether, so the
    /// companion could describe where to go but never start anything. The owner
    /// has decided it should be able to start and to continue, which makes the
    /// flag a <i>confirmation</i> requirement rather than an exclusion: the
    /// resolution carries it through to the prompt, which must state what is
    /// about to happen and get agreement before opening it. Dropping someone
    /// into a timed paper they did not ask for is a worse failure than making
    /// them click once more.
    /// </para>
    /// </summary>
    public bool Immersive { get; init; }

    /// <summary>
    /// Gated on <c>TutorBookUnlocked</c> rather than on a module key.
    ///
    /// <para>
    /// The Tutor Book is sold as an add-on and unlocks a subscription flag; it is
    /// not one of the admin-togglable dashboard modules. Checking a module key
    /// for it would be wrong in both directions — it would hide the book from a
    /// learner who bought the add-on, and advertise it to one on a legacy plan
    /// that fails module checks open.
    /// </para>
    /// </summary>
    public bool RequiresTutorBook { get; init; }

    /// <summary>Extra search terms; the title and description are always searched.</summary>
    public string Keywords { get; init; } = string.Empty;
}

/// <summary>
/// The outcome of resolving a destination for one learner. Either an absolute
/// URL, or a refusal carrying enough for the companion to explain itself and
/// offer the upgrade path — never a URL the learner cannot use.
/// </summary>
public sealed record CompanionDestinationResolution(
    bool Allowed,
    string Id,
    string Title,
    string? Url,
    string Reason,
    CompanionDestinationKind Kind = CompanionDestinationKind.Learning,
    string? RequiredScope = null,
    /// <summary>
    /// True for a timed or immersive surface. The companion must say what is
    /// about to start and get agreement before opening it — the owner lifted the
    /// old blanket ban on starting activities, not the requirement to ask.
    /// </summary>
    bool RequiresConfirmation = false)
{
    public static CompanionDestinationResolution Unknown(string id) =>
        new(false, id, id, null, "unknown_destination");
}

public interface ICompanionDestinationRegistry
{
    /// <summary>Every destination in the catalog, entitlement not applied.</summary>
    IReadOnlyList<CompanionDestination> All { get; }

    /// <summary>Resolves one id to a server-built URL, re-checking entitlement.</summary>
    Task<CompanionDestinationResolution> ResolveAsync(
        string destinationId,
        CompanionTurnContext context,
        CancellationToken ct);

    /// <summary>
    /// Destinations this learner can actually open, ranked against a free-text
    /// query. Locked ones are excluded, so the companion cannot advertise a
    /// place and then fail to open it.
    /// </summary>
    Task<IReadOnlyList<CompanionDestinationResolution>> SearchAsync(
        string? query,
        CompanionTurnContext context,
        int limit,
        CancellationToken ct);
}

/// <summary>
/// Serves F-023 and unblocks F-098…F-101.
///
/// <para><b>Why a static table.</b> The alternative — letting the model compose
/// a path — fails in two ways that matter commercially: it can invent a route
/// that 404s, and it can link straight past a paywall. A closed vocabulary makes
/// both impossible, and makes every navigation suggestion auditable.</para>
///
/// <para><b>Entitlement is re-checked at resolve time</b>, from the entitlement
/// snapshot rather than from the turn context's flattened scope list, because
/// module access has fail-open/deny-override semantics that only
/// <see cref="EffectiveEntitlementSnapshot.IsModuleEnabled"/> gets right.</para>
/// </summary>
public sealed class CompanionDestinationRegistry(
    IEffectiveEntitlementResolver entitlements,
    ICompanionFeatureFlags flags,
    PlatformLinkService links) : ICompanionDestinationRegistry
{
    public IReadOnlyList<CompanionDestination> All => Catalog;

    /// <summary>
    /// The catalog as static data, for <see cref="CompanionPlatformMapIndexer"/>.
    /// Indexing happens outside a request, so it has no registry instance and no
    /// learner to resolve entitlement against — it publishes what the product
    /// contains, and opening any of it still goes through <see cref="ResolveAsync"/>.
    /// </summary>
    internal static IReadOnlyList<CompanionDestination> CatalogForIndexing => Catalog;

    public async Task<CompanionDestinationResolution> ResolveAsync(
        string destinationId,
        CompanionTurnContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(destinationId)) return CompanionDestinationResolution.Unknown("");

        var id = destinationId.Trim();
        var destination = Find(id);
        if (destination is null) return CompanionDestinationResolution.Unknown(id);

        // Follow at most one redirect. Retired ids point at live ones; a chain
        // would be a catalog authoring error, and silently walking it would hide it.
        if (destination.SupersededById is { } canonicalId)
        {
            var canonical = Find(canonicalId);
            if (canonical is null) return CompanionDestinationResolution.Unknown(id);
            destination = canonical;
        }

        var snapshot = await entitlements.ResolveAsync(context.UserId, ct);
        return await EvaluateAsync(destination, context, snapshot, ct);
    }

    public async Task<IReadOnlyList<CompanionDestinationResolution>> SearchAsync(
        string? query,
        CompanionTurnContext context,
        int limit,
        CancellationToken ct)
    {
        var snapshot = await entitlements.ResolveAsync(context.UserId, ct);
        var terms = Tokenise(query);

        var scored = new List<(CompanionDestination Destination, int Score)>();
        foreach (var destination in Catalog)
        {
            if (destination.SupersededById is not null) continue; // never surface an alias
            var score = Score(destination, terms, context);
            if (terms.Count > 0 && score == 0) continue;
            scored.Add((destination, score));
        }

        var results = new List<CompanionDestinationResolution>();
        foreach (var (destination, _) in scored.OrderByDescending(x => x.Score).ThenBy(x => x.Destination.Title))
        {
            if (results.Count >= Math.Clamp(limit, 1, 25)) break;
            var resolution = await EvaluateAsync(destination, context, snapshot, ct);
            if (!resolution.Allowed) continue; // do not advertise what cannot be opened
            results.Add(resolution);
        }

        return results;
    }

    private async Task<CompanionDestinationResolution> EvaluateAsync(
        CompanionDestination destination,
        CompanionTurnContext context,
        EffectiveEntitlementSnapshot snapshot,
        CancellationToken ct)
    {
        // The platform flag is the only check that needs to go to the database,
        // so it is asked first and the rest is decided by Gate, which is pure.
        if (destination.RequiredFeatureFlag is { } flagKey
            && !await flags.IsPlatformFlagOnAsync(flagKey, ct))
        {
            return new CompanionDestinationResolution(
                false, destination.Id, destination.Title, null, "surface_disabled", destination.Kind);
        }

        var gated = Gate(destination, context, snapshot);
        if (!gated.Allowed) return gated;

        return gated with { Url = links.BuildWebUrl(destination.Path) };
    }

    /// <summary>
    /// Every entitlement decision for one destination, as a pure function.
    ///
    /// <para>
    /// Separated from <see cref="EvaluateAsync"/> so the rules can be tested
    /// against a hand-built entitlement snapshot rather than against a database
    /// seeded with subscriptions, plans and module lists. These are the checks
    /// that decide whether a paying learner gets in and a lapsed one does not,
    /// and they should be cheap enough to test exhaustively.
    /// </para>
    ///
    /// <para>The URL is filled in by the caller, so this can never hand one back
    /// by accident on a refusal path.</para>
    /// </summary>
    internal static CompanionDestinationResolution Gate(
        CompanionDestination destination,
        CompanionTurnContext context,
        EffectiveEntitlementSnapshot snapshot)
    {
        if (destination.Professions is { Count: > 0 } professions
            && !professions.Contains(context.Profession))
        {
            return new CompanionDestinationResolution(
                false, destination.Id, destination.Title, null, "wrong_profession", destination.Kind);
        }

        // Expiry, checked before the module gate.
        //
        // An expired course leaves ExpiresAt in the past while the module list
        // can still read as enabled, so an expired learner resolved every gated
        // destination and was handed a working link into content they no longer
        // have. Anything needing a package therefore has to ask whether the
        // package is still live, and the answer leads to renewal rather than to
        // a locked page.
        var needsLiveAccess = destination.RequiredModuleKey is not null || destination.RequiresTutorBook;
        if (needsLiveAccess && snapshot.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            return new CompanionDestinationResolution(
                false, destination.Id, destination.Title, null, "access_expired", destination.Kind, "renew");
        }

        if (destination.RequiresTutorBook && !snapshot.TutorBookUnlocked)
        {
            return new CompanionDestinationResolution(
                false, destination.Id, destination.Title, null, "module_not_entitled", destination.Kind, "tutor-book");
        }

        if (destination.RequiredModuleKey is { } moduleKey && !snapshot.IsModuleEnabled(moduleKey))
        {
            return new CompanionDestinationResolution(
                false, destination.Id, destination.Title, null, "module_not_entitled", destination.Kind, $"module:{moduleKey}");
        }

        // Never hand back a link into a timed activity while the learner is
        // already inside a protected attempt. Starting a second one mid-exam is
        // the kind of "helpful" action that costs somebody their attempt.
        if (destination.Immersive && context.ExamMode)
        {
            return new CompanionDestinationResolution(
                false, destination.Id, destination.Title, null, "exam_in_progress", destination.Kind);
        }

        return new CompanionDestinationResolution(
            true, destination.Id, destination.Title, null, "allowed",
            destination.Kind, RequiredScope: null, RequiresConfirmation: destination.Immersive);
    }

    private static CompanionDestination? Find(string id) =>
        Catalog.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    private static List<string> Tokenise(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : query.ToLowerInvariant()
                .Split([' ', ',', '.', '?', '!', '/', '-', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2)
                .Distinct()
                .ToList();

    private static int Score(CompanionDestination destination, List<string> terms, CompanionTurnContext context)
    {
        var score = 0;

        // A destination for the subtest the learner is currently looking at is
        // almost always the right one, so the surface hint is worth real weight.
        if (destination.SubtestCode is not null
            && string.Equals(destination.SubtestCode, context.Envelope.SubtestCode, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (terms.Count == 0) return score;

        var haystack = $"{destination.Id} {destination.Title} {destination.Description} {destination.Keywords}"
            .ToLowerInvariant();

        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.Ordinal)) score += 3;
        }

        return score;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The catalog. Every path here is a real learner route.
    //
    // Immersive players and exam runners were originally left out so the
    // companion could only ever suggest. The owner has decided it should be able
    // to start and to continue an activity, so they are present and marked
    // Immersive, which turns the exclusion into a confirmation step: the
    // companion says what is about to start and waits for a yes.
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly IReadOnlyList<CompanionDestination> Catalog =
    [
        new() { Id = "writing.practice", Title = "Writing practice", Path = "/writing", Kind = CompanionDestinationKind.Practice, SubtestCode = "writing", Description = "Write referral letters and get AI feedback against the writing rulebook.", Keywords = "letter referral discharge transfer task" },
        new() { Id = "speaking.practice", Title = "Speaking practice", Path = "/speaking", Kind = CompanionDestinationKind.Practice, SubtestCode = "speaking", Description = "Role-play cards with feedback on the speaking criteria.", Keywords = "role play roleplay card interlocutor" },
        new() { Id = "reading.practice", Title = "Reading practice", Path = "/reading", Kind = CompanionDestinationKind.Practice, SubtestCode = "reading", Description = "Timed reading papers with part A, B and C sections.", Keywords = "part matching gap fill text" },
        new() { Id = "listening.practice", Title = "Listening practice", Path = "/listening", Kind = CompanionDestinationKind.Practice, SubtestCode = "listening", Description = "Listening papers and targeted drills.", Keywords = "audio consultation extract drill" },
        new() { Id = "pronunciation.practice", Title = "Pronunciation practice", Path = "/pronunciation", Kind = CompanionDestinationKind.Practice, SubtestCode = "speaking", Description = "Pronunciation drills and minimal-pair discrimination.", Keywords = "accent intonation stress sounds" },
        new() { Id = "grammar.practice", Title = "Grammar", Path = "/grammar", Kind = CompanionDestinationKind.Practice, Description = "Grammar lessons and exercises.", Keywords = "tense article preposition clause" },
        new() { Id = "conversation.practice", Title = "AI conversation", Path = "/conversation", Kind = CompanionDestinationKind.Practice, SubtestCode = "speaking", Description = "Live spoken practice with the AI conversation partner.", Keywords = "voice talk speak partner" },
        new() { Id = "mock.exams", Title = "Mock exams", Path = "/mocks", Kind = CompanionDestinationKind.Practice, RequiredModuleKey = ModuleKeys.Mocks, Description = "Full-length timed mock exams under test conditions.", Keywords = "full test simulation timed" },

        new() { Id = "companion", Title = "AI Learning Companion", Path = "/companion", Kind = CompanionDestinationKind.Learning, RequiredFeatureFlag = "ai_learning_companion", Description = "The full-screen study conversation, with previous threads and credit balance.", Keywords = "companion chat assistant tutor sami ask" },

        new() { Id = "study.plan", Title = "Study plan", Path = "/study-plan", Kind = CompanionDestinationKind.Learning, Description = "The personalised weekly plan built from the exam date and goals.", Keywords = "schedule weekly timetable" },
        new() { Id = "next.actions", Title = "Next best actions", Path = "/next-actions", Kind = CompanionDestinationKind.Learning, Description = "What to do next, ranked by impact on the score.", Keywords = "recommended todo priority" },
        new() { Id = "learning.paths", Title = "Learning paths", Path = "/learning-paths", Kind = CompanionDestinationKind.Learning, Description = "Structured course paths from foundation to exam level.", Keywords = "course curriculum track" },
        new() { Id = "review.queue", Title = "Review queue", Path = "/review", Kind = CompanionDestinationKind.Learning, Description = "Spaced-repetition review of items due today.", Keywords = "spaced repetition due revise" },
        new() { Id = "remediation", Title = "Remediation", Path = "/remediation", Kind = CompanionDestinationKind.Learning, Description = "Targeted work on the weaknesses the engine has detected.", Keywords = "weakness fix improve mistakes" },
        new() { Id = "vocabulary.recalls", Title = "Vocabulary recalls", Path = "/recalls", Kind = CompanionDestinationKind.Learning, RequiredModuleKey = ModuleKeys.Recalls, Description = "Medical vocabulary recall sets and bookmarks.", Keywords = "words terms lexis flashcard" },
        new() { Id = "strategies", Title = "Strategy guides", Path = "/strategies", Kind = CompanionDestinationKind.Learning, RequiredFeatureFlag = "strategy_guides", Description = "Subtest strategy guides and exam technique.", Keywords = "technique tactics approach" },
        new() { Id = "exam.guide", Title = "Exam guide", Path = "/exam-guide", Kind = CompanionDestinationKind.Learning, Description = "How the OET exam works, format and scoring.", Keywords = "format structure rules scoring" },
        new() { Id = "test.day", Title = "Test day", Path = "/test-day", Kind = CompanionDestinationKind.Learning, Description = "What to expect and prepare on exam day.", Keywords = "exam day checklist venue identification" },

        new() { Id = "readiness", Title = "Readiness", Path = "/readiness", Kind = CompanionDestinationKind.Progress, Description = "Readiness estimate per subtest against the target grade.", Keywords = "ready score estimate band" },
        new() { Id = "progress", Title = "Progress", Path = "/progress", Kind = CompanionDestinationKind.Progress, Description = "Progress over time across all four subtests.", Keywords = "trend history chart improvement" },
        new() { Id = "predictions", Title = "Predictions", Path = "/predictions", Kind = CompanionDestinationKind.Progress, Description = "Projected performance based on recent attempts.", Keywords = "forecast projection outlook" },
        new() { Id = "submissions.history", Title = "Attempt history", Path = "/submissions", Kind = CompanionDestinationKind.Progress, Description = "Every attempt made, with feedback and comparisons.", Keywords = "past attempts results feedback submissions" },
        new() { Id = "history", Title = "Attempt history", Path = "/history", Kind = CompanionDestinationKind.Progress, SupersededById = "submissions.history", Description = "Alias kept so older references still resolve.", Keywords = "history" },
        new() { Id = "achievements", Title = "Achievements", Path = "/achievements", Kind = CompanionDestinationKind.Progress, Description = "Badges and streaks earned so far.", Keywords = "badge streak milestone" },
        new() { Id = "goals", Title = "Goals", Path = "/goals", Kind = CompanionDestinationKind.Progress, Description = "Target grade, exam date and study commitment.", Keywords = "target exam date deadline" },

        new() { Id = "materials", Title = "Materials library", Path = "/materials", Kind = CompanionDestinationKind.Library, RequiredModuleKey = ModuleKeys.MaterialsLibrary, Description = "Downloadable PDFs, worksheets and sample answers.", Keywords = "download worksheet notes book" },
        new() { Id = "videos", Title = "Video library", Path = "/videos", Kind = CompanionDestinationKind.Library, RequiredModuleKey = ModuleKeys.VideoLibrary, RequiredFeatureFlag = "video_library", Description = "Recorded lessons and walkthroughs.", Keywords = "video lesson lecture watch recording" },
        new() { Id = "live.classes", Title = "Live classes", Path = "/classes", Kind = CompanionDestinationKind.Library, Description = "Scheduled live classes and recordings.", Keywords = "class live session webinar" },

        new() { Id = "account.settings", Title = "Account settings", Path = "/settings", Kind = CompanionDestinationKind.Account, Description = "Profile, language, notifications and privacy settings.", Keywords = "profile password language notification privacy" },
        new() { Id = "ai.usage", Title = "AI usage", Path = "/ai-usage", Kind = CompanionDestinationKind.Account, Description = "How AI credits have been spent.", Keywords = "credit usage spend consumption" },

        new() { Id = "pricing", Title = "Pricing", Path = "/pricing", Kind = CompanionDestinationKind.Commerce, Description = "Plans, courses and what each one includes.", Keywords = "price cost plan package buy upgrade" },
        new() { Id = "ai.packages", Title = "AI credit packages", Path = "/ai-packages", Kind = CompanionDestinationKind.Commerce, Description = "Buy more AI credits for assessments and feedback.", Keywords = "credits top buy more" },
        new() { Id = "subscriptions", Title = "Subscriptions and packages", Path = "/subscriptions", Kind = CompanionDestinationKind.Commerce, Description = "Current subscription, packages and renewal dates.", Keywords = "subscription renew plan package" },
        new() { Id = "billing", Title = "Billing", Path = "/billing", Kind = CompanionDestinationKind.Commerce, Description = "Invoices, payment methods and receipts.", Keywords = "invoice receipt payment card refund" },

        new() { Id = "support", Title = "Support", Path = "/support", Kind = CompanionDestinationKind.Support, Description = "Contact the support team about an account or technical problem.", Keywords = "help contact problem issue ticket" },
        new() { Id = "escalations", Title = "Escalations", Path = "/escalations", Kind = CompanionDestinationKind.Support, Description = "Raise or track a formal escalation about marking or service.", Keywords = "escalate complaint dispute appeal" },
        new() { Id = "feedback.guide", Title = "Understanding your feedback", Path = "/feedback-guide", Kind = CompanionDestinationKind.Support, Description = "How to read the AI and expert feedback reports.", Keywords = "feedback report criteria explain" },

        // ── Phase 6 additions ───────────────────────────────────────────────
        // Places the companion could not name at all, plus the two immersive
        // surfaces it may now start rather than only describe.

        new() { Id = "register", Title = "Create an account", Path = "/register", Kind = CompanionDestinationKind.Account, Description = "Register for a new account on the platform.", Keywords = "sign up signup join create account new" },
        new() { Id = "sign.in", Title = "Sign in", Path = "/sign-in", Kind = CompanionDestinationKind.Account, Description = "Sign in to an existing account.", Keywords = "login log in signin access" },
        new() { Id = "devices", Title = "Devices", Path = "/settings", Kind = CompanionDestinationKind.Account, Description = "The devices signed in to this account, and how to remove one.", Keywords = "device phone laptop trusted otp code limit remove" },

        // TutorBookUnlocked, NOT IsModuleEnabled — see EvaluateAsync. The two
        // are different flags and gating on the wrong one either hides the book
        // from someone who bought it or advertises it to someone who did not.
        new() { Id = "tutor.book", Title = "The Tutor Book", Path = "/learner/tutor-book", Kind = CompanionDestinationKind.Library, RequiresTutorBook = true, Description = "The Tutor Book, for learners whose package includes it.", Keywords = "tutor book sessions comprehensive" },

        new() { Id = "vocabulary", Title = "Vocabulary", Path = "/vocabulary", Kind = CompanionDestinationKind.Learning, Description = "Browse medical vocabulary, flashcards and quizzes.", Keywords = "word term definition flashcard quiz browse lexis" },
        new() { Id = "vocabulary.favourites", Title = "Saved words", Path = "/recalls/favourites", Kind = CompanionDestinationKind.Learning, RequiredModuleKey = ModuleKeys.Recalls, Description = "Words the learner has bookmarked to revisit.", Keywords = "saved bookmark favourite starred words" },
        new() { Id = "listening.recalls", Title = "Listening recalls", Path = "/listening/practice", Kind = CompanionDestinationKind.Practice, SubtestCode = "listening", Description = "Listening practice built from reported recall material.", Keywords = "recall listening practice reported remembered" },
        new() { Id = "listening.dictation", Title = "Listening dictation", Path = "/listening/dictation", Kind = CompanionDestinationKind.Practice, SubtestCode = "listening", Description = "Dictation drills for Part A note completion and spelling.", Keywords = "dictation spelling note completion type" },
        new() { Id = "reading.drills", Title = "Reading drills", Path = "/reading/drills", Kind = CompanionDestinationKind.Practice, SubtestCode = "reading", Description = "Targeted drills on one Reading part at a time.", Keywords = "drill part a b c skim scan practice" },
        new() { Id = "writing.submissions", Title = "Writing submissions", Path = "/writing/submissions", Kind = CompanionDestinationKind.Progress, SubtestCode = "writing", Description = "Every letter submitted, with its feedback and any appeal.", Keywords = "letter submitted feedback appeal regrade past" },
        new() { Id = "basic.english", Title = "Basic English course", Path = "/materials", Kind = CompanionDestinationKind.Library, RequiredModuleKey = ModuleKeys.BasicEnglish, Description = "General English foundation materials, for learners whose package includes them.", Keywords = "general english basic foundation beginner grammar" },
        new() { Id = "onboarding", Title = "Getting started", Path = "/onboarding", Kind = CompanionDestinationKind.Learning, Description = "The setup walkthrough: profession, exam date and goals.", Keywords = "start setup begin first tour walkthrough new" },

        // Immersive surfaces. Present so the companion can START them, which is
        // the owner's decision to lift the earlier suggest-only rule. Every one
        // is marked Immersive, which forces an explicit confirmation before the
        // learner is dropped into anything timed.
        new() { Id = "writing.start", Title = "Start a writing task", Path = "/writing/practice", Kind = CompanionDestinationKind.Practice, SubtestCode = "writing", Immersive = true, Description = "Begin a new writing practice letter now.", Keywords = "start begin write letter task now practice" },
        new() { Id = "speaking.start", Title = "Start a speaking card", Path = "/speaking/practice", Kind = CompanionDestinationKind.Practice, SubtestCode = "speaking", Immersive = true, Description = "Begin a new speaking role-play card now.", Keywords = "start begin speak card role play now practice" },
        new() { Id = "reading.start", Title = "Start a reading paper", Path = "/reading/practice", Kind = CompanionDestinationKind.Practice, SubtestCode = "reading", Immersive = true, Description = "Begin a reading paper now. It is timed.", Keywords = "start begin reading paper now timed practice" },
        new() { Id = "listening.start", Title = "Start a listening paper", Path = "/listening/practice", Kind = CompanionDestinationKind.Practice, SubtestCode = "listening", Immersive = true, Description = "Begin a listening paper now. It is timed.", Keywords = "start begin listening paper now timed practice" },
        new() { Id = "mock.start", Title = "Start a mock exam", Path = "/mocks", Kind = CompanionDestinationKind.Practice, RequiredModuleKey = ModuleKeys.Mocks, Immersive = true, Description = "Begin a full timed mock exam under test conditions.", Keywords = "start begin mock full exam timed simulation" },
    ];
}
