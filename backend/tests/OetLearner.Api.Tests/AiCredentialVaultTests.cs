using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;

namespace OetLearner.Api.Tests;

public class AiCredentialVaultTests
{
    private static (LearnerDbContext db, AiCredentialVault vault) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var dpProvider = DataProtectionProvider.Create(nameof(AiCredentialVaultTests));
        var httpFactory = new StubHttpClientFactory();
        var vault = new AiCredentialVault(db, dpProvider, httpFactory, NullLogger<AiCredentialVault>.Instance);
        return (db, vault);
    }

    [Fact]
    public async Task Upsert_EncryptsKey_AndReturnsHintOnly()
    {
        var (db, vault) = Build();
        var result = await vault.UpsertAsync(
            userId: "u1", authAccountId: null,
            providerCode: "openai-platform",
            plaintextKey: "sk-thisisasupersecrettestkey-abcdef",
            modelAllowlistCsv: null,
            skipValidation: true,
            ct: default);

        Assert.True(result.Success);
        Assert.Equal("…cdef", result.KeyHint);
        var row = await db.UserAiCredentials.SingleAsync();
        Assert.NotEqual("sk-thisisasupersecrettestkey-abcdef", row.EncryptedKey);
        Assert.Contains("…cdef", row.KeyHint);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Upsert_RejectsTooShortKey()
    {
        var (db, vault) = Build();
        var result = await vault.UpsertAsync("u1", null, "openai-platform", "short", null, true, default);
        Assert.False(result.Success);
        Assert.Equal("key_too_short", result.ErrorCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ResolvePlaintext_ReturnsOriginalKey()
    {
        var (db, vault) = Build();
        const string original = "sk-thisisasupersecrettestkey-abcdef";
        await vault.UpsertAsync("u1", null, "openai-platform", original, null, true, default);
        var plaintext = await vault.ResolvePlaintextAsync("u1", "openai-platform", default);
        Assert.Equal(original, plaintext);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ResolvePlaintext_ReturnsNullAfterRevoke()
    {
        var (db, vault) = Build();
        var saved = await vault.UpsertAsync("u1", null, "openai-platform", "sk-abcdef0123456789-xyz", null, true, default);
        await vault.RevokeAsync("u1", saved.CredentialId!, default);
        var plaintext = await vault.ResolvePlaintextAsync("u1", "openai-platform", default);
        Assert.Null(plaintext);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task ResolvePlaintext_RespectsCooldown()
    {
        var (db, vault) = Build();
        var saved = await vault.UpsertAsync("u1", null, "openai-platform", "sk-abcdef0123456789-xyz", null, true, default);
        await vault.MarkInvalidAsync(saved.CredentialId!, "401", TimeSpan.FromHours(24), default);
        var plaintext = await vault.ResolvePlaintextAsync("u1", "openai-platform", default);
        Assert.Null(plaintext);
        var row = await db.UserAiCredentials.FirstAsync();
        Assert.Equal(AiCredentialStatus.Invalid, row.Status);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Upsert_ReplacesExisting_ForSameProvider()
    {
        var (db, vault) = Build();
        await vault.UpsertAsync("u1", null, "openai-platform", "sk-firstkey-abcdef-123456", null, true, default);
        await vault.UpsertAsync("u1", null, "openai-platform", "sk-secondkey-xyz-7890abcd", null, true, default);
        var rows = await db.UserAiCredentials.ToListAsync();
        Assert.Single(rows);
        Assert.Equal("…abcd", rows[0].KeyHint);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task List_ReturnsHintNotPlaintext()
    {
        var (db, vault) = Build();
        await vault.UpsertAsync("u1", null, "openai-platform", "sk-abcdef0123456789-xyz", null, true, default);
        var rows = await vault.ListAsync("u1", default);
        Assert.Single(rows);
        var hint = rows[0].KeyHint;
        Assert.DoesNotContain("sk-", hint);
        Assert.StartsWith("…", hint);
        await db.DisposeAsync();
    }

    /// <summary>Stub HttpClient that always 200s — we test the encryption +
    /// storage logic, not the provider-specific ping shape.</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StubHandler());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
