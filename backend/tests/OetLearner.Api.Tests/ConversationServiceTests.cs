using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Conversation;

namespace OetLearner.Api.Tests;

public sealed class ConversationServiceTests
{
    [Fact]
    public async Task CreateSessionAsync_RejectsWhenNoPublishedTemplateOrSourceContentExists()
    {
        await using var db = NewDb();
        var options = new ConversationOptions { Enabled = true };
        var service = new ConversationService(
            db,
            Options.Create(options),
            new StubConversationOptionsProvider(options),
            new AllowAllConversationEntitlementService());

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CreateSessionAsync(
            "learner-1",
            new ConversationCreateSessionRequest(
                ContentId: null,
                ExamFamilyCode: null,
                TaskTypeCode: "oet-roleplay",
                Profession: "medicine",
                Difficulty: "medium"),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.Equal("CONVERSATION_TEMPLATE_REQUIRED", ex.ErrorCode);
        Assert.Empty(db.ConversationSessions);
    }

    private static LearnerDbContext NewDb() =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class StubConversationOptionsProvider(ConversationOptions options) : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default) => Task.FromResult(options);
        public void Invalidate() { }
    }

    private sealed class AllowAllConversationEntitlementService : IConversationEntitlementService
    {
        public Task<ConversationEntitlement> CheckAsync(string? userId, CancellationToken ct)
            => Task.FromResult(new ConversationEntitlement(true, "paid", int.MaxValue, int.MaxValue, 7, null, "Allowed."));
    }
}