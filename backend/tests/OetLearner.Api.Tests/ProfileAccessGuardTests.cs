using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class ProfileAccessGuardTests
{
    [Fact]
    public async Task LearnerService_GetMeAsync_DoesNotRecreateMissingProfile()
    {
        var options = CreateDbOptions();
        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var service = CreateLearnerService(db);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.GetMeAsync("learner-001", CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
        Assert.Contains("Learner profile not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Users);
        Assert.Empty(db.Goals);
        Assert.Empty(db.Settings);
        Assert.Empty(db.StudyPlans);
        Assert.Empty(db.ReadinessSnapshots);
    }

    [Fact]
    public async Task ExpertService_GetMeAsync_DoesNotRecreateMissingProfile()
    {
        var options = CreateDbOptions();
        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var service = CreateExpertService(db);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.GetMeAsync("expert-001", CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
        Assert.Contains("Expert profile not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.ExpertUsers);
    }

    private static DbContextOptions<LearnerDbContext> CreateDbOptions()
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static LearnerService CreateLearnerService(LearnerDbContext db)
    {
        var platformLinks = new PlatformLinkService(
            Options.Create(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            Options.Create(new BillingOptions()));

        var storageRoot = Path.Combine(Path.GetTempPath(), $"oet-profile-guards-{Guid.NewGuid():N}");
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = storageRoot });
        var mediaStorage = new MediaStorageService(new TestHostEnvironment(storageRoot), storageOptions);

        return new LearnerService(db, mediaStorage, platformLinks, null!);
    }

    private static ExpertService CreateExpertService(LearnerDbContext db)
    {
        var platformLinks = new PlatformLinkService(
            Options.Create(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            Options.Create(new BillingOptions()));

        var storageRoot = Path.Combine(Path.GetTempPath(), $"oet-profile-guards-{Guid.NewGuid():N}");
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = storageRoot });
        var mediaStorage = new MediaStorageService(new TestHostEnvironment(storageRoot), storageOptions);

        return new ExpertService(db, NullLogger<ExpertService>.Instance, mediaStorage, platformLinks, null!);
    }

    private sealed class TestHostEnvironment(string contentRootPath) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
