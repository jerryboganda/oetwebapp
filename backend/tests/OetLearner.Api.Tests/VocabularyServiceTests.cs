using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class VocabularyServiceTests
{
    private static (LearnerDbContext db, VocabularyService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new VocabularyService(db, new Sm2Scheduler()));
    }

    private static async Task SeedAsync(LearnerDbContext db, int count = 5, string category = "medical")
    {
        var offset = await db.VocabularyTerms.CountAsync();
        for (var i = 1; i <= count; i++)
        {
            var idNum = offset + i;
            db.VocabularyTerms.Add(new VocabularyTerm
            {
                Id = $"vt-{idNum:000}",
                Term = $"term{idNum}",
                Definition = $"definition of term{idNum}",
                ExampleSentence = $"Example using term{idNum}.",
                ExamTypeCode = "oet",
                Category = category,
                Difficulty = "medium",
                Status = "active",
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetTerms_respects_category_filter_and_returns_typed_page()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 3, "medical");
        await SeedAsync(db, 2, "symptoms");

        var page = await svc.GetTermsAsync("oet", "symptoms", null, null, 1, 20, default);

        Assert.Equal(2, page.Total);
        Assert.All(page.Terms, t => Assert.Equal("symptoms", t.Category));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddToMyVocabulary_is_idempotent()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 1);

        var r1 = await svc.AddToMyVocabularyAsync("user-1", "vt-001", null, false, default);
        var r2 = await svc.AddToMyVocabularyAsync("user-1", "vt-001", null, false, default);

        Assert.True(r1.Added);
        Assert.False(r2.Added);
        Assert.Equal(1, await db.LearnerVocabularies.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddToMyVocabulary_enforces_free_tier_cap_for_non_premium()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 501);

        // Seed 500 cards → next add should 402.
        for (var i = 1; i <= 500; i++)
        {
            await svc.AddToMyVocabularyAsync("user-cap", $"vt-{i:000}", null, false, default);
        }

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.AddToMyVocabularyAsync("user-cap", "vt-501", null, false, default));

        Assert.Equal("VOCAB_FREE_CAP_REACHED", ex.ErrorCode);
        Assert.Equal(402, ex.StatusCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task AddToMyVocabulary_bypasses_cap_for_premium()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 501);

        for (var i = 1; i <= 500; i++)
        {
            await svc.AddToMyVocabularyAsync("user-premium", $"vt-{i:000}", null, true, default);
        }

        var r = await svc.AddToMyVocabularyAsync("user-premium", "vt-501", null, true, default);
        Assert.True(r.Added);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SubmitFlashcardReview_advances_schedule_and_updates_mastery()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 1);
        var added = await svc.AddToMyVocabularyAsync("u1", "vt-001", null, false, default);

        var res = await svc.SubmitFlashcardReviewAsync("u1", added.Item.Id, quality: 4, default);

        Assert.Equal("learning", res.Mastery);
        Assert.Equal(1, res.ReviewCount);
        Assert.Equal(1, res.IntervalDays);
        Assert.True(res.NextReviewDate > DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-1)));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SubmitQuiz_awards_xp_and_reports_newly_mastered_terms()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 1);
        var added = await svc.AddToMyVocabularyAsync("u1", "vt-001", null, false, default);

        // Push the learner to the edge of "mastered": 9 correct reviews.
        for (var i = 0; i < 9; i++)
            await svc.SubmitFlashcardReviewAsync("u1", added.Item.Id, 4, default);

        // One more correct answer in a quiz → should tip to "mastered".
        var submission = new VocabQuizSubmissionV2(
            Format: "definition_match",
            Answers: new List<VocabQuizAnswerV2>
            {
                new("vt-001", Correct: true, UserAnswer: "definition of term1")
            },
            DurationSeconds: 30);
        var result = await svc.SubmitQuizAsync("u1", submission, default);

        Assert.Equal(1, result.TermsQuizzed);
        Assert.Equal(1, result.CorrectCount);
        Assert.Equal(100, result.Score);
        Assert.Equal(10 + 25, result.XpAwarded);   // 10 per correct + 25 bonus
        Assert.Contains("vt-001", result.NewlyMasteredTermIds);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetQuiz_returns_no_repeated_correct_answers_in_distractors()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 8);

        var res = await svc.GetQuizAsync("u1", 5, "definition_match", default);

        foreach (var q in res.Questions)
        {
            var correct = q.CorrectAnswer;
            var distractors = q.Options.Where((_, i) => i != q.CorrectIndex).ToList();
            Assert.DoesNotContain(correct, distractors);
        }
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetCategories_groups_counts_by_category()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 3, "medical");
        await SeedAsync(db, 2, "anatomy");

        var res = await svc.GetCategoriesAsync("oet", null, default);

        Assert.Equal(2, res.Categories.Count);
        Assert.Equal(3, res.Categories.First(c => c.Category == "medical").TermCount);
        Assert.Equal(2, res.Categories.First(c => c.Category == "anatomy").TermCount);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetStats_reports_zero_for_fresh_user()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 5);

        var stats = await svc.GetStatsAsync("fresh", default);

        Assert.Equal(0, stats.TotalInList);
        Assert.Equal(0, stats.Mastered);
        Assert.Equal(0, stats.DueToday);
        Assert.Equal(5, stats.TotalTermsInCatalog);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task LookupAsync_returns_exact_match_when_present()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 3);
        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "vt-unique", Term = "hypertension",
            Definition = "High BP.", ExampleSentence = "He has hypertension.",
            ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", Status = "active",
        });
        await db.SaveChangesAsync();

        var lookup = await svc.LookupAsync("Hypertension", "oet", default);

        Assert.True(lookup.Found);
        Assert.NotNull(lookup.Term);
        Assert.Equal("hypertension", lookup.Term!.Term);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task LookupAsync_returns_suggestions_for_near_match()
    {
        var (db, svc) = Build();
        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "vt-a", Term = "hypertension",
            Definition = "High BP.", ExampleSentence = "x.",
            ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", Status = "active",
        });
        db.VocabularyTerms.Add(new VocabularyTerm
        {
            Id = "vt-b", Term = "hypoxia",
            Definition = "Low O2.", ExampleSentence = "x.",
            ExamTypeCode = "oet", Category = "medical", Difficulty = "medium", Status = "active",
        });
        await db.SaveChangesAsync();

        var lookup = await svc.LookupAsync("hyp", "oet", default);

        Assert.False(lookup.Found);
        Assert.Equal(2, lookup.Suggestions.Count);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RemoveFromMyVocabulary_deletes_the_row()
    {
        var (db, svc) = Build();
        await SeedAsync(db, 1);
        await svc.AddToMyVocabularyAsync("u1", "vt-001", null, false, default);

        await svc.RemoveFromMyVocabularyAsync("u1", "vt-001", default);

        Assert.Equal(0, await db.LearnerVocabularies.CountAsync());
        await db.DisposeAsync();
    }
}
