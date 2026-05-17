using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

public sealed class ConversationOptionsProviderTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    public ConversationOptionsProviderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(options => options.UseSqlite(_connection));
        services.AddMemoryCache();
        services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();
        services.AddScoped<IAiProviderRegistry, AiProviderRegistry>();
        services.AddSingleton<IOptions<ConversationOptions>>(_ => Options.Create(new ConversationOptions
        {
            RealtimeSttEnabled = true,
            RealtimeAsrProvider = "elevenlabs-stt",
            RealtimeSttAllowRealProvider = true,
            RealtimeSttRealProviderProductionAuthorized = false,
            RealtimeSttEstimatedCostUsdPerMinute = 0.01m,
            RealtimeSttProviderSessionTopology = "single-instance",
            RealtimeSttRegionId = "test-region",
            RealtimeSttAssumeLearnersAdult = true,
        }));
        services.AddSingleton<IConversationOptionsProvider, ConversationOptionsProvider>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetAsync_AppliesProductionAuthorizationFromAdminSettingsRow()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.ConversationSettings.Add(new ConversationSettingsRow
            {
                Id = "default",
                RealtimeSttRealProviderProductionAuthorized = true,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var provider = _services.GetRequiredService<IConversationOptionsProvider>();
        var options = await provider.GetAsync();

        Assert.True(options.RealtimeSttRealProviderProductionAuthorized);
    }
}
