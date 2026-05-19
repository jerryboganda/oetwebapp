using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
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
        => Build(providerPreference, environment: null, providers);

    private static PronunciationAsrProviderSelector Build(
        string providerPreference,
        IHostEnvironment? environment,
        params IPronunciationAsrProvider[] providers)
    {
        var opts = Options.Create(new PronunciationOptions { Provider = providerPreference });
        return new PronunciationAsrProviderSelector(
            providers,
            opts,
            NullLogger<PronunciationAsrProviderSelector>.Instance,
            environment);
    }

    private sealed class FakeProvider(string name, bool configured) : IPronunciationAsrProvider
    {
        public string Name { get; } = name;
        public bool IsConfigured { get; } = configured;
        public Task<AsrResult> AnalyzeAsync(AsrRequest request, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    [Fact]
    public void Auto_Prefers_Azure_When_Configured()
    {
        var sel = Build("auto",
            new FakeProvider("azure", configured: true),
            new FakeProvider("whisper", configured: true),
            new FakeProvider("mock", configured: true));
        Assert.Equal("azure", sel.Select().Name);
    }

    [Fact]
    public void Auto_Falls_Back_To_Whisper_When_Azure_Missing()
    {
        var sel = Build("auto",
            new FakeProvider("azure", configured: false),
            new FakeProvider("whisper", configured: true),
            new FakeProvider("mock", configured: true));
        Assert.Equal("whisper", sel.Select().Name);
    }

    [Fact]
    public void Auto_Falls_Back_To_Mock_When_No_Real_Provider_Configured()
    {
        var sel = Build("auto",
            new FakeProvider("azure", configured: false),
            new FakeProvider("whisper", configured: false),
            new FakeProvider("mock", configured: true));
        Assert.Equal("mock", sel.Select().Name);
    }

    [Fact]
    public void Explicit_Azure_Throws_When_Unconfigured()
    {
        var sel = Build("azure",
            new FakeProvider("azure", configured: false),
            new FakeProvider("mock", configured: true));
        Assert.Throws<InvalidOperationException>(() => sel.Select());
    }

    [Fact]
    public void Explicit_Whisper_Throws_When_Unconfigured()
    {
        var sel = Build("whisper",
            new FakeProvider("whisper", configured: false),
            new FakeProvider("mock", configured: true));
        Assert.Throws<InvalidOperationException>(() => sel.Select());
    }

    [Fact]
    public void Explicit_Mock_Returns_Mock_Always()
    {
        var sel = Build("mock",
            new FakeProvider("azure", configured: true),
            new FakeProvider("mock", configured: true));
        Assert.Equal("mock", sel.Select().Name);
    }

    [Fact]
    public void Production_Auto_Throws_Instead_Of_Falling_Back_To_Mock()
    {
        var sel = Build("auto",
            new FakeHostEnvironment(Environments.Production),
            new FakeProvider("azure", configured: false),
            new FakeProvider("whisper", configured: false),
            new FakeProvider("mock", configured: true));

        Assert.Throws<InvalidOperationException>(() => sel.Select());
    }

    [Fact]
    public void Production_Explicit_Mock_Throws()
    {
        var sel = Build("mock",
            new FakeHostEnvironment(Environments.Production),
            new FakeProvider("mock", configured: true));

        var ex = Assert.Throws<InvalidOperationException>(() => sel.Select());
        Assert.Contains("mock provider", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
