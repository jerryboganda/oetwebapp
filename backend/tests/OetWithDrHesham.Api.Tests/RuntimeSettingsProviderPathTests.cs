using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Conversation;
using OetWithDrHesham.Api.Services.Conversation.Tts;
using OetWithDrHesham.Api.Services.Pronunciation;
using OetWithDrHesham.Api.Services.Settings;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Tests;

public sealed class RuntimeSettingsProviderCapabilityTests
{
    [Fact]
    public async Task ConversationTtsCapability_UsesSnapshotSynchronously_AndRefreshesAsynchronously()
    {
        var options = new MutableConversationOptionsProvider(new ConversationOptions());
        var provider = new ElevenLabsConversationTtsProvider(
            null!,
            options,
            NullLogger<ElevenLabsConversationTtsProvider>.Instance);

        Assert.False(provider.IsConfigured);
        Assert.Equal(0, options.AsyncReads);

        options.Set(new ConversationOptions { ElevenLabsApiKey = "runtime-key" });
        Assert.True(provider.IsConfigured);
        Assert.Equal(0, options.AsyncReads);

        options.Set(new ConversationOptions());
        Assert.False(await provider.IsConfiguredAsync());
        Assert.Equal(1, options.AsyncReads);

        options.Set(new ConversationOptions { ElevenLabsApiKey = "rotated-key" });
        Assert.True(await provider.IsConfiguredAsync());
        Assert.Equal(2, options.AsyncReads);
    }

    [Fact]
    public async Task WhisperCapabilities_UseRuntimeSnapshotWithoutSynchronousReads()
    {
        var runtime = new MutableRuntimeSettingsProvider(configured: false);
        var credentials = new EmptyPronunciationCredentialResolver();
        var pronunciation = new WhisperPronunciationAsrProvider(
            null!,
            Options.Create(new PronunciationOptions()),
            null!,
            credentials,
            runtime,
            null!,
            TimeProvider.System,
            NullLogger<WhisperPronunciationAsrProvider>.Instance);
        var speaking = new OpenAiWhisperSpeakingProvider(
            null!,
            null!,
            runtime,
            credentials,
            null!,
            TimeProvider.System,
            NullLogger<OpenAiWhisperSpeakingProvider>.Instance);

        Assert.False(pronunciation.IsConfigured);
        Assert.False(speaking.IsConfigured);
        Assert.Equal(0, runtime.AsyncReads);

        runtime.SetConfigured(true);
        Assert.True(pronunciation.IsConfigured);
        Assert.True(speaking.IsConfigured);
        Assert.Equal(0, runtime.AsyncReads);

        runtime.SetConfigured(false);
        Assert.False(await pronunciation.IsConfiguredAsync());
        runtime.SetConfigured(true);
        Assert.True(await pronunciation.IsConfiguredAsync());
        Assert.Equal(2, runtime.AsyncReads);
    }

    private sealed class MutableConversationOptionsProvider(
        ConversationOptions current) : IConversationOptionsProvider
    {
        private ConversationOptions _current = current;
        private int _asyncReads;

        public ConversationOptions Current => Volatile.Read(ref _current);
        public int AsyncReads => Volatile.Read(ref _asyncReads);

        public Task<ConversationOptions> GetAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _asyncReads);
            return Task.FromResult(Current);
        }

        public void Set(ConversationOptions value) => Volatile.Write(ref _current, value);
        public void Invalidate() { }
    }

    private sealed class MutableRuntimeSettingsProvider : IRuntimeSettingsProvider
    {
        private RuntimeSettingsSnapshot _snapshot;
        private int _asyncReads;

        public MutableRuntimeSettingsProvider(bool configured)
        {
            _snapshot = BuildSnapshot(configured);
        }

        public RuntimeSettingsSnapshot CurrentSnapshot => Volatile.Read(ref _snapshot);
        public int AsyncReads => Volatile.Read(ref _asyncReads);

        public void SetConfigured(bool configured)
            => Volatile.Write(ref _snapshot, BuildSnapshot(configured));

        public Task<EffectiveSettings> GetAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _asyncReads);
            return Task.FromResult(CurrentSnapshot.Effective);
        }

        public Task<RuntimeSettingsRow> GetRawAsync(CancellationToken ct = default)
            => Task.FromResult(CurrentSnapshot.Raw);

        public void Invalidate() { }
        public string Protect(string plain) => plain;
        public string? Unprotect(string? cipher) => cipher;

        private static RuntimeSettingsSnapshot BuildSnapshot(bool configured)
        {
            var whisper = configured
                ? new SpeakingWhisperSettings(
                    "runtime-whisper-key",
                    "https://api.openai.com/v1",
                    "whisper-1",
                    IsConfigured: true)
                : new SpeakingWhisperSettings(
                    null,
                    "https://api.openai.com/v1",
                    "whisper-1",
                    IsConfigured: false);
            return new RuntimeSettingsSnapshot(
                TestRuntimeSettingsProvider.Base() with { SpeakingWhisper = whisper },
                new RuntimeSettingsRow { Id = "default" });
        }
    }

    private sealed class EmptyPronunciationCredentialResolver : IPronunciationCredentialResolver
    {
        public Task<PronunciationCredentials?> ResolveAsync(string providerCode, CancellationToken ct)
            => Task.FromResult<PronunciationCredentials?>(null);

        public bool IsRegistryConfigured(string providerCode) => false;
        public void Invalidate() { }
    }
}

public sealed class RuntimeSettingsProviderPathStructureTests
{
    private static readonly Regex SyncWaitPattern = new(
        @"\.GetAwaiter\s*\(\s*\)\s*\.GetResult\s*\(\s*\)|\.Result\b|\.ContinueWith\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void OwnedRequestAndProviderPaths_ContainNoSyncWaitsOrContinuations()
    {
        var root = FindRepositoryRoot();
        var files = new List<string>
        {
            "backend/src/OetWithDrHesham.Api/Services/AuthTokenService.cs",
            "backend/src/OetWithDrHesham.Api/Services/ExternalAuthService.cs",
            "backend/src/OetWithDrHesham.Api/Services/ExternalIdentityProviderClient.cs",
            "backend/src/OetWithDrHesham.Api/Services/PlatformLinkService.cs",
            "backend/src/OetWithDrHesham.Api/Services/Settings/RuntimeSettingsProvider.cs",
            "backend/src/OetWithDrHesham.Api/Services/Conversation/ConversationOptionsProvider.cs",
            "backend/src/OetWithDrHesham.Api/Services/Conversation/ConversationAudioService.cs",
            "backend/src/OetWithDrHesham.Api/Services/Pronunciation/WhisperPronunciationAsrProvider.cs",
            "backend/src/OetWithDrHesham.Api/Services/Speaking/OpenAiWhisperSpeakingProvider.cs",
            "backend/src/OetWithDrHesham.Api/Services/Rulebook/AiProviderConnectionTester.cs",
            "backend/src/OetWithDrHesham.Api/Services/Reading/ReadingAttemptService.cs",
        };

        files.AddRange(EnumerateRelativeCsFiles(
            root,
            "backend/src/OetWithDrHesham.Api/Services/Conversation/Asr"));
        files.AddRange(EnumerateRelativeCsFiles(
            root,
            "backend/src/OetWithDrHesham.Api/Services/Conversation/Tts"));

        foreach (var relativePath in files.Distinct(StringComparer.Ordinal))
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.False(
                SyncWaitPattern.IsMatch(source),
                $"{relativePath} contains a blocking task wait or ContinueWith.");
        }
    }

    private static IEnumerable<string> EnumerateRelativeCsFiles(string root, string relativeDirectory)
        => Directory.EnumerateFiles(
                Path.Combine(root, relativeDirectory),
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetRelativePath(root, path));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend", "src", "OetWithDrHesham.Api")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
