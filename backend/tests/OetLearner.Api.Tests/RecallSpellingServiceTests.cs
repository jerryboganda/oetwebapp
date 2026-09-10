using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Tests;

/// <summary>
/// Recall spelling — Practice Spelling / mini Spelling Test (§3B/§3C) and the
/// persisted Review Mistakes list (§3D).
///
/// The properties this file exists to protect:
/// <list type="bullet">
/// <item>pass/fail is a direct, ordinal comparison — no AI, no fuzzy matching;</item>
/// <item>a missed word is stored server-side, counted, and removed on a correct
/// answer;</item>
/// <item>the mistakes list survives a fresh <see cref="LearnerDbContext"/> (i.e. it
/// is not device-local state);</item>
/// <item>only the existing recall word is referenced — no word or audio copy.</item>
/// </list>
/// </summary>
public class RecallSpellingServiceTests
{
    private const string User = "user-1";

    private static LearnerDbContext NewDb(string name)
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    private static VocabularyTerm Term(
        string id,
        string term,
        string? audioMediaAssetId = "asset-1",
        bool isFreePreview = false,
        string category = "symptoms",
        int examFrequencyCount = 0)
        => new()
        {
            Id = id,
            Term = term,
            Definition = $"Definition of {term}.",
            ExampleSentence = $"A clinical sentence using {term}.",
            ExamTypeCode = "oet",
            Category = category,
            Status = "active",
            RecallSetCodesJson = "[\"2026\"]",
            AudioMediaAssetId = audioMediaAssetId,
            IsFreePreview = isFreePreview,
            ExamFrequencyCount = examFrequencyCount,
        };

    // ── §3B comparison rule ───────────────────────────────────────────────

    [Theory]
    // exact
    [InlineData("dyspnoea", "dyspnoea", true)]
    // case is ignored
    [InlineData("dyspnoea", "Dyspnoea", true)]
    [InlineData("Dyspnoea", "DYSPNOEA", true)]
    // leading/trailing spaces are ignored
    [InlineData("dyspnoea", "  dyspnoea  ", true)]
    [InlineData("dyspnoea", "\tdyspnoea\n", true)]
    // internal spaces must match exactly
    [InlineData("advance care plan", "advance care plan", true)]
    [InlineData("advance care plan", "advancecareplan", false)]
    [InlineData("advance care plan", "advance  care plan", false)]
    // hyphens must match exactly
    [InlineData("well-being", "well-being", true)]
    [InlineData("well-being", "wellbeing", false)]
    [InlineData("wellbeing", "well-being", false)]
    // genuine misspellings
    [InlineData("dyspnoea", "dyspnea", false)]
    [InlineData("dyspnoea", "dyspnoe", false)]
    [InlineData("dyspnoea", "", false)]
    public void IsCorrectSpelling_follows_the_brief_comparison_rule(
        string canonical, string typed, bool expected)
        => Assert.Equal(expected, RecallSpellingService.IsCorrectSpelling(canonical, typed));

    // ── mistakes are recorded on a wrong answer ───────────────────────────

    [Fact]
    public async Task Wrong_answer_creates_one_mistake_row_referencing_the_existing_word()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.Add(Term("t1", "dyspnoea"));
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);

        var result = await svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", "dyspnea"), true, default);

        Assert.False(result.Correct);
        Assert.Equal("dyspnoea", result.Canonical);
        Assert.True(result.AddedToMistakes);
        Assert.False(result.RemovedFromMistakes);
        Assert.Equal(1, result.WrongAttemptCount);

        var row = Assert.Single(db.RecallSpellingMistakes);
        Assert.Equal("t1", row.VocabularyTermId);   // reference only — no word copy
        Assert.Equal(1, row.WrongAttemptCount);
        // The word and its audio still live solely on the vocabulary term.
        var term = await db.VocabularyTerms.SingleAsync(t => t.Id == "t1");
        Assert.Equal("asset-1", term.AudioMediaAssetId);
    }

    [Fact]
    public async Task Spelling_the_same_word_wrong_again_keeps_one_row_and_increments_the_count()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.Add(Term("t1", "dyspnoea"));
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);

        await svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", "dyspnea"), true, default);
        var second = await svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", "dyspnoe"), true, default);
        var third = await svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", "dispn ea"), true, default);

        Assert.Equal(2, second.WrongAttemptCount);
        Assert.False(second.AddedToMistakes);
        Assert.Equal(3, third.WrongAttemptCount);

        var row = Assert.Single(db.RecallSpellingMistakes);
        Assert.Equal(3, row.WrongAttemptCount);
    }

    [Fact]
    public async Task A_correct_answer_removes_the_word_from_review_mistakes()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.Add(Term("t1", "dyspnoea"));
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);

        await svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", "dyspnea"), true, default);
        Assert.Single(db.RecallSpellingMistakes);

        var fixedAttempt = await svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", "Dyspnoea"), true, default);

        Assert.True(fixedAttempt.Correct);
        Assert.True(fixedAttempt.RemovedFromMistakes);
        Assert.Equal(0, fixedAttempt.WrongAttemptCount);
        Assert.Empty(db.RecallSpellingMistakes);
    }

    [Fact]
    public async Task A_correct_answer_for_a_word_that_was_never_missed_is_a_no_op()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.Add(Term("t1", "dyspnoea"));
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);

        var result = await svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", "dyspnoea"), true, default);

        Assert.True(result.Correct);
        Assert.False(result.AddedToMistakes);
        Assert.False(result.RemovedFromMistakes);
        Assert.Empty(db.RecallSpellingMistakes);
    }

    [Fact]
    public async Task Mistakes_survive_a_new_context_so_the_list_is_not_device_local()
    {
        var name = Guid.NewGuid().ToString("N");
        await using (var first = NewDb(name))
        {
            first.VocabularyTerms.Add(Term("t1", "dyspnoea"));
            await first.SaveChangesAsync();
            await new RecallSpellingService(first)
                .CheckAsync(User, new RecallSpellingCheckRequest("t1", "dyspnea"), true, default);
        }

        // A brand-new context stands in for a logout / app restart / other device:
        // the mistake is still there because it lives in the database, not the client.
        await using var reopened = NewDb(name);
        var mistakes = await new RecallSpellingService(reopened).GetMistakesAsync(User, true, default);

        var item = Assert.Single(mistakes.Items);
        Assert.Equal("t1", item.TermId);
        Assert.Equal("dyspnoea", item.Term);
        Assert.Equal(1, item.WrongAttemptCount);
    }

    [Fact]
    public async Task Mistakes_are_scoped_per_learner()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.Add(Term("t1", "dyspnoea"));
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);

        await svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", "dyspnea"), true, default);

        Assert.Empty((await svc.GetMistakesAsync("user-2", true, default)).Items);
        Assert.Single((await svc.GetMistakesAsync(User, true, default)).Items);
    }

    [Fact]
    public async Task Mistakes_are_returned_most_recently_missed_first()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.AddRange(Term("t1", "dyspnoea"), Term("t2", "hypertension"));
        // Explicit timestamps keep the assertion deterministic — UtcNow has coarse
        // resolution and three back-to-back writes can land in the same tick.
        var now = DateTimeOffset.UtcNow;
        db.RecallSpellingMistakes.AddRange(
            new RecallSpellingMistake
            {
                Id = "m1", UserId = User, VocabularyTermId = "t1", WrongAttemptCount = 2,
                LastWrongAt = now.AddMinutes(-30), CreatedAt = now.AddMinutes(-30),
            },
            new RecallSpellingMistake
            {
                Id = "m2", UserId = User, VocabularyTermId = "t2", WrongAttemptCount = 1,
                LastWrongAt = now.AddMinutes(-5), CreatedAt = now.AddMinutes(-5),
            });
        await db.SaveChangesAsync();

        var mistakes = await new RecallSpellingService(db).GetMistakesAsync(User, true, default);

        Assert.Equal(new[] { "t2", "t1" }, mistakes.Items.Select(i => i.TermId).ToArray());
        Assert.Equal(2, mistakes.Items[1].WrongAttemptCount);
        Assert.True(mistakes.Items[0].HasAudio);
    }

    [Fact]
    public async Task A_repeat_miss_refreshes_last_wrong_at_so_the_word_floats_back_to_the_top()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.AddRange(Term("t1", "dyspnoea"), Term("t2", "hypertension"));
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);

        await svc.CheckAsync(User, new RecallSpellingCheckRequest("t2", "wrong"), true, default);
        await svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", "wrong"), true, default);

        var t2 = await db.RecallSpellingMistakes.SingleAsync(m => m.VocabularyTermId == "t2");
        Assert.True(t2.LastWrongAt >= before);
        Assert.Equal(2, (await svc.GetMistakesAsync(User, true, default)).Items.Count);
    }

    // ── §3C word sets ─────────────────────────────────────────────────────

    [Fact]
    public async Task Set_excludes_words_that_have_no_audio_because_the_test_is_audio_driven()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.AddRange(
            Term("t1", "dyspnoea", audioMediaAssetId: "asset-1"),
            Term("t2", "silent", audioMediaAssetId: null));
        await db.SaveChangesAsync();

        var set = await new RecallSpellingService(db).GetSetAsync(User, "all", "all", true, default);

        var only = Assert.Single(set.Items);
        Assert.Equal("t1", only.TermId);
    }

    [Fact]
    public async Task Set_from_favorites_uses_the_learners_bookmarks_only()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.AddRange(Term("t1", "dyspnoea"), Term("t2", "hypertension"));
        db.RecallBookmarks.Add(new RecallBookmark
        {
            Id = "bm1", UserId = User, VocabularyTermId = "t2", Source = "user",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var set = await new RecallSpellingService(db).GetSetAsync(User, "all", "favorites", true, default);

        var only = Assert.Single(set.Items);
        Assert.Equal("t2", only.TermId);
        Assert.False(only.FromMistakes);
    }

    [Fact]
    public async Task Set_from_mistakes_flags_the_words_that_came_from_review_mistakes()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.AddRange(Term("t1", "dyspnoea"), Term("t2", "hypertension"));
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);

        await svc.CheckAsync(User, new RecallSpellingCheckRequest("t2", "wrong"), true, default);
        var set = await svc.GetSetAsync(User, "all", "mistakes", true, default);

        var only = Assert.Single(set.Items);
        Assert.Equal("t2", only.TermId);
        Assert.True(only.FromMistakes);
    }

    [Fact]
    public async Task Set_respects_the_requested_size_and_all_words_returns_everything()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        for (var i = 0; i < 25; i++) db.VocabularyTerms.Add(Term($"t{i}", $"word{i}"));
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);

        Assert.Equal(10, (await svc.GetSetAsync(User, "10", "all", true, default)).Items.Count);
        Assert.Equal(20, (await svc.GetSetAsync(User, "20", "all", true, default)).Items.Count);
        Assert.Equal(25, (await svc.GetSetAsync(User, "all", "all", true, default)).Items.Count);
    }

    [Fact]
    public async Task Set_does_not_leak_the_canonical_spelling()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.Add(Term("t1", "dyspnoea"));
        await db.SaveChangesAsync();

        var set = await new RecallSpellingService(db).GetSetAsync(User, "all", "all", true, default);

        // The DTO carries an id + display metadata only. The answer must not be
        // present anywhere in the payload before the learner presses Check.
        Assert.DoesNotContain(
            typeof(RecallSpellingSetItem).GetProperties(),
            p => p.Name is "Term" or "Canonical" or "Definition");
    }

    // ── paywall / locking ─────────────────────────────────────────────────

    [Fact]
    public async Task Non_premium_learners_only_see_free_preview_words()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.AddRange(
            Term("t1", "free-word", isFreePreview: true),
            Term("t2", "locked-word"));
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);

        var free = await svc.GetSetAsync(User, "all", "all", isPremium: false, default);
        var paid = await svc.GetSetAsync(User, "all", "all", isPremium: true, default);

        Assert.Equal("t1", Assert.Single(free.Items).TermId);
        Assert.Equal(2, paid.Items.Count);
    }

    [Fact]
    public async Task Non_premium_learners_cannot_grade_a_locked_word()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.Add(Term("t2", "locked-word"));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            new RecallSpellingService(db)
                .CheckAsync(User, new RecallSpellingCheckRequest("t2", "locked-word"), false, default));

        Assert.Equal(402, ex.StatusCode);
        Assert.Equal("subscription_required", ex.ErrorCode);
        Assert.Empty(db.RecallSpellingMistakes);
    }

    [Fact]
    public async Task Unknown_or_non_recall_terms_are_rejected()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "t3", Term = "orphan", Definition = "d", ExampleSentence = "e",
            ExamTypeCode = "oet", Category = "c", Status = "active", RecallSetCodesJson = "[]",
        });
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);

        await Assert.ThrowsAsync<ApiException>(() =>
            svc.CheckAsync(User, new RecallSpellingCheckRequest("nope", "x"), true, default));
        await Assert.ThrowsAsync<ApiException>(() =>
            svc.CheckAsync(User, new RecallSpellingCheckRequest("t3", "orphan"), true, default));
    }

    [Fact]
    public async Task A_blank_answer_is_rejected_rather_than_counted_as_a_mistake()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = NewDb(name);
        db.VocabularyTerms.Add(Term("t1", "dyspnoea"));
        await db.SaveChangesAsync();
        var svc = new RecallSpellingService(db);

        await Assert.ThrowsAsync<ApiException>(() =>
            svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", null), true, default));
        await Assert.ThrowsAsync<ApiException>(() =>
            svc.CheckAsync(User, new RecallSpellingCheckRequest("  ", "x"), true, default));

        // An empty string is a legitimate (wrong) answer, not a validation error.
        var empty = await svc.CheckAsync(User, new RecallSpellingCheckRequest("t1", ""), true, default);
        Assert.False(empty.Correct);
    }
}
