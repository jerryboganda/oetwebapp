using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class ExpertOnboardingServiceTests
{
    private static (LearnerDbContext db, ExpertOnboardingService svc) Build(bool seedExpert = true, bool active = true)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        if (seedExpert)
        {
            db.ExpertUsers.Add(new ExpertUser
            {
                Id = "exp-1",
                DisplayName = "Dr Test",
                Email = "exp@test.local",
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = active,
            });
            db.SaveChanges();
        }
        return (db, new ExpertOnboardingService(db));
    }

    private static ExpertOnboardingProfileDto ValidProfile() =>
        new(DisplayName: "Dr Smith", Bio: "10 years OET coaching.", PhotoUrl: null);

    private static ExpertOnboardingQualificationsDto ValidQuals() =>
        new(Qualifications: "MBBS, MRCGP", Certifications: "OET Examiner", ExperienceYears: 10);

    private static ExpertOnboardingRatesDto ValidRates() =>
        new(HourlyRateMinorUnits: 5000, SessionRateMinorUnits: 12500, Currency: "GBP");

    // ── EnsureExpertAsync gate ─────────────────────────────────────────────

    [Fact]
    public async Task GetStatusAsync_throws_403_when_expert_missing()
    {
        var (db, svc) = Build(seedExpert: false);
        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.GetStatusAsync("exp-1", default));
        Assert.Equal("expert_profile_not_found", ex.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveProfileAsync_throws_403_when_expert_inactive()
    {
        var (db, svc) = Build(active: false);
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveProfileAsync("exp-1", ValidProfile(), default));
        Assert.Equal("expert_profile_not_found", ex.ErrorCode);
        await db.DisposeAsync();
    }

    // ── GetStatusAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatusAsync_returns_empty_status_when_no_progress_row()
    {
        var (db, svc) = Build();
        var s = await svc.GetStatusAsync("exp-1", default);
        Assert.False(s.IsComplete);
        Assert.Empty(s.CompletedSteps);
        Assert.Null(s.Profile);
        Assert.Null(s.Qualifications);
        Assert.Null(s.Rates);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetStatusAsync_rehydrates_saved_state_after_each_step()
    {
        var (db, svc) = Build();
        await svc.SaveProfileAsync("exp-1", ValidProfile(), default);
        await svc.SaveQualificationsAsync("exp-1", ValidQuals(), default);
        await svc.SaveRatesAsync("exp-1", ValidRates(), default);

        var s = await svc.GetStatusAsync("exp-1", default);
        Assert.False(s.IsComplete);
        Assert.NotNull(s.Profile);
        Assert.NotNull(s.Qualifications);
        Assert.NotNull(s.Rates);
        Assert.Contains("profile", s.CompletedSteps);
        Assert.Contains("qualifications", s.CompletedSteps);
        Assert.Contains("rates", s.CompletedSteps);
        await db.DisposeAsync();
    }

    // ── Step ordering / idempotency ────────────────────────────────────────

    [Fact]
    public async Task SaveProfileAsync_appends_step_only_once_when_called_repeatedly()
    {
        var (db, svc) = Build();
        await svc.SaveProfileAsync("exp-1", ValidProfile(), default);
        await svc.SaveProfileAsync("exp-1", ValidProfile() with { Bio = "Updated bio." }, default);

        var s = await svc.GetStatusAsync("exp-1", default);
        Assert.Single(s.CompletedSteps, "profile");
        Assert.Equal("Updated bio.", s.Profile!.Bio);
        await db.DisposeAsync();
    }

    // ── Profile validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData("", "valid bio")]
    [InlineData("   ", "valid bio")]
    public async Task SaveProfileAsync_rejects_blank_display_name(string name, string bio)
    {
        var (db, svc) = Build();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveProfileAsync("exp-1", new ExpertOnboardingProfileDto(name, bio, null), default));
        Assert.Equal("invalid_display_name", ex.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveProfileAsync_rejects_oversized_display_name()
    {
        var (db, svc) = Build();
        var name = new string('x', 129);
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveProfileAsync("exp-1", new ExpertOnboardingProfileDto(name, "bio", null), default));
        Assert.Equal("invalid_display_name", ex.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveProfileAsync_rejects_blank_or_oversized_bio()
    {
        var (db, svc) = Build();
        await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveProfileAsync("exp-1", new ExpertOnboardingProfileDto("Name", "", null), default));
        await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveProfileAsync("exp-1", new ExpertOnboardingProfileDto("Name", new string('b', 2001), null), default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveProfileAsync_rejects_oversized_photo_url()
    {
        var (db, svc) = Build();
        var url = "https://example.com/" + new string('a', 1100);
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveProfileAsync("exp-1", new ExpertOnboardingProfileDto("Name", "Bio", url), default));
        Assert.Equal("invalid_photo_url", ex.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveProfileAsync_accepts_null_or_empty_photo_url()
    {
        var (db, svc) = Build();
        await svc.SaveProfileAsync("exp-1", new ExpertOnboardingProfileDto("Name", "Bio", null), default);
        await svc.SaveProfileAsync("exp-1", new ExpertOnboardingProfileDto("Name", "Bio", ""), default);
        var s = await svc.GetStatusAsync("exp-1", default);
        Assert.NotNull(s.Profile);
        await db.DisposeAsync();
    }

    // ── Qualifications validation ──────────────────────────────────────────

    [Fact]
    public async Task SaveQualificationsAsync_rejects_blank_qualifications()
    {
        var (db, svc) = Build();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveQualificationsAsync("exp-1", new ExpertOnboardingQualificationsDto("  ", "C", 5), default));
        Assert.Equal("invalid_qualifications", ex.ErrorCode);
        await db.DisposeAsync();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(71)]
    public async Task SaveQualificationsAsync_rejects_out_of_range_experience_years(int years)
    {
        var (db, svc) = Build();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveQualificationsAsync("exp-1", new ExpertOnboardingQualificationsDto("MBBS", "", years), default));
        Assert.Equal("invalid_experience_years", ex.ErrorCode);
        await db.DisposeAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70)]
    public async Task SaveQualificationsAsync_accepts_boundary_experience_years(int years)
    {
        var (db, svc) = Build();
        await svc.SaveQualificationsAsync("exp-1", new ExpertOnboardingQualificationsDto("MBBS", "", years), default);
        var s = await svc.GetStatusAsync("exp-1", default);
        Assert.Equal(years, s.Qualifications!.ExperienceYears);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveQualificationsAsync_rejects_oversized_certifications()
    {
        var (db, svc) = Build();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveQualificationsAsync("exp-1",
                new ExpertOnboardingQualificationsDto("MBBS", new string('c', 4001), 5), default));
        Assert.Equal("invalid_certifications", ex.ErrorCode);
        await db.DisposeAsync();
    }

    // ── Rates validation ───────────────────────────────────────────────────

    [Theory]
    [InlineData("XYZ")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SaveRatesAsync_rejects_invalid_currency(string currency)
    {
        var (db, svc) = Build();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveRatesAsync("exp-1", new ExpertOnboardingRatesDto(1000, 5000, currency), default));
        Assert.Equal("invalid_currency", ex.ErrorCode);
        await db.DisposeAsync();
    }

    [Theory]
    [InlineData("gbp")]
    [InlineData("Usd")]
    [InlineData("EUR")]
    [InlineData("aud")]
    public async Task SaveRatesAsync_normalises_currency_to_uppercase(string currency)
    {
        var (db, svc) = Build();
        var saved = await svc.SaveRatesAsync("exp-1",
            new ExpertOnboardingRatesDto(1000, 5000, currency), default);
        Assert.Equal(currency.ToUpperInvariant(), saved.Currency);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task SaveRatesAsync_rejects_negative_or_oversized_rates()
    {
        var (db, svc) = Build();
        await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveRatesAsync("exp-1", new ExpertOnboardingRatesDto(-1, 100, "GBP"), default));
        await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveRatesAsync("exp-1", new ExpertOnboardingRatesDto(100, -1, "GBP"), default));
        await Assert.ThrowsAsync<ApiException>(
            () => svc.SaveRatesAsync("exp-1", new ExpertOnboardingRatesDto(100_000_001_00, 100, "GBP"), default));
        await db.DisposeAsync();
    }

    // ── CompleteAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_throws_when_any_step_missing()
    {
        // None saved
        var (db1, svc1) = Build();
        var ex = await Assert.ThrowsAsync<ApiException>(() => svc1.CompleteAsync("exp-1", default));
        Assert.Equal("onboarding_incomplete", ex.ErrorCode);
        await db1.DisposeAsync();

        // Only profile saved
        var (db2, svc2) = Build();
        await svc2.SaveProfileAsync("exp-1", ValidProfile(), default);
        ex = await Assert.ThrowsAsync<ApiException>(() => svc2.CompleteAsync("exp-1", default));
        Assert.Equal("onboarding_incomplete", ex.ErrorCode);
        await db2.DisposeAsync();

        // Profile + qualifications saved (rates missing)
        var (db3, svc3) = Build();
        await svc3.SaveProfileAsync("exp-1", ValidProfile(), default);
        await svc3.SaveQualificationsAsync("exp-1", ValidQuals(), default);
        ex = await Assert.ThrowsAsync<ApiException>(() => svc3.CompleteAsync("exp-1", default));
        Assert.Equal("onboarding_incomplete", ex.ErrorCode);
        await db3.DisposeAsync();
    }

    [Fact]
    public async Task CompleteAsync_finalises_when_all_three_steps_saved()
    {
        var (db, svc) = Build();
        await svc.SaveProfileAsync("exp-1", ValidProfile(), default);
        await svc.SaveQualificationsAsync("exp-1", ValidQuals(), default);
        await svc.SaveRatesAsync("exp-1", ValidRates(), default);

        var resp = await svc.CompleteAsync("exp-1", default);
        Assert.True(resp.Completed);

        var s = await svc.GetStatusAsync("exp-1", default);
        Assert.True(s.IsComplete);
        Assert.Contains("review", s.CompletedSteps);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task CompleteAsync_is_idempotent_and_does_not_overwrite_completed_at()
    {
        var (db, svc) = Build();
        await svc.SaveProfileAsync("exp-1", ValidProfile(), default);
        await svc.SaveQualificationsAsync("exp-1", ValidQuals(), default);
        await svc.SaveRatesAsync("exp-1", ValidRates(), default);

        await svc.CompleteAsync("exp-1", default);
        var first = await db.Set<ExpertOnboardingProgress>().AsNoTracking()
            .FirstAsync(p => p.ExpertUserId == "exp-1");
        var firstAt = first.CompletedAt;

        await svc.CompleteAsync("exp-1", default);
        var second = await db.Set<ExpertOnboardingProgress>().AsNoTracking()
            .FirstAsync(p => p.ExpertUserId == "exp-1");

        Assert.Equal(firstAt, second.CompletedAt);
        Assert.True(second.IsComplete);
        await db.DisposeAsync();
    }
}
