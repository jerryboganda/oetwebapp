using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Manages AI-driven content generation jobs for admin CMS.
/// </summary>
public class ContentGenerationService(LearnerDbContext db)
{
    public async Task<object> QueueGenerationAsync(string adminUserId, ContentGenerationRequest request, CancellationToken ct)
    {
        var jobId = $"cg-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        var job = new ContentGenerationJob
        {
            Id = jobId,
            RequestedBy = adminUserId,
            ExamTypeCode = request.ExamTypeCode,
            SubtestCode = request.SubtestCode,
            TaskTypeId = request.TaskTypeId,
            ProfessionId = request.ProfessionId,
            Difficulty = request.Difficulty ?? "medium",
            RequestedCount = request.Count > 0 ? request.Count : 1,
            GeneratedCount = 0,
            PromptConfigJson = JsonSupport.Serialize(new
            {
                request.ExamTypeCode,
                request.SubtestCode,
                request.TaskTypeId,
                request.ProfessionId,
                request.Difficulty,
                request.CustomInstructions
            }),
            State = "pending",
            CreatedAt = now
        };
        db.ContentGenerationJobs.Add(job);

        // Queue background job
        var bgJob = new BackgroundJobItem
        {
            Id = $"bg-{Guid.NewGuid():N}",
            Type = JobType.ContentGeneration,
            ResourceId = jobId,
            State = AsyncState.Queued,
            AvailableAt = now,
            CreatedAt = now,
            LastTransitionAt = now,
            StatusReasonCode = "queued",
            StatusMessage = "Content generation job queued."
        };
        db.BackgroundJobs.Add(bgJob);

        await db.SaveChangesAsync(ct);

        return new
        {
            jobId,
            state = job.State,
            examTypeCode = job.ExamTypeCode,
            subtestCode = job.SubtestCode,
            requestedCount = job.RequestedCount,
            createdAt = job.CreatedAt
        };
    }

    public async Task<object> GetJobsAsync(int page, int pageSize, CancellationToken ct)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 20 : pageSize;

        var total = await db.ContentGenerationJobs.CountAsync(ct);
        var items = await db.ContentGenerationJobs
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new
        {
            total,
            page,
            pageSize,
            items = items.Select(j => new
            {
                jobId = j.Id,
                requestedBy = j.RequestedBy,
                examTypeCode = j.ExamTypeCode,
                subtestCode = j.SubtestCode,
                taskTypeId = j.TaskTypeId,
                professionId = j.ProfessionId,
                difficulty = j.Difficulty,
                requestedCount = j.RequestedCount,
                generatedCount = j.GeneratedCount,
                state = j.State,
                createdAt = j.CreatedAt,
                completedAt = j.CompletedAt
            })
        };
    }

    public async Task<object> GetJobDetailAsync(string jobId, CancellationToken ct)
    {
        var job = await db.ContentGenerationJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw ApiException.NotFound("JOB_NOT_FOUND", "Content generation job not found.");

        return new
        {
            jobId = job.Id,
            requestedBy = job.RequestedBy,
            examTypeCode = job.ExamTypeCode,
            subtestCode = job.SubtestCode,
            taskTypeId = job.TaskTypeId,
            professionId = job.ProfessionId,
            difficulty = job.Difficulty,
            requestedCount = job.RequestedCount,
            generatedCount = job.GeneratedCount,
            state = job.State,
            errorMessage = job.ErrorMessage,
            promptConfigJson = job.PromptConfigJson,
            generatedContentIdsJson = job.GeneratedContentIdsJson,
            createdAt = job.CreatedAt,
            completedAt = job.CompletedAt
        };
    }
}

public record ContentGenerationRequest(
    string ExamTypeCode,
    string SubtestCode,
    string? TaskTypeId,
    string? ProfessionId,
    string? Difficulty,
    int Count,
    string? CustomInstructions);
