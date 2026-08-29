using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Tests.Services;

public sealed class AiRawResponseRetentionTests
{
    [Fact]
    public async Task Purge_clears_ciphertext_and_keeps_hash()
    {
        var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var store = new AiRawResponseStore(db, new EphemeralDataProtectionProvider());
        await store.CaptureAsync("reading.explanation.v1", "op1", "anthropic", "RAW_BODY_SECRET", default);

        var row = Assert.Single(db.AiRawResponses);
        row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var purged = await store.PurgeExpiredAsync(50, default);
        Assert.Equal(1, purged);
        Assert.Null(row.PayloadCiphertext);
        Assert.NotNull(row.PurgedAt);
        Assert.False(string.IsNullOrWhiteSpace(row.PayloadSha256));
        Assert.Equal(1, await db.AiRawResponses.CountAsync());
    }
}
