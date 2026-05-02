using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// AI Writing Coach — provides real-time suggestions during writing practice.
/// Mock implementation returns simulated suggestions.
/// Production: swap for OpenAI/Gemini API with writing-specific prompts.
/// </summary>
public class WritingCoachService(LearnerDbContext db)
{
    private static readonly (string type, string original, string suggested, string explanation)[] SuggestionTemplates = new[]
    {
        ("grammar", "The patient have been", "The patient has been", "Subject-verb agreement: singular subject 'patient' requires 'has'."),
        ("vocabulary", "very important information", "critical clinical information", "Use precise clinical terminology for a professional tone."),
        ("structure", "I am writing to tell you about", "I am writing to inform you regarding", "More formal register suits the OET referral letter genre."),
        ("conciseness", "Due to the fact that the patient experienced", "Because the patient experienced", "Reduce wordiness for conciseness marks."),
        ("tone", "You should make sure to", "Please ensure that", "Softer directive phrasing is more appropriate for professional correspondence."),
        ("grammar", "The medications was changed", "The medications were changed", "Subject-verb agreement: plural 'medications' requires 'were'."),
        ("vocabulary", "got better", "improved", "Use formal medical language instead of colloquial expressions."),
        ("structure", "Also I wanted to mention", "Additionally, I would like to highlight", "Use formal transition phrases to improve organisation marks."),
        ("format", "mr wheeler", "Mr Wheeler", "Proper nouns and titles should be capitalised."),
        ("conciseness", "at this point in time", "currently", "Replace wordy phrases with concise alternatives.")
    };

    public async Task<object> CheckTextAsync(string userId, string attemptId, WritingCoachCheckRequest request, CancellationToken ct)
    {
        // Ensure or create coach session
        var session = await db.WritingCoachSessions
            .FirstOrDefaultAsync(s => s.AttemptId == attemptId && s.UserId == userId, ct);

        if (session == null)
        {
            session = new WritingCoachSession
            {
                Id = $"wcs-{Guid.NewGuid():N}",
                AttemptId = attemptId,
                UserId = userId,
                SuggestionsGenerated = 0,
                SuggestionsAccepted = 0,
                SuggestionsDismissed = 0,
                StartedAt = DateTimeOffset.UtcNow
            };
            db.WritingCoachSessions.Add(session);
        }

        var text = request.CurrentText ?? "";
        var suggestions = new List<object>();

        // Generate suggestions based on text content
        foreach (var template in SuggestionTemplates)
        {
            if (text.Contains(template.original, StringComparison.OrdinalIgnoreCase))
            {
                var startOffset = text.IndexOf(template.original, StringComparison.OrdinalIgnoreCase);
                var suggestion = new WritingCoachSuggestion
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    AttemptId = attemptId,
                    SuggestionType = template.type,
                    OriginalText = template.original,
                    SuggestedText = template.suggested,
                    Explanation = template.explanation,
                    StartOffset = startOffset,
                    EndOffset = startOffset + template.original.Length,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.WritingCoachSuggestions.Add(suggestion);
                session.SuggestionsGenerated++;

                suggestions.Add(new
                {
                    id = suggestion.Id,
                    type = suggestion.SuggestionType,
                    originalText = suggestion.OriginalText,
                    suggestedText = suggestion.SuggestedText,
                    explanation = suggestion.Explanation,
                    startOffset = suggestion.StartOffset,
                    endOffset = suggestion.EndOffset,
                    resolution = suggestion.Resolution
                });
            }
        }

        // Per Writing Module Technical Specification v1.0, the coach must not
        // generate generic suggestions from response length or word count.

        await db.SaveChangesAsync(ct);

        return new
        {
            sessionId = session.Id,
            suggestions,
            stats = new
            {
                totalGenerated = session.SuggestionsGenerated,
                accepted = session.SuggestionsAccepted,
                dismissed = session.SuggestionsDismissed,
                pending = session.SuggestionsGenerated - session.SuggestionsAccepted - session.SuggestionsDismissed
            }
        };
    }

    public async Task<object> ResolveSuggestionAsync(string userId, Guid suggestionId, string resolution, CancellationToken ct)
    {
        if (resolution is not ("accepted" or "dismissed"))
            throw ApiException.Validation("INVALID_RESOLUTION", "Resolution must be 'accepted' or 'dismissed'.");

        var suggestion = await db.WritingCoachSuggestions
            .FirstOrDefaultAsync(s => s.Id == suggestionId, ct)
            ?? throw ApiException.NotFound("SUGGESTION_NOT_FOUND", "Coach suggestion not found.");

        // Verify ownership via session
        var session = await db.WritingCoachSessions
            .FirstOrDefaultAsync(s => s.Id == suggestion.SessionId && s.UserId == userId, ct)
            ?? throw ApiException.NotFound("SESSION_NOT_FOUND", "Coach session not found.");

        if (suggestion.Resolution != null)
            throw ApiException.Validation("ALREADY_RESOLVED", "Suggestion has already been resolved.");

        suggestion.Resolution = resolution;
        if (resolution == "accepted") session.SuggestionsAccepted++;
        else session.SuggestionsDismissed++;

        await db.SaveChangesAsync(ct);

        return new
        {
            id = suggestion.Id,
            resolution = suggestion.Resolution,
            stats = new
            {
                totalGenerated = session.SuggestionsGenerated,
                accepted = session.SuggestionsAccepted,
                dismissed = session.SuggestionsDismissed
            }
        };
    }

    public async Task<object> GetStatsAsync(string userId, string attemptId, CancellationToken ct)
    {
        var session = await db.WritingCoachSessions
            .FirstOrDefaultAsync(s => s.AttemptId == attemptId && s.UserId == userId, ct);

        if (session == null)
        {
            return new
            {
                active = false,
                totalGenerated = 0,
                accepted = 0,
                dismissed = 0,
                pending = 0,
                acceptanceRate = 0.0,
                suggestionBreakdown = Array.Empty<object>()
            };
        }

        var suggestions = await db.WritingCoachSuggestions
            .Where(s => s.SessionId == session.Id)
            .ToListAsync(ct);

        var breakdown = suggestions
            .GroupBy(s => s.SuggestionType)
            .Select(g => new
            {
                type = g.Key,
                total = g.Count(),
                accepted = g.Count(s => s.Resolution == "accepted"),
                dismissed = g.Count(s => s.Resolution == "dismissed"),
                pending = g.Count(s => s.Resolution == null)
            })
            .ToList();

        return new
        {
            active = true,
            totalGenerated = session.SuggestionsGenerated,
            accepted = session.SuggestionsAccepted,
            dismissed = session.SuggestionsDismissed,
            pending = session.SuggestionsGenerated - session.SuggestionsAccepted - session.SuggestionsDismissed,
            acceptanceRate = session.SuggestionsGenerated > 0
                ? Math.Round(100.0 * session.SuggestionsAccepted / session.SuggestionsGenerated, 1)
                : 0.0,
            suggestionBreakdown = breakdown
        };
    }
}

public record WritingCoachCheckRequest(
    string CurrentText,
    int? CursorPosition);
