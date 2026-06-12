using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Jobs orphaned in Processing (container restart mid-job) must be recovered
/// by <see cref="BackgroundJobProcessor.RecoverStuckJobsAsync"/>: recent
/// orphans re-queue with a retry spent, retry-exhausted orphans fail with the
/// normal failure side effects, ancient orphans fail terminally without
/// blasting stale learner notifications, and live jobs are left alone.
/// </summary>
public class BackgroundJobStuckRecoveryTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BackgroundJobStuckRecoveryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RecentStuckJob_IsRequeuedWithBackoff()
    {
        var jobId = await SeedProcessingJobAsync(stuckFor: TimeSpan.FromMinutes(45), retryCount: 0);

        await RunRecoveryAsync();

        var job = await LoadJobAsync(jobId);
        Assert.Equal(AsyncState.Queued, job.State);
        Assert.Equal(1, job.RetryCount);
        Assert.Equal("stale_processing_requeued", job.StatusReasonCode);
        Assert.True(job.AvailableAt > DateTimeOffset.UtcNow, "requeued job should respect the retry backoff");
    }

    [Fact]
    public async Task StuckJobWithExhaustedRetries_IsFailed()
    {
        var jobId = await SeedProcessingJobAsync(stuckFor: TimeSpan.FromMinutes(45), retryCount: 2);

        await RunRecoveryAsync();

        var job = await LoadJobAsync(jobId);
        Assert.Equal(AsyncState.Failed, job.State);
        Assert.Equal(3, job.RetryCount);
        Assert.Equal("stale_processing", job.StatusReasonCode);
    }

    [Fact]
    public async Task AncientStuckJob_IsFailedWithoutRetry()
    {
        var jobId = await SeedProcessingJobAsync(stuckFor: TimeSpan.FromHours(48), retryCount: 0);

        await RunRecoveryAsync();

        var job = await LoadJobAsync(jobId);
        Assert.Equal(AsyncState.Failed, job.State);
        Assert.Equal(0, job.RetryCount);
        Assert.Equal("stale_processing_expired", job.StatusReasonCode);
    }

    [Fact]
    public async Task NonRetryableStuckJob_IsFailed()
    {
        var jobId = await SeedProcessingJobAsync(stuckFor: TimeSpan.FromMinutes(45), retryCount: 0, retryable: false);

        await RunRecoveryAsync();

        var job = await LoadJobAsync(jobId);
        Assert.Equal(AsyncState.Failed, job.State);
        Assert.Equal("stale_processing", job.StatusReasonCode);
    }

    [Fact]
    public async Task FreshProcessingJob_IsLeftAlone()
    {
        var jobId = await SeedProcessingJobAsync(stuckFor: TimeSpan.FromMinutes(5), retryCount: 0);

        await RunRecoveryAsync();

        var job = await LoadJobAsync(jobId);
        Assert.Equal(AsyncState.Processing, job.State);
        Assert.Equal(0, job.RetryCount);
    }

    private async Task RunRecoveryAsync()
    {
        var processor = _factory.Services.GetRequiredService<BackgroundJobProcessor>();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await processor.RecoverStuckJobsAsync(scope.ServiceProvider, db, CancellationToken.None);
    }

    private async Task<string> SeedProcessingJobAsync(TimeSpan stuckFor, int retryCount, bool retryable = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var job = new BackgroundJobItem
        {
            Id = $"job-stuck-{Guid.NewGuid():N}",
            Type = JobType.StudyPlanRegeneration,
            State = AsyncState.Processing,
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow - stuckFor,
            AvailableAt = DateTimeOffset.UtcNow - stuckFor,
            LastTransitionAt = DateTimeOffset.UtcNow - stuckFor,
            StatusReasonCode = "processing",
            StatusMessage = "Job is processing.",
            Retryable = retryable,
            RetryCount = retryCount,
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<BackgroundJobItem> LoadJobAsync(string jobId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        return await db.BackgroundJobs.AsNoTracking().FirstAsync(x => x.Id == jobId);
    }
}
