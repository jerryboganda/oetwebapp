using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// WS4 — Admin Sequence Builder. Verifies the backward-compat keystone
/// (<see cref="ListeningSequenceService.DeriveFromPolicy"/> reproduces the
/// legacy per-window timing exactly), the structural validator, and the
/// publish-gate on <see cref="ListeningSequenceService.ReplaceAsync"/>.
/// </summary>
public class ListeningSequenceServiceTests
{
    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    // ── Oracle: the EXACT legacy ListeningSessionService.ComputeWindowMs logic
    //    (base switch + extra-time multiplier) as it shipped before WS4. The
    //    derived sequence must reproduce this for every ForwardPath state. ──
    private static int LegacyComputeWindowMs(string state, EffectiveListeningPolicy p, IListeningModePolicy mode)
    {
        int Apply(int ms) => p.ExtraTimePct > 0
            ? (int)Math.Round(ms * (1.0 + p.ExtraTimePct / 100.0))
            : ms;

        return state switch
        {
            ListeningFsmTransitions.A1Preview => Apply(p.PreviewMsA1),
            ListeningFsmTransitions.A2Preview => Apply(p.PreviewMsA2),
            ListeningFsmTransitions.C1Preview => Apply(p.PreviewMsC1),
            ListeningFsmTransitions.C2Preview => Apply(p.PreviewMsC2),
            ListeningFsmTransitions.A1Review => Apply(p.ReviewMsA1),
            ListeningFsmTransitions.A2Review => Apply(p.ReviewMsA2),
            ListeningFsmTransitions.C1Review => Apply(p.ReviewMsC1),
            ListeningFsmTransitions.C2Review => Apply(p.ReviewMsC2FinalCbt),
            ListeningFsmTransitions.C2FinalReview => Apply(mode.FinalReviewAllPartsMs ?? p.ReviewMsC2FinalCbt),
            ListeningFsmTransitions.BIntro => Apply(p.BetweenSectionTransitionMs),
            _ => 0,
        };
    }

    private static int ApplyExtraTime(EffectiveListeningPolicy p, int ms)
        => p.ExtraTimePct > 0 ? (int)Math.Round(ms * (1.0 + p.ExtraTimePct / 100.0)) : ms;

    private static EffectiveListeningPolicy DefaultsPolicy()
        => ListeningPolicyResolver.Resolve(null, null);

    private static EffectiveListeningPolicy ExtraTimePolicy(int pct)
        => ListeningPolicyResolver.Resolve(
            null,
            new ListeningUserPolicyOverride { UserId = "u", ExtraTimeEntitlementPct = pct });

    [Fact]
    public void DeriveFromPolicy_reproduces_legacy_base_window_for_every_forward_path_state()
    {
        var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var policy = DefaultsPolicy();
        var mode = new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam);

        var sequence = svc.DeriveFromPolicy(policy, mode);

        // One item per ForwardPath state, in order.
        Assert.Equal(ListeningFsmTransitions.ForwardPath.Count, sequence.Items.Count);
        for (var i = 0; i < ListeningFsmTransitions.ForwardPath.Count; i++)
        {
            var state = ListeningFsmTransitions.ForwardPath[i];
            var item = sequence.Items[i];
            Assert.Equal(state, item.Label);

            // The derived base duration must equal the legacy switch result
            // computed with ExtraTimePct=0 (defaults policy has no extra time).
            var legacyBase = LegacyComputeWindowMs(state, policy, mode);
            Assert.Equal(legacyBase, item.DurationMs);

            // And WindowMsForState must surface the same number.
            Assert.Equal(legacyBase, ListeningSequenceService.WindowMsForState(sequence, state));
        }
    }

    [Fact]
    public void DeriveFromPolicy_plus_extra_time_matches_legacy_window_with_entitlement()
    {
        var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var policy = ExtraTimePolicy(25);
        var mode = new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam);

        var sequence = svc.DeriveFromPolicy(policy, mode);

        // The session FSM applies extra-time on top of the derived BASE value.
        // That composition must equal the legacy ComputeWindowMs (which folded
        // extra-time into each branch).
        foreach (var state in ListeningFsmTransitions.ForwardPath)
        {
            var baseMs = ListeningSequenceService.WindowMsForState(sequence, state) ?? 0;
            var withExtra = ApplyExtraTime(policy, baseMs);
            Assert.Equal(LegacyComputeWindowMs(state, policy, mode), withExtra);
        }
    }

    [Fact]
    public void DeriveFromPolicy_paper_mode_uses_final_review_all_parts_window()
    {
        var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var policy = DefaultsPolicy();
        var paperMode = new ListeningModePolicyResolver().For(ListeningAttemptMode.Paper);

        var sequence = svc.DeriveFromPolicy(policy, paperMode);

        var c2Final = ListeningSequenceService.WindowMsForState(sequence, ListeningFsmTransitions.C2FinalReview);
        Assert.Equal(ListeningPolicyDefaults.FinalReviewAllPartsMsPaper, c2Final);
    }

    [Fact]
    public void Validate_accepts_canonical_sequence_over_canonical_structure()
    {
        var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var canonical = svc.DeriveFromPolicy(DefaultsPolicy(), new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam));
        var structure = CanonicalStructure();

        var report = svc.Validate(canonical, structure);

        Assert.True(report.IsValid, string.Join(" | ", report.Issues.Select(i => i.Message)));
        Assert.Equal(42, report.Counts.TotalItems);
    }

    [Fact]
    public void Validate_rejects_reordered_sequence()
    {
        var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var canonical = svc.DeriveFromPolicy(DefaultsPolicy(), new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam));

        // Swap two adjacent phases (a1_preview ↔ a1_audio) — a reordering.
        var items = canonical.Items.ToList();
        (items[1], items[2]) = (items[2], items[1]);
        var reordered = canonical with { Items = items };

        var report = svc.Validate(reordered, CanonicalStructure());

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, i => i.Code == "listening_sequence_phase_order");
    }

    [Fact]
    public void Validate_rejects_missing_phase()
    {
        var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var canonical = svc.DeriveFromPolicy(DefaultsPolicy(), new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam));

        var items = canonical.Items.Skip(1).ToList(); // drop intro
        var missing = canonical with { Items = items };

        var report = svc.Validate(missing, CanonicalStructure());

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, i => i.Code == "listening_sequence_phase_count");
    }

    [Fact]
    public void Validate_rejects_extra_phase()
    {
        var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var canonical = svc.DeriveFromPolicy(DefaultsPolicy(), new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam));

        var items = canonical.Items.ToList();
        items.Add(new ListeningSequenceItem(items.Count, ListeningSequenceService.TypeBeep, null, null, 1000, "extra"));
        var extra = canonical with { Items = items };

        var report = svc.Validate(extra, CanonicalStructure());

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, i => i.Code == "listening_sequence_phase_count");
    }

    [Fact]
    public void Validate_rejects_short_coverage()
    {
        var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var canonical = svc.DeriveFromPolicy(DefaultsPolicy(), new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam));

        // Canonical phase order is intact, but the authored structure is short.
        var structure = new ListeningSequenceStructure(
            PartACount: 20, PartBCount: 6, PartCCount: 12,
            AuthoredExtractPartCodes: new HashSet<string>(StringComparer.Ordinal) { "A1", "A2", "B", "C1", "C2" });

        var report = svc.Validate(canonical, structure);

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, i => i.Code == "listening_sequence_part_a_count");
    }

    [Fact]
    public void Validate_rejects_audio_extract_with_no_authored_extract()
    {
        var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var canonical = svc.DeriveFromPolicy(DefaultsPolicy(), new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam));

        // Canonical sequence, full coverage, but NO authored extracts at all —
        // every audio_extract phase fails to resolve.
        var structure = new ListeningSequenceStructure(
            PartACount: 24, PartBCount: 6, PartCCount: 12,
            AuthoredExtractPartCodes: new HashSet<string>(StringComparer.Ordinal));

        var report = svc.Validate(canonical, structure);

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, i => i.Code == "listening_sequence_audio_unresolved");
    }

    [Fact]
    public async Task ReplaceAsync_persists_valid_sequence_and_writes_audit_event()
    {
        await using var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var paper = SeedPaper(db, ContentStatus.Draft);
        var canonical = svc.DeriveFromPolicy(DefaultsPolicy(), new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam));

        var report = await svc.ReplaceAsync(paper.Id, canonical, "admin-1", CancellationToken.None);

        Assert.True(report.IsValid);
        var reloaded = await db.ContentPapers.SingleAsync(p => p.Id == paper.Id);
        Assert.False(string.IsNullOrWhiteSpace(reloaded.ListeningSequenceJson));
        Assert.Equal(1, reloaded.RowVersion);
        Assert.Contains(db.AuditEvents, e => e.Action == "ListeningSequenceUpdated" && e.ResourceId == paper.Id);

        // Round-trips back through GetAsync.
        var fetched = await svc.GetAsync(paper.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(canonical.Items.Count, fetched!.Items.Count);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_when_paper_published()
    {
        await using var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var paper = SeedPaper(db, ContentStatus.Published);
        var canonical = svc.DeriveFromPolicy(DefaultsPolicy(), new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam));

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.ReplaceAsync(paper.Id, canonical, "admin-1", CancellationToken.None));

        Assert.Equal("listening_sequence_paper_published", ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);

        var reloaded = await db.ContentPapers.SingleAsync(p => p.Id == paper.Id);
        Assert.Null(reloaded.ListeningSequenceJson);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_invalid_sequence()
    {
        await using var db = NewDb();
        var svc = new ListeningSequenceService(db);
        var paper = SeedPaper(db, ContentStatus.Draft);
        var canonical = svc.DeriveFromPolicy(DefaultsPolicy(), new ListeningModePolicyResolver().For(ListeningAttemptMode.Exam));
        var items = canonical.Items.Skip(2).ToList(); // structurally broken
        var broken = canonical with { Items = items };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.ReplaceAsync(paper.Id, broken, "admin-1", CancellationToken.None));

        Assert.Equal("listening_sequence_invalid", ex.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_missing_paper_and_blank_json()
    {
        await using var db = NewDb();
        var svc = new ListeningSequenceService(db);

        Assert.Null(await svc.GetAsync("does-not-exist", CancellationToken.None));

        var paper = SeedPaper(db, ContentStatus.Draft); // ListeningSequenceJson null
        Assert.Null(await svc.GetAsync(paper.Id, CancellationToken.None));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ContentPaper SeedPaper(LearnerDbContext db, ContentStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var paper = new ContentPaper
        {
            Id = $"paper-{Guid.NewGuid():N}",
            SubtestCode = "listening",
            Title = "Sequence Test Paper",
            Slug = $"seq-{Guid.NewGuid():N}",
            Status = status,
            Difficulty = "standard",
            AppliesToAllProfessions = true,
            EstimatedDurationMinutes = 45,
            CreatedAt = now,
            UpdatedAt = now,
            ExtractedTextJson = CanonicalStructureJson(),
        };
        db.ContentPapers.Add(paper);
        db.SaveChanges();
        return paper;
    }

    private static ListeningSequenceStructure CanonicalStructure()
        => ListeningSequenceService.ReadStructure(CanonicalStructureJson());

    /// <summary>Build an ExtractedTextJson blob with the canonical 42-question
    /// coverage (A1×12, A2×12, B×6, C1×6, C2×6) plus one authored extract per
    /// part code.</summary>
    private static string CanonicalStructureJson()
    {
        var questions = new List<object>();
        void Add(int from, int to, string part)
        {
            for (var n = from; n <= to; n++)
                questions.Add(new { number = n, partCode = part });
        }
        Add(1, 12, "A1");
        Add(13, 24, "A2");
        Add(25, 30, "B");
        Add(31, 36, "C1");
        Add(37, 42, "C2");

        var extracts = new[] { "A1", "A2", "B", "C1", "C2" }
            .Select((code, i) => new { partCode = code, displayOrder = i })
            .Cast<object>()
            .ToList();

        return JsonSerializer.Serialize(new
        {
            listeningQuestions = questions,
            listeningExtracts = extracts,
        });
    }
}
