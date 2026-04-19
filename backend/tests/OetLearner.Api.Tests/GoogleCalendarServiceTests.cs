using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.StudyPlanner;

namespace OetLearner.Api.Tests;

public class GoogleCalendarServiceTests
{
    private static GoogleCalendarService Build(GoogleCalendarOptions opts, out LearnerDbContext db)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        db = new LearnerDbContext(options);
        var httpFactory = new NullHttpClientFactory();
        var dp = new EphemeralDataProtectionProvider();
        return new GoogleCalendarService(db, httpFactory, dp, Options.Create(opts));
    }

    [Fact]
    public void BuildAuthorizationUrl_includes_required_params()
    {
        var svc = Build(new GoogleCalendarOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "s",
            RedirectUri = "https://example.test/cb",
        }, out _);

        var url = svc.BuildAuthorizationUrl("state-abc");

        Assert.Contains("accounts.google.com/o/oauth2/v2/auth", url);
        Assert.Contains("client_id=test-client-id", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("access_type=offline", url);
        Assert.Contains("prompt=consent", url);
        Assert.Contains("state=state-abc", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Fexample.test%2Fcb", url);
        Assert.Contains("scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fcalendar", url);
    }

    [Fact]
    public void BuildAuthorizationUrl_throws_when_clientId_missing()
    {
        var svc = Build(new GoogleCalendarOptions { ClientId = "", ClientSecret = "s", RedirectUri = "x" }, out _);
        Assert.Throws<InvalidOperationException>(() => svc.BuildAuthorizationUrl("s"));
    }

    [Fact]
    public async Task GetLinkAsync_returns_null_when_not_connected()
    {
        var svc = Build(new GoogleCalendarOptions { ClientId = "x", ClientSecret = "y", RedirectUri = "z" }, out _);
        var link = await svc.GetLinkAsync("nobody", default);
        Assert.Null(link);
    }

    [Fact]
    public async Task DisconnectAsync_is_idempotent_when_no_link()
    {
        var svc = Build(new GoogleCalendarOptions { ClientId = "x", ClientSecret = "y", RedirectUri = "z" }, out _);
        // Should not throw even with no existing link.
        await svc.DisconnectAsync("nobody", default);
    }

    [Fact]
    public async Task DisconnectAsync_removes_existing_link()
    {
        var svc = Build(new GoogleCalendarOptions { ClientId = "x", ClientSecret = "y", RedirectUri = "z" }, out var db);
        db.LearnerCalendarLinks.Add(new LearnerCalendarLink
        {
            Id = "lcl-1",
            UserId = "u-1",
            Provider = "google",
            CalendarId = "primary",
            RefreshTokenEncrypted = "stub",
            TokenHint = "abcd",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await svc.DisconnectAsync("u-1", default);

        Assert.False(await db.LearnerCalendarLinks.AnyAsync(x => x.UserId == "u-1"));
    }

    [Fact]
    public async Task PushPlanAsync_returns_zero_when_not_connected()
    {
        var svc = Build(new GoogleCalendarOptions { ClientId = "x", ClientSecret = "y", RedirectUri = "z" }, out _);
        var pushed = await svc.PushPlanAsync("u-1", default);
        Assert.Equal(0, pushed);
    }

    [Fact]
    public async Task ExchangeCodeAsync_throws_when_client_not_configured()
    {
        var svc = Build(new GoogleCalendarOptions { ClientId = "", ClientSecret = "", RedirectUri = "" }, out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ExchangeCodeAsync("u-1", "code", default));
    }

    /// <summary>
    /// Minimal IHttpClientFactory that returns a default HttpClient so tests
    /// can exercise code paths that don't hit the network (everything we test
    /// here is either validation or DB-only).
    /// </summary>
    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
