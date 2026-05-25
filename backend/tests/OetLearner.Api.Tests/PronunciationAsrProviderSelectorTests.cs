using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Pronunciation;

namespace OetLearner.Api.Tests;

/// <summary>
/// Runtime ASR provider selection invariants.
/// </summary>
public class PronunciationAsrProviderSelectorTests
{
    private static PronunciationAsrProviderSelector Build(
        string providerPreference,
        params IPronunciationAsrProvider[] providers)
        => BuildWithResolver(providerPreference, new FakeCredentialResolver(), providers);

    private static PronunciationAsrProviderSelector BuildWithResolver(
        string providerPreference,
        IPronunciationCredentialResolver credentialResolver,
        IPronunciationAsrProvider[] providers,
        string environmentName = "Development")
    {
        var opts = Options.Create(new PronunciationOptions { Provider = providerPreference });
        return new PronunciationAsrProviderSelector(
            providers,
            opts,
            credentialResolver,
            new TestWebHostEnvironment(environmentName),
            NullLogger<PronunciationAsrProviderSelector>.Instance);
    }

    private sealed class FakeProvider(string name, bool configured) : IPronunciationAsrProvider
    {
        public string Name { get; } = name;
        public bool IsConfigured { get; } = configured;
        public Task<AsrResult> AnalyzeAsync(AsrRequest request, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task Auto_Prefers_Azure_When_Configured()
    {
        var sel = Build("auto",
            new FakeProvider("azure", configured: true),
            new FakeProvider("gemini", configured: true),
            new FakeProvider("whisper", configured: true),
            new FakeProvider("mock", configured: true));
        Assert.Equal("azure", (await sel.SelectAsync(default)).Name);
    }

    [Fact]
    public async Task Auto_Falls_Back_To_Gemini_When_Azure_Missing()
    {
        var sel = Build("auto",
            new FakeProvider("azure", configured: false),
            new FakeProvider("gemini", configured: true),
            new FakeProvider("whisper", configured: true),
            new FakeProvider("mock", configured: true));
        Assert.Equal("gemini", (await sel.SelectAsync(default)).Name);
    }

    [Fact]
    public async Task Auto_Falls_Back_To_Whisper_When_Azure_Missing()
    {
        var sel = Build("auto",
            new FakeProvider("azure", configured: false),
            new FakeProvider("gemini", configured: false),
            new FakeProvider("whisper", configured: true),
            new FakeProvider("mock", configured: true));
        Assert.Equal("whisper", (await sel.SelectAsync(default)).Name);
    }

    [Fact]
    public async Task Auto_Falls_Back_To_Mock_When_No_Real_Provider_Configured()
    {
        var sel = Build("auto",
            new FakeProvider("azure", configured: false),
            new FakeProvider("gemini", configured: false),
            new FakeProvider("whisper", configured: false),
            new FakeProvider("mock", configured: true));
        Assert.Equal("mock", (await sel.SelectAsync(default)).Name);
    }

    [Fact]
    public async Task Auto_Throws_In_Production_When_No_Real_Provider_Configured()
    {
        var sel = BuildWithResolver(
            "auto",
            new FakeCredentialResolver(),
            [
                new FakeProvider("azure", configured: false),
                new FakeProvider("gemini", configured: false),
                new FakeProvider("whisper", configured: false),
                new FakeProvider("mock", configured: true)
            ],
            Environments.Production);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sel.SelectAsync(default));
        Assert.Contains("provider 'auto' is not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Explicit_Azure_Throws_When_Unconfigured()
    {
        var sel = Build("azure",
            new FakeProvider("azure", configured: false),
            new FakeProvider("mock", configured: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sel.SelectAsync(default));
    }

    [Fact]
    public async Task Explicit_Whisper_Throws_When_Unconfigured()
    {
        var sel = Build("whisper",
            new FakeProvider("whisper", configured: false),
            new FakeProvider("mock", configured: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sel.SelectAsync(default));
    }

    [Fact]
    public async Task Explicit_Gemini_Returns_Gemini_When_Configured()
    {
        var sel = Build("gemini",
            new FakeProvider("gemini", configured: true),
            new FakeProvider("mock", configured: true));
        Assert.Equal("gemini", (await sel.SelectAsync(default)).Name);
    }

    [Fact]
    public async Task Explicit_Gemini_Throws_When_Unconfigured()
    {
        var sel = Build("gemini",
            new FakeProvider("gemini", configured: false),
            new FakeProvider("mock", configured: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sel.SelectAsync(default));
    }

    [Fact]
    public async Task Unknown_Provider_Value_Throws_Instead_Of_Falling_Back_To_Auto()
    {
        var sel = Build("made-up-provider",
            new FakeProvider("azure", configured: true),
            new FakeProvider("mock", configured: true));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sel.SelectAsync(default));
        Assert.Contains("Unsupported Pronunciation:Provider value", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_Gemini_Warms_Registry_Credentials_Before_Configured_Check()
    {
        var resolver = new FakeCredentialResolver((
            "gemini-pronunciation-audio",
            new PronunciationCredentials(
                ApiKey: "gemini-key",
                BaseUrl: "https://generativelanguage.googleapis.com/v1beta",
                DefaultModel: "gemini-3.5-flash",
                AzureRegion: null)));
        var sel = BuildWithResolver("gemini", resolver,
            [
                new ResolverBackedProvider("gemini", "gemini-pronunciation-audio", resolver),
                new FakeProvider("mock", configured: true)
            ]);

        Assert.False(resolver.IsRegistryConfigured("gemini-pronunciation-audio"));
        Assert.Equal("gemini", (await sel.SelectAsync(default)).Name);
        Assert.True(resolver.IsRegistryConfigured("gemini-pronunciation-audio"));
    }

    [Fact]
    public async Task Warmup_Resolves_All_Pronunciation_Registry_Codes()
    {
        var resolver = new FakeCredentialResolver();
        var sel = BuildWithResolver("auto", resolver,
            [
                new FakeProvider("azure", configured: false),
                new FakeProvider("gemini", configured: false),
                new FakeProvider("whisper", configured: false),
                new FakeProvider("mock", configured: true)
            ]);

        await sel.SelectAsync(default);

        Assert.Contains("azure-phoneme", resolver.ResolvedCodes);
        Assert.Contains("gemini-pronunciation-audio", resolver.ResolvedCodes);
        Assert.Contains("whisper-asr", resolver.ResolvedCodes);
    }

    [Fact]
    public async Task Warmup_Propagates_Cancellation()
    {
        var resolver = new FakeCredentialResolver();
        var sel = BuildWithResolver("auto", resolver,
            [
                new FakeProvider("mock", configured: true)
            ]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => sel.SelectAsync(cts.Token));
    }

    [Fact]
    public async Task Explicit_Mock_Returns_Mock_Always()
    {
        var sel = Build("mock",
            new FakeProvider("azure", configured: true),
            new FakeProvider("mock", configured: true));
        Assert.Equal("mock", (await sel.SelectAsync(default)).Name);
    }

    [Fact]
    public async Task Explicit_Mock_Throws_In_Production()
    {
        var sel = BuildWithResolver(
            "mock",
            new FakeCredentialResolver(),
            [
                new FakeProvider("mock", configured: true)
            ],
            Environments.Production);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sel.SelectAsync(default));
        Assert.Contains("forbidden in production", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeCredentialResolver : IPronunciationCredentialResolver
    {
        private readonly Dictionary<string, PronunciationCredentials> _rows;
        private readonly HashSet<string> _warmedCodes = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyCollection<string> ResolvedCodes => _warmedCodes;

        public FakeCredentialResolver(params (string Code, PronunciationCredentials Credentials)[] rows)
        {
            _rows = rows.ToDictionary(
                row => row.Code,
                row => row.Credentials,
                StringComparer.OrdinalIgnoreCase);
        }

        public Task<PronunciationCredentials?> ResolveAsync(string providerCode, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _warmedCodes.Add(providerCode);
            return Task.FromResult(_rows.TryGetValue(providerCode, out var credentials) ? credentials : null);
        }

        public bool IsRegistryConfigured(string providerCode) => _warmedCodes.Contains(providerCode) && _rows.ContainsKey(providerCode);

        public void Invalidate() => _warmedCodes.Clear();
    }

    private sealed class ResolverBackedProvider(
        string name,
        string providerCode,
        IPronunciationCredentialResolver credentialResolver) : IPronunciationAsrProvider
    {
        public string Name { get; } = name;
        public bool IsConfigured => credentialResolver.IsRegistryConfigured(providerCode);
        public Task<AsrResult> AnalyzeAsync(AsrRequest request, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
