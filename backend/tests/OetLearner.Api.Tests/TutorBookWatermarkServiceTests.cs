using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OetLearner.Api.Services;
using OetLearner.Api.Services.TutorBook;

namespace OetLearner.Api.Tests;

public sealed class TutorBookWatermarkServiceTests
{
    // The key every production PDF was signed with before TutorBook:SignatureSecret
    // existed. Hard-coded here on purpose: if it ever changes, those copies stop
    // being traceable and this suite must fail.
    private const string LegacyKey = "dev-tutor-book-signing-key";
    private const string Secret = "configured-tutor-book-secret-0f3a9c1d7e5b2a4c";
    private const string Email = "Buyer@Example.com";

    [Fact]
    public void ComputeBuyerSignature_UsesConfiguredSecret()
    {
        var signature = Service("Production", Secret).ComputeBuyerSignature(" buyer@example.COM ");

        Assert.Equal(ExpectedSignature(Secret, Email), signature);
        Assert.NotEqual(ExpectedSignature(LegacyKey, Email), signature);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(LegacyKey)]
    public async Task Download_InProductionWithoutSecret_FailsClosed(string? secret)
    {
        var service = Service("Production", secret);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.GetWatermarkedAsync("Buyer", Email, DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ex.StatusCode);
        Assert.Equal("tutor_book_signing_unconfigured", ex.ErrorCode);
        Assert.False(ex.Retryable);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void ComputeBuyerSignature_OutsideProductionWithoutSecret_UsesLegacyKey(string environment)
    {
        Assert.Equal(ExpectedSignature(LegacyKey, Email), Service(environment, null).ComputeBuyerSignature(Email));
    }

    [Fact]
    public void VerifyBuyerSignature_AcceptsConfiguredAndLegacyKeys_RejectsTampered()
    {
        var service = Service("Production", Secret);
        var configured = ExpectedSignature(Secret, Email);
        var legacy = ExpectedSignature(LegacyKey, Email);

        Assert.Equal("configured", service.VerifyBuyerSignature(Email, configured));
        Assert.Equal("configured", service.VerifyBuyerSignature(Email, $" {configured.ToLowerInvariant()} "));
        Assert.Equal("legacy", service.VerifyBuyerSignature(Email, legacy));

        var tampered = (configured[0] == 'A' ? "B" : "A") + configured[1..];
        Assert.Null(service.VerifyBuyerSignature(Email, tampered));
        Assert.Null(service.VerifyBuyerSignature(Email, configured[..^1]));
        Assert.Null(service.VerifyBuyerSignature("someone-else@example.com", configured));
        Assert.Null(service.VerifyBuyerSignature(Email, ""));

        // Tracing an already-issued PDF must not depend on the signing secret being set.
        Assert.Equal("legacy", Service("Production", null).VerifyBuyerSignature(Email, legacy));
    }

    private static string ExpectedSignature(string key, string email)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash, 0, 8);
    }

    private static TutorBookWatermarkService Service(string environment, string? secret)
        => new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["TutorBook:SignatureSecret"] = secret })
                .Build(),
            new TestHostEnvironment(environment));

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
