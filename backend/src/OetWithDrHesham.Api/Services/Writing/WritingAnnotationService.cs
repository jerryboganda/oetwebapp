using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

/// <summary>
/// Tutor span annotations over a submission's letter text (WS-B4). A tutor may create and list
/// annotations on submissions they are marking; only the authoring tutor may delete an annotation.
/// </summary>
public interface IWritingAnnotationService
{
    Task<IReadOnlyList<WritingFeedbackAnnotation>> ListAsync(Guid submissionId, CancellationToken ct);

    Task<WritingFeedbackAnnotation> CreateAsync(
        Guid submissionId,
        string tutorId,
        WritingAnnotationInput input,
        CancellationToken ct);

    /// <summary>Returns false when the annotation does not exist or is not owned by this tutor.</summary>
    Task<bool> DeleteAsync(Guid submissionId, Guid annotationId, string tutorId, CancellationToken ct);
}

public sealed record WritingAnnotationInput(
    string? Criterion,
    string HighlightedText,
    int StartOffset,
    int EndOffset,
    string Severity,
    string? Suggestion,
    string FeedbackText);

public sealed class WritingAnnotationService : IWritingAnnotationService
{
    private readonly LearnerDbContext _db;
    private readonly ILogger<WritingAnnotationService> _logger;

    public WritingAnnotationService(LearnerDbContext db, ILogger<WritingAnnotationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WritingFeedbackAnnotation>> ListAsync(Guid submissionId, CancellationToken ct)
    {
        return await _db.WritingFeedbackAnnotations
            .AsNoTracking()
            .Where(a => a.SubmissionId == submissionId)
            .OrderBy(a => a.StartOffset)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<WritingFeedbackAnnotation> CreateAsync(
        Guid submissionId,
        string tutorId,
        WritingAnnotationInput input,
        CancellationToken ct)
    {
        // Link to this tutor's review of the submission (if one exists) so annotations and the
        // review stay associated; the review row is created on submit.
        var review = await _db.WritingTutorReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.SubmissionId == submissionId && r.TutorId == tutorId, ct);

        var annotation = new WritingFeedbackAnnotation
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            ReviewId = review?.Id,
            TutorId = tutorId,
            Criterion = string.IsNullOrWhiteSpace(input.Criterion) ? null : input.Criterion,
            HighlightedText = input.HighlightedText,
            StartOffset = input.StartOffset,
            EndOffset = input.EndOffset,
            Severity = string.IsNullOrWhiteSpace(input.Severity) ? "medium" : input.Severity,
            Suggestion = string.IsNullOrWhiteSpace(input.Suggestion) ? null : input.Suggestion,
            FeedbackText = input.FeedbackText,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.WritingFeedbackAnnotations.Add(annotation);
        await _db.SaveChangesAsync(ct);
        return annotation;
    }

    public async Task<bool> DeleteAsync(Guid submissionId, Guid annotationId, string tutorId, CancellationToken ct)
    {
        var annotation = await _db.WritingFeedbackAnnotations
            .FirstOrDefaultAsync(
                a => a.Id == annotationId && a.SubmissionId == submissionId && a.TutorId == tutorId,
                ct);
        if (annotation is null)
        {
            return false;
        }

        _db.WritingFeedbackAnnotations.Remove(annotation);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
