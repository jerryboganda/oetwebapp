using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Speaking;

public interface ISpeakingDuplicateMarker
{
    Task<int> MarkDuplicatesAsync(CancellationToken ct);
}

/// <summary>
/// Keeps the earliest valid Speaking AI assessment as canonical and marks later
/// identical rows <c>IsDuplicate</c>. Never deletes evidence.
/// </summary>
public sealed class SpeakingDuplicateMarker(LearnerDbContext db) : ISpeakingDuplicateMarker
{
    public async Task<int> MarkDuplicatesAsync(CancellationToken ct)
    {
        var rows = await db.SpeakingAiAssessments
            .OrderBy(a => a.GeneratedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);

        var marked = 0;
        foreach (var group in rows.GroupBy(IdentityKey, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(group.Key) || group.Count() < 2)
                continue;

            var ordered = group.ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var shouldBeDuplicate = i > 0;
                if (ordered[i].IsDuplicate == shouldBeDuplicate)
                    continue;
                ordered[i].IsDuplicate = shouldBeDuplicate;
                marked++;
            }
        }

        if (marked > 0)
            await db.SaveChangesAsync(ct);

        return marked;
    }

    private static string IdentityKey(Domain.SpeakingAiAssessment row)
    {
        if (!string.IsNullOrWhiteSpace(row.IdentityHash))
            return "h:" + row.IdentityHash;
        return $"s:{row.SpeakingSessionId}|t:{row.TranscriptId}|p:{row.PromptTemplateId}";
    }
}
