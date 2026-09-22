using Microsoft.Extensions.Logging;

namespace OetLearner.Api.Services.Speaking;

public sealed class LiveKitProviderUnavailable : ILiveKitGateway
{
    private readonly ILogger<LiveKitProviderUnavailable> _logger;

    public LiveKitProviderUnavailable(ILogger<LiveKitProviderUnavailable> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<LiveKitRoomCreationResult> CreateRoomAsync(
        string roomName,
        int maxDurationSeconds,
        CancellationToken ct)
        => Fail<LiveKitRoomCreationResult>();

    public Task<string> MintAccessTokenAsync(
        string roomName,
        string identity,
        LiveKitTokenCapabilities caps,
        TimeSpan ttl,
        CancellationToken ct)
        => Fail<string>();

    public Task<string> StartEgressAsync(string roomName, string outputUrl, CancellationToken ct)
        => Fail<string>();

    public Task<bool> StopEgressAsync(string egressId, CancellationToken ct)
        => Fail<bool>();

    public Task DeleteRoomAsync(string roomName, CancellationToken ct)
        => Fail();

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        _logger.LogWarning(
            "LiveKit provider is not configured. Rejecting webhook without a mock fallback.");
        return false;
    }

    private Task<T> Fail<T>()
        => Task.FromException<T>(new LiveKitProviderUnavailableException(
            "The live tutor provider is not configured. Configure LiveKit before using human Speaking rooms."));

    private Task Fail()
        => Task.FromException(new LiveKitProviderUnavailableException(
            "The live tutor provider is not configured. Configure LiveKit before using human Speaking rooms."));
}

public sealed class LiveKitProviderUnavailableException : Exception
{
    public LiveKitProviderUnavailableException(string message) : base(message) { }
}
