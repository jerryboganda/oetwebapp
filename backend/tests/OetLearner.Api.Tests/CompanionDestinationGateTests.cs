using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Entitlements;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Phase 6 — the destinations that did not exist, the expiry gate that was
/// missing, and the owner's decision that the companion may now start and
/// continue an activity rather than only describe one.
///
/// <para>
/// Expiry is the one that mattered most and was hardest to see. A lapsed
/// learner's module list can still read as enabled while their access date has
/// passed, so every gated destination resolved and handed them a working link
/// into content they no longer had. Nothing errored; they simply kept getting in.
/// </para>
/// </summary>
public sealed class CompanionDestinationGateTests
{
    // ── the new destinations ─────────────────────────────────────────────────

    [Theory]
    [InlineData("register")]
    [InlineData("tutor.book")]
    [InlineData("vocabulary")]
    [InlineData("listening.recalls")]
    [InlineData("basic.english")]
    [InlineData("onboarding")]
    public void The_places_the_companion_could_not_name_now_exist(string id)
    {
        Assert.Contains(CompanionDestinationRegistry.CatalogForIndexing,
            d => string.Equals(d.Id, id, StringComparison.Ordinal));
    }

    [Fact]
    public void The_tutor_book_is_gated_on_its_own_unlock_not_on_a_module()
    {
        // TutorBookUnlocked and IsModuleEnabled are different flags. Checking the
        // module would be wrong in both directions: it would hide the book from
        // someone who bought the add-on, and advertise it to someone on a legacy
        // plan whose module checks fail open.
        var tutorBook = Find("tutor.book");

        Assert.True(tutorBook.RequiresTutorBook);
        Assert.Null(tutorBook.RequiredModuleKey);
    }

    [Fact]
    public void Registration_and_sign_in_are_reachable_without_a_package()
    {
        // Gating the sign-up route behind entitlement would be its own joke.
        var snapshot = Snapshot();

        Assert.True(CompanionDestinationRegistry.Gate(Find("register"), Context(), snapshot).Allowed);
        Assert.True(CompanionDestinationRegistry.Gate(Find("sign.in"), Context(), snapshot).Allowed);
    }

    [Fact]
    public void Every_catalog_entry_still_has_a_real_path_and_no_stale_persona()
    {
        foreach (var destination in CompanionDestinationRegistry.CatalogForIndexing)
        {
            Assert.StartsWith("/", destination.Path, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(destination.Description));

            var searchable = $"{destination.Id} {destination.Title} {destination.Description} {destination.Keywords}";
            Assert.DoesNotContain("jana", searchable, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── expiry ───────────────────────────────────────────────────────────────

    [Fact]
    public void An_expired_learner_is_refused_and_pointed_at_renewal()
    {
        var expired = Snapshot(
            modules: [ModuleKeys.Recalls],
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        var result = CompanionDestinationRegistry.Gate(Find("vocabulary.recalls"), Context(), expired);

        Assert.False(result.Allowed);
        Assert.Equal("access_expired", result.Reason);
        Assert.Equal("renew", result.RequiredScope);
        Assert.Null(result.Url);
    }

    [Fact]
    public void Expiry_beats_a_module_list_that_still_says_yes()
    {
        // The specific shape of the bug: the module reads enabled, the date has
        // passed, and without this check the learner is let straight in.
        var expired = Snapshot(
            modules: [ModuleKeys.Recalls],
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.True(expired.IsModuleEnabled(ModuleKeys.Recalls));
        Assert.False(CompanionDestinationRegistry.Gate(Find("vocabulary.recalls"), Context(), expired).Allowed);
    }

    [Fact]
    public void A_learner_whose_access_has_not_yet_lapsed_still_gets_in()
    {
        var live = Snapshot(
            modules: [ModuleKeys.Recalls],
            expiresAt: DateTimeOffset.UtcNow.AddDays(30));

        Assert.True(CompanionDestinationRegistry.Gate(Find("vocabulary.recalls"), Context(), live).Allowed);
    }

    [Fact]
    public void A_permanent_package_has_no_expiry_to_fail()
    {
        // Null ExpiresAt means "never expires", not "expired at the epoch".
        var permanent = Snapshot(modules: [ModuleKeys.Recalls], expiresAt: null);

        Assert.True(CompanionDestinationRegistry.Gate(Find("vocabulary.recalls"), Context(), permanent).Allowed);
    }

    [Fact]
    public void Expiry_does_not_block_the_ungated_places()
    {
        // An expired learner still needs to reach pricing, billing and support —
        // those are exactly where they go to fix it.
        var expired = Snapshot(expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        Assert.True(CompanionDestinationRegistry.Gate(Find("pricing"), Context(), expired).Allowed);
        Assert.True(CompanionDestinationRegistry.Gate(Find("billing"), Context(), expired).Allowed);
        Assert.True(CompanionDestinationRegistry.Gate(Find("support"), Context(), expired).Allowed);
    }

    [Fact]
    public void An_expired_learner_cannot_reach_the_tutor_book_either()
    {
        var expired = Snapshot(tutorBook: true, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Equal("access_expired",
            CompanionDestinationRegistry.Gate(Find("tutor.book"), Context(), expired).Reason);
    }

    [Fact]
    public void Without_the_unlock_the_tutor_book_names_what_is_needed()
    {
        var result = CompanionDestinationRegistry.Gate(Find("tutor.book"), Context(), Snapshot());

        Assert.False(result.Allowed);
        Assert.Equal("tutor-book", result.RequiredScope);
    }

    [Fact]
    public void With_the_unlock_the_tutor_book_opens()
    {
        Assert.True(CompanionDestinationRegistry
            .Gate(Find("tutor.book"), Context(), Snapshot(tutorBook: true)).Allowed);
    }

    // ── start and continue ───────────────────────────────────────────────────

    [Fact]
    public void Starting_an_activity_is_allowed_but_requires_confirmation()
    {
        // The owner lifted the ban on starting activities, not the requirement to
        // ask. Dropping a learner into a timed paper they did not ask for is a
        // worse failure than one more click.
        var result = CompanionDestinationRegistry.Gate(Find("writing.start"), Context(), Snapshot());

        Assert.True(result.Allowed);
        Assert.True(result.RequiresConfirmation);
    }

    [Fact]
    public void An_ordinary_destination_needs_no_confirmation()
    {
        var result = CompanionDestinationRegistry.Gate(Find("study.plan"), Context(), Snapshot());

        Assert.True(result.Allowed);
        Assert.False(result.RequiresConfirmation);
    }

    [Fact]
    public void No_timed_activity_can_be_started_during_a_protected_attempt()
    {
        // Starting a second attempt mid-exam is precisely the sort of helpful
        // action that costs a candidate their sitting.
        var inExam = Context() with { ExamMode = true };

        foreach (var immersive in CompanionDestinationRegistry.CatalogForIndexing.Where(d => d.Immersive))
        {
            var result = CompanionDestinationRegistry.Gate(
                immersive, inExam, Snapshot(modules: [ModuleKeys.Mocks]));

            Assert.False(result.Allowed, $"{immersive.Id} was startable during an exam");
            Assert.Equal("exam_in_progress", result.Reason);
        }
    }

    [Fact]
    public void Every_subtest_can_be_started_and_continued()
    {
        // Pack 4 s10 and s11 need a resume point for each subtest; a missing one
        // degrades to "go to the dashboard", which is not continuing anything.
        foreach (var subtest in new[] { "writing", "speaking", "reading", "listening" })
        {
            Assert.Contains(CompanionDestinationRegistry.CatalogForIndexing,
                d => d.Immersive && string.Equals(d.SubtestCode, subtest, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void A_refusal_never_carries_a_url()
    {
        // The whole navigation contract in one property: if the learner may not
        // open it, there is nothing to click.
        var expired = Snapshot(modules: [ModuleKeys.Mocks], expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        foreach (var destination in CompanionDestinationRegistry.CatalogForIndexing)
        {
            var result = CompanionDestinationRegistry.Gate(destination, Context() with { ExamMode = true }, expired);
            if (!result.Allowed) Assert.Null(result.Url);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static CompanionDestination Find(string id) =>
        CompanionDestinationRegistry.CatalogForIndexing
            .Single(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    private static CompanionTurnContext Context() => new()
    {
        UserId = "learner-1",
        ProfessionId = "medicine",
        ActionsEnabled = true,
    };

    private static EffectiveEntitlementSnapshot Snapshot(
        string[]? modules = null,
        bool tutorBook = false,
        DateTimeOffset? expiresAt = null) =>
        new(
            UserId: "learner-1",
            HasEligibleSubscription: modules is { Length: > 0 },
            IsTrial: false,
            Tier: modules is { Length: > 0 } ? "paid" : "free",
            SubscriptionId: null,
            SubscriptionStatus: null,
            PlanId: null,
            PlanVersionId: null,
            PlanCode: "test-plan",
            AiQuotaPlanCode: null,
            AiQuotaPlanCodeSource: null,
            ActiveAddOnCodes: [],
            IsFrozen: false,
            Trace: [])
        {
            EnabledModules = modules ?? [],
            HasNoExplicitModuleConfig = modules is null,
            TutorBookUnlocked = tutorBook,
            ExpiresAt = expiresAt,
        };
}
