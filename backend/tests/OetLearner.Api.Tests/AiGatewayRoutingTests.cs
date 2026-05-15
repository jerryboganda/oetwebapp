using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests;

public class AiGatewayRoutingTests
{
    private readonly RulebookLoader _loader = new();

    [Fact]
    public async Task CompleteAsync_AppliesFeatureRouteProviderAndModel_WhenRequestModelIsOmitted()
    {
        var registryProvider = new CapturingProvider("registry");
        var mockProvider = new CapturingProvider("mock");
        var gateway = new AiGatewayService(
            _loader,
            new IAiModelProvider[] { mockProvider, registryProvider },
            providerRegistry: new FakeProviderRegistry("openai-platform", AiProviderDialect.OpenAiCompatible),
            featureRouteResolver: new FakeFeatureRouteResolver(AiFeatureCodes.AdminWritingDraft, "openai-platform", "route-controlled-model"));

        var result = await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = BuildWritingPrompt(gateway),
            FeatureCode = AiFeatureCodes.AdminWritingDraft,
        });

        Assert.Equal("completion from registry", result.Completion);
        Assert.Null(mockProvider.LastRequest);
        Assert.NotNull(registryProvider.LastRequest);
        Assert.Equal("route-controlled-model", registryProvider.LastRequest!.Model);
    }

    [Fact]
    public async Task CompleteAsync_UsesExplicitRequestModel_WhenFeatureRouteAlsoHasModel()
    {
        var registryProvider = new CapturingProvider("registry");
        var gateway = new AiGatewayService(
            _loader,
            new IAiModelProvider[] { registryProvider },
            providerRegistry: new FakeProviderRegistry("openai-platform", AiProviderDialect.OpenAiCompatible),
            featureRouteResolver: new FakeFeatureRouteResolver(AiFeatureCodes.AdminWritingDraft, "openai-platform", "route-controlled-model"));

        await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = BuildWritingPrompt(gateway),
            FeatureCode = AiFeatureCodes.AdminWritingDraft,
            Model = "explicit-model",
        });

        Assert.NotNull(registryProvider.LastRequest);
        Assert.Equal("explicit-model", registryProvider.LastRequest!.Model);
    }

    [Fact]
    public async Task CompleteAsync_UsesProviderDefaultModel_WhenFeatureRouteModelIsOmitted()
    {
        var registryProvider = new CapturingProvider("registry");
        var gateway = new AiGatewayService(
            _loader,
            new IAiModelProvider[] { registryProvider },
            providerRegistry: new FakeProviderRegistry("openai-platform", AiProviderDialect.OpenAiCompatible),
            featureRouteResolver: new FakeFeatureRouteResolver(AiFeatureCodes.AdminWritingDraft, "openai-platform", null));

        await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = BuildWritingPrompt(gateway),
            FeatureCode = AiFeatureCodes.AdminWritingDraft,
        });

        Assert.NotNull(registryProvider.LastRequest);
        Assert.Equal("provider-default-model", registryProvider.LastRequest!.Model);
        Assert.Equal("openai-platform", registryProvider.LastRequest.ProviderCode);
    }

    [Fact]
    public async Task CompleteAsync_ResolvesCredentialsAgainstFeatureRouteProvider()
    {
        var registryProvider = new CapturingProvider("registry");
        var credentialResolver = new CapturingCredentialResolver();
        var gateway = new AiGatewayService(
            _loader,
            new IAiModelProvider[] { registryProvider },
            credentialResolver: credentialResolver,
            providerRegistry: new FakeProviderRegistry("openai-platform", AiProviderDialect.OpenAiCompatible),
            featureRouteResolver: new FakeFeatureRouteResolver(AiFeatureCodes.ConversationReply, "openai-platform", null));

        await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = BuildWritingPrompt(gateway),
            FeatureCode = AiFeatureCodes.ConversationReply,
            UserId = "learner-1",
        });

        Assert.Equal("learner-1", credentialResolver.LastUserId);
        Assert.Equal(AiFeatureCodes.ConversationReply, credentialResolver.LastFeatureCode);
        Assert.Equal("openai-platform", credentialResolver.LastProviderCode);
    }

    [Fact]
    public async Task CompleteAsync_FallbackRegistrySelection_IgnoresHigherPriorityVoiceRows()
    {
        var registryProvider = new CapturingProvider("registry");
        var mockProvider = new CapturingProvider("mock");
        var gateway = new AiGatewayService(
            _loader,
            new IAiModelProvider[] { mockProvider, registryProvider },
            providerRegistry: new FakeProviderRegistry(
                ProviderRow("azure-tts", AiProviderDialect.AzureTts, AiProviderCategory.Tts, 0, "voice-default-model"),
                ProviderRow("openai-platform", AiProviderDialect.OpenAiCompatible, AiProviderCategory.TextChat, 10, "text-default-model")));

        var result = await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = BuildWritingPrompt(gateway),
            FeatureCode = AiFeatureCodes.WritingGrade,
        });

        Assert.Equal("completion from registry", result.Completion);
        Assert.Null(mockProvider.LastRequest);
        Assert.NotNull(registryProvider.LastRequest);
        Assert.Equal("openai-platform", registryProvider.LastRequest!.ProviderCode);
        Assert.Equal("text-default-model", registryProvider.LastRequest.Model);
    }

    [Fact]
    public async Task CompleteAsync_FallbackRegistrySelection_UsesMock_WhenTextProviderHasNoPlatformKey()
    {
        var registryProvider = new CapturingProvider("registry");
        var mockProvider = new CapturingProvider("mock");
        var uncredentialedTextProvider = ProviderRow(
            "openai-platform",
            AiProviderDialect.OpenAiCompatible,
            AiProviderCategory.TextChat,
            encryptedApiKey: string.Empty);
        var gateway = new AiGatewayService(
            _loader,
            new IAiModelProvider[] { mockProvider, registryProvider },
            providerRegistry: new FakeProviderRegistry(uncredentialedTextProvider));

        var result = await gateway.CompleteAsync(new AiGatewayRequest
        {
            Prompt = BuildWritingPrompt(gateway),
            FeatureCode = AiFeatureCodes.WritingGrade,
        });

        Assert.Equal("completion from mock", result.Completion);
        Assert.NotNull(mockProvider.LastRequest);
        Assert.Null(registryProvider.LastRequest);
    }

    private static AiGroundedPrompt BuildWritingPrompt(IAiGatewayService gateway)
        => gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.GenerateContent,
            LetterType = "routine_referral",
        });

    private sealed class CapturingProvider(string name) : IAiModelProvider
    {
        public string Name { get; } = name;
        public AiProviderRequest? LastRequest { get; private set; }

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new AiProviderCompletion
            {
                Text = $"completion from {Name}",
                Usage = new AiUsage { PromptTokens = 3, CompletionTokens = 2 },
            });
        }
    }

    private sealed class CapturingCredentialResolver : IAiCredentialResolver
    {
        public string? LastUserId { get; private set; }
        public string? LastFeatureCode { get; private set; }
        public string? LastProviderCode { get; private set; }

        public Task<AiCredentialResolution> ResolveAsync(
            string? userId,
            string featureCode,
            string? callerRequestedProviderCode,
            CancellationToken ct)
        {
            LastUserId = userId;
            LastFeatureCode = featureCode;
            LastProviderCode = callerRequestedProviderCode;
            return Task.FromResult(new AiCredentialResolution(
                AiKeySource.Platform,
                callerRequestedProviderCode ?? "platform-default",
                ApiKeyPlaintext: null,
                BaseUrlOverride: null,
                CredentialId: null,
                PolicyTrace: "test.platform"));
        }
    }

    private sealed class FakeFeatureRouteResolver(string featureCode, string providerCode, string? model) : IAiFeatureRouteResolver
    {
        public Task<AiFeatureRouteResolution?> ResolveAsync(string requestedFeatureCode, CancellationToken ct)
            => Task.FromResult<AiFeatureRouteResolution?>(
                string.Equals(requestedFeatureCode, featureCode, StringComparison.OrdinalIgnoreCase)
                    ? new AiFeatureRouteResolution(providerCode, model)
                    : null);

        public bool IsKnownFeatureCode(string requestedFeatureCode)
            => string.Equals(requestedFeatureCode, featureCode, StringComparison.OrdinalIgnoreCase);
    }

    private static AiProvider ProviderRow(
        string providerCode,
        AiProviderDialect dialect,
        AiProviderCategory category = AiProviderCategory.TextChat,
        int failoverPriority = 0,
        string defaultModel = "provider-default-model",
        string encryptedApiKey = "encrypted-test-key")
        => new()
        {
            Id = providerCode,
            Code = providerCode,
            Name = providerCode,
            Dialect = dialect,
            Category = category,
            BaseUrl = "https://provider.example.test",
            IsActive = true,
            FailoverPriority = failoverPriority,
            DefaultModel = defaultModel,
            EncryptedApiKey = encryptedApiKey,
        };

    private sealed class FakeProviderRegistry : IAiProviderRegistry
    {
        private readonly IReadOnlyList<AiProvider> _providers;

        public FakeProviderRegistry(string providerCode, AiProviderDialect dialect)
            : this(ProviderRow(providerCode, dialect))
        {
        }

        public FakeProviderRegistry(params AiProvider[] providers)
        {
            _providers = providers;
        }

        public Task<AiProvider?> FindByCodeAsync(string code, CancellationToken ct)
            => Task.FromResult(_providers.FirstOrDefault(provider =>
                provider.IsActive && string.Equals(provider.Code, code, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<AiProvider>> ListActiveAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(_providers
                .Where(provider => provider.IsActive)
                .OrderBy(provider => provider.FailoverPriority)
                .ToList());

        public Task<IReadOnlyList<AiProvider>> ListByCategoryAsync(AiProviderCategory category, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(_providers
                .Where(provider => provider.IsActive && provider.Category == category)
                .OrderBy(provider => provider.FailoverPriority)
                .ToList());

        public Task<string?> GetPlatformKeyAsync(string providerCode, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }
}
