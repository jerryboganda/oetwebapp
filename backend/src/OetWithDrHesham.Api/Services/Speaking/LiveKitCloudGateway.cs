using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OetWithDrHesham.Api.Configuration;

namespace OetWithDrHesham.Api.Services.Speaking;

/// <summary>
/// Real <see cref="ILiveKitGateway"/> implementation backed by LiveKit
/// Cloud. Replaces the dev-only <see cref="LiveKitGatewayStub"/> when
/// <see cref="LiveKitOptions.IsEnabled"/> is true.
///
/// LiveKit access tokens are JWTs signed with HS256 using the configured
/// <see cref="LiveKitOptions.ApiSecret"/> as the shared secret. The token
/// carries the room name + identity + capability grants (publish,
/// subscribe, roomAdmin) as a custom <c>video</c> claim — the schema
/// matches the official LiveKit server SDKs (livekit-server-sdk-js,
/// livekit-server-sdk-go, livekit-server-sdk-python).
///
/// Why no NuGet SDK: LiveKit's official server SDK currently ships only
/// for Node, Go, Python, Ruby, and Java. There is no first-party .NET
/// SDK. Minting + verifying tokens against the public spec is small
/// enough (~60 LoC) that depending on an unmaintained community NuGet
/// would add more risk than it removes. REST endpoints we hit
/// (<c>/twirp/livekit.RoomService/CreateRoom</c> and
/// <c>/twirp/livekit.Egress/StartRoomCompositeEgress</c>) are stable
/// Twirp endpoints documented at https://docs.livekit.io/realtime/server/.
/// </summary>
public sealed class LiveKitCloudGateway : ILiveKitGateway
{
    private const string CreateRoomPath = "/twirp/livekit.RoomService/CreateRoom";
    private const string StartEgressPath = "/twirp/livekit.Egress/StartRoomCompositeEgress";
    private const string StopEgressPath = "/twirp/livekit.Egress/StopEgress";
    private const string HttpClientName = "LiveKitCloud";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IOptions<LiveKitOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LiveKitCloudGateway> _logger;

    public LiveKitCloudGateway(
        IOptions<LiveKitOptions> options,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<LiveKitCloudGateway> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─────────────────────────────────────────────────────────────────
    // Room creation
    // ─────────────────────────────────────────────────────────────────

    public async Task<LiveKitRoomCreationResult> CreateRoomAsync(string roomName, int maxDurationSeconds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roomName)) throw new ArgumentException("roomName required", nameof(roomName));
        var opts = _options.Value;
        EnsureConfigured(opts);

        // emptyTimeout = how long the room stays alive once empty;
        // departureTimeout = grace period after the last participant
        // disconnects before LiveKit auto-ends the room.
        var request = new CreateRoomRequest(
            Name: roomName,
            EmptyTimeout: 60,
            DepartureTimeout: 30,
            MaxParticipants: 8,
            Metadata: JsonSerializer.Serialize(new
            {
                source = "oet-speaking",
                maxDurationSeconds,
            }, JsonOpts));

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var http = await PrepareClientAsync(httpClient, opts);

        try
        {
            using var response = await http.PostAsJsonAsync(CreateRoomPath, request, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "LiveKitCloudGateway.CreateRoom failed status={Status} body={Body}",
                    (int)response.StatusCode,
                    body);
                throw new InvalidOperationException(
                    $"LiveKit CreateRoom failed: HTTP {(int)response.StatusCode}.");
            }

            var room = await response.Content.ReadFromJsonAsync<CreateRoomResponse>(JsonOpts, ct)
                ?? throw new InvalidOperationException("LiveKit CreateRoom returned an empty body.");

            _logger.LogInformation(
                "LiveKitCloudGateway.CreateRoom name={RoomName} sid={Sid}",
                roomName,
                room.Sid);

            return new LiveKitRoomCreationResult(room.Sid, opts.WssUrl);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "LiveKitCloudGateway.CreateRoom transport_error name={RoomName}",
                roomName);
            throw new InvalidOperationException(
                "LiveKit CreateRoom transport error.", ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Token mint
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mint a LiveKit access token (HS256 JWT). The <c>video</c> custom
    /// claim is the canonical LiveKit grant payload — its field names
    /// must match the schema parsed by the LiveKit server SFU.
    /// </summary>
    public Task<string> MintAccessTokenAsync(
        string roomName,
        string identity,
        LiveKitTokenCapabilities caps,
        TimeSpan ttl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roomName)) throw new ArgumentException("roomName required", nameof(roomName));
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("identity required", nameof(identity));

        var opts = _options.Value;
        EnsureConfigured(opts);

        // Capability scoping per Phase 6 plan:
        //   learner → roomJoin + canPublish (own tracks) + canSubscribe
        //   tutor   → all of the above + roomAdmin
        //
        // The capability split is encoded by the caller via
        // LiveKitTokenCapabilities; here we translate it into LiveKit's
        // grant schema. We treat "CanPublishVideo == true" as the tutor
        // signal (only tutor + assigned interlocutors publish video in
        // the OET Speaking flow), and use it to opt-into roomAdmin.
        var isTutor = caps.CanPublishVideo;
        var grant = new LiveKitVideoGrant
        {
            Room = roomName,
            RoomJoin = true,
            RoomAdmin = isTutor,
            CanPublish = caps.CanPublishAudio || caps.CanPublishVideo,
            CanSubscribe = caps.CanSubscribe,
            CanPublishData = true,
            // For learners we restrict source list to mic only; tutors
            // get full mic + cam + screenshare. LiveKit honours this
            // list when CanPublishSources is non-empty.
            CanPublishSources = isTutor
                ? new[] { "microphone", "camera", "screen_share", "screen_share_audio" }
                : new[] { "microphone" },
        };

        var now = _timeProvider.GetUtcNow();
        var exp = now.Add(ttl);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.ApiSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // LiveKit expects the grant under the "video" custom claim. JWT
        // standard claims (iss, sub, exp, nbf) are populated via the
        // token descriptor below.
        var grantJson = JsonSerializer.Serialize(grant, JsonOpts);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, opts.ApiKey),
            new Claim(JwtRegisteredClaimNames.Sub, identity),
            new Claim(JwtRegisteredClaimNames.Jti, $"oet-{Guid.NewGuid():N}"),
            new Claim("name", identity),
            new Claim("video", grantJson, JsonClaimValueTypes.Json),
        };

        var token = new JwtSecurityToken(
            issuer: opts.ApiKey,
            audience: null,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: exp.UtcDateTime,
            signingCredentials: credentials);

        var handler = new JwtSecurityTokenHandler();
        var encoded = handler.WriteToken(token);

        _logger.LogInformation(
            "LiveKitCloudGateway.MintAccessToken room={RoomName} identity={Identity} ttl={Ttl} canPubAudio={Audio} canPubVideo={Video} canSub={Sub} isAdmin={Admin}",
            roomName,
            identity,
            ttl,
            caps.CanPublishAudio,
            caps.CanPublishVideo,
            caps.CanSubscribe,
            isTutor);

        return Task.FromResult(encoded);
    }

    // ─────────────────────────────────────────────────────────────────
    // Egress lifecycle
    // ─────────────────────────────────────────────────────────────────

    public async Task<string> StartEgressAsync(string roomName, string outputUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roomName)) throw new ArgumentException("roomName required", nameof(roomName));
        if (string.IsNullOrWhiteSpace(outputUrl)) throw new ArgumentException("outputUrl required", nameof(outputUrl));

        var opts = _options.Value;
        EnsureConfigured(opts);

        // outputUrl format expectation: "s3://bucket-name/key.mp4". We
        // map this into a LiveKit S3Upload + EncodedFileOutput pair.
        // If the format is anything else (e.g. plain https URL) we fall
        // through to a direct file output — LiveKit treats this as a
        // local path on the egress worker, useful for self-hosted.
        var fileOutput = BuildFileOutput(outputUrl, opts);

        var request = new StartEgressRequest(
            RoomName: roomName,
            Layout: "grid",
            AudioOnly: false,
            VideoOnly: false,
            File: fileOutput);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var http = await PrepareClientAsync(httpClient, opts);

        try
        {
            using var response = await http.PostAsJsonAsync(StartEgressPath, request, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "LiveKitCloudGateway.StartEgress failed status={Status} body={Body}",
                    (int)response.StatusCode,
                    body);
                throw new InvalidOperationException(
                    $"LiveKit StartEgress failed: HTTP {(int)response.StatusCode}.");
            }

            var info = await response.Content.ReadFromJsonAsync<EgressInfoResponse>(JsonOpts, ct)
                ?? throw new InvalidOperationException("LiveKit StartEgress returned an empty body.");

            _logger.LogInformation(
                "LiveKitCloudGateway.StartEgress room={RoomName} egressId={EgressId} output={OutputUrl}",
                roomName,
                info.EgressId,
                outputUrl);

            return info.EgressId;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "LiveKitCloudGateway.StartEgress transport_error room={RoomName}",
                roomName);
            throw new InvalidOperationException("LiveKit StartEgress transport error.", ex);
        }
    }

    public async Task<bool> StopEgressAsync(string egressId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(egressId)) throw new ArgumentException("egressId required", nameof(egressId));
        var opts = _options.Value;
        EnsureConfigured(opts);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var http = await PrepareClientAsync(httpClient, opts);

        try
        {
            using var response = await http.PostAsJsonAsync(StopEgressPath, new { egressId }, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "LiveKitCloudGateway.StopEgress non_success status={Status} body={Body}",
                    (int)response.StatusCode,
                    body);
                return false;
            }
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "LiveKitCloudGateway.StopEgress transport_error egressId={EgressId}",
                egressId);
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Webhook signature verification
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verify the HMAC-SHA256 signature attached to an inbound LiveKit
    /// webhook. LiveKit Cloud signs each event by placing a JWT in the
    /// <c>Authorization</c> header whose <c>sha256</c> claim is the
    /// base64-encoded digest of the raw request body. We accept BOTH
    /// the JWT-in-Authorization scheme and the older hex HMAC scheme
    /// (used by some compatible providers + our test fixtures).
    /// </summary>
    public bool VerifyWebhookSignature(string payload, string signature)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));
        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogInformation("LiveKitCloudGateway.VerifyWebhookSignature missing_signature");
            return false;
        }

        var opts = _options.Value;
        var secret = opts.WebhookSigningSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning("LiveKitCloudGateway.VerifyWebhookSignature secret_not_configured");
            return false;
        }

        var trimmed = signature.Trim();

        // LiveKit Cloud JWT scheme: Authorization header carries a JWT
        // signed with the api secret; the body hash lives in the sha256
        // claim. If the header parses as a JWT against the configured
        // signing secret AND the body hash matches, we accept it.
        if (TryVerifyJwtSignature(trimmed, payload, opts))
        {
            _logger.LogInformation("LiveKitCloudGateway.VerifyWebhookSignature jwt_match");
            return true;
        }

        // Fallback: hex HMAC scheme (compatible providers + tests).
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = Convert.ToHexString(computed).ToLowerInvariant();
        var supplied = trimmed.ToLowerInvariant();
        if (supplied.Length != expected.Length)
        {
            _logger.LogInformation("LiveKitCloudGateway.VerifyWebhookSignature length_mismatch");
            return false;
        }

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied),
            Encoding.UTF8.GetBytes(expected));

        _logger.LogInformation(
            "LiveKitCloudGateway.VerifyWebhookSignature hex_result={Result}",
            ok ? "match" : "mismatch");

        return ok;
    }

    private bool TryVerifyJwtSignature(string headerValue, string payload, LiveKitOptions opts)
    {
        try
        {
            // Strip "Bearer " prefix if present.
            var raw = headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? headerValue.Substring(7)
                : headerValue;
            // Heuristic: a JWT has three dot-separated segments.
            if (raw.Count(c => c == '.') != 2) return false;

            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.ApiSecret));
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(5),
            };

            handler.ValidateToken(raw, parameters, out var validated);

            var jwt = (JwtSecurityToken)validated;
            var sha256Claim = jwt.Claims.FirstOrDefault(c => c.Type == "sha256")?.Value;
            if (string.IsNullOrEmpty(sha256Claim)) return false;

            var bodyHash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            var expected = Convert.ToBase64String(bodyHash);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(sha256Claim),
                Encoding.UTF8.GetBytes(expected));
        }
        catch
        {
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private async Task<HttpClient> PrepareClientAsync(HttpClient client, LiveKitOptions opts)
    {
        // LiveKit Twirp endpoints share the same auth scheme as Egress
        // and Room services: an HS256 JWT minted from the api key/secret
        // with a 60-second TTL. Caller-side cache could be added later
        // but token minting is sub-millisecond so we skip for now.
        var management = MintManagementToken(opts);

        // Determine REST host. WSS URL is "wss://x.livekit.cloud"; we
        // map it to "https://x.livekit.cloud" for REST.
        var host = NormaliseHost(opts.WssUrl);
        if (client.BaseAddress is null || client.BaseAddress.Host != new Uri(host).Host)
        {
            client.BaseAddress = new Uri(host);
        }
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", management);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        await Task.CompletedTask;
        return client;
    }

    private string MintManagementToken(LiveKitOptions opts)
    {
        var now = _timeProvider.GetUtcNow();
        var exp = now.AddMinutes(2);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.ApiSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Management tokens carry roomAdmin + roomCreate + recording grants
        // so the same JWT can hit both RoomService and Egress endpoints.
        var grant = JsonSerializer.Serialize(new LiveKitVideoGrant
        {
            RoomCreate = true,
            RoomAdmin = true,
            RoomList = true,
            RoomRecord = true,
        }, JsonOpts);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, opts.ApiKey),
            new Claim(JwtRegisteredClaimNames.Sub, opts.ApiKey),
            new Claim("video", grant, JsonClaimValueTypes.Json),
        };

        var token = new JwtSecurityToken(
            issuer: opts.ApiKey,
            audience: null,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: exp.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NormaliseHost(string wssUrl)
    {
        if (string.IsNullOrWhiteSpace(wssUrl)) return "https://example.livekit.cloud";
        if (wssUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + wssUrl.Substring(6);
        }
        if (wssUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            return "http://" + wssUrl.Substring(5);
        }
        return wssUrl;
    }

    private static LiveKitFileOutput BuildFileOutput(string outputUrl, LiveKitOptions opts)
    {
        // s3://bucket/key.mp4 → S3 upload block
        if (outputUrl.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
        {
            var rest = outputUrl.Substring(5);
            var slash = rest.IndexOf('/');
            var bucket = slash > 0 ? rest.Substring(0, slash) : rest;
            var filepath = slash > 0 ? rest.Substring(slash + 1) : Path.GetFileName(rest);

            return new LiveKitFileOutput
            {
                Filepath = filepath,
                FileType = "MP4",
                S3 = new LiveKitS3Upload
                {
                    Bucket = bucket,
                    // Region + credentials are configured server-side on
                    // the LiveKit Cloud project; we leave them empty so
                    // the provider falls back to its environment.
                },
            };
        }

        // Default: local-disk path (self-hosted / Docker dev clusters)
        return new LiveKitFileOutput
        {
            Filepath = outputUrl,
            FileType = "MP4",
        };
    }

    private static void EnsureConfigured(LiveKitOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            throw new InvalidOperationException(
                "LiveKit ApiKey is not configured. Set LiveKit:ApiKey in configuration.");
        }
        if (string.IsNullOrWhiteSpace(opts.ApiSecret))
        {
            throw new InvalidOperationException(
                "LiveKit ApiSecret is not configured. Set LiveKit:ApiSecret in configuration.");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Request / response shapes (Twirp wire format — camelCase JSON)
    // ─────────────────────────────────────────────────────────────────

    private sealed record CreateRoomRequest(
        string Name,
        int EmptyTimeout,
        int DepartureTimeout,
        int MaxParticipants,
        string Metadata);

    private sealed record CreateRoomResponse(
        string Sid,
        string Name,
        DateTimeOffset? CreationTime);

    private sealed record StartEgressRequest(
        string RoomName,
        string Layout,
        bool AudioOnly,
        bool VideoOnly,
        LiveKitFileOutput? File);

    private sealed record EgressInfoResponse(
        [property: JsonPropertyName("egressId")] string EgressId,
        [property: JsonPropertyName("roomId")] string? RoomId,
        [property: JsonPropertyName("status")] string? Status);

    private sealed class LiveKitFileOutput
    {
        public string Filepath { get; set; } = string.Empty;
        public string FileType { get; set; } = "MP4";
        public LiveKitS3Upload? S3 { get; set; }
    }

    private sealed class LiveKitS3Upload
    {
        public string Bucket { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string? AccessKey { get; set; }
        public string? Secret { get; set; }
    }

    /// <summary>
    /// LiveKit grant schema — must use these exact field names (camelCase
    /// in JSON) to be accepted by the LiveKit SFU. Matches the public
    /// schema at https://docs.livekit.io/realtime/server/keys/.
    /// </summary>
    private sealed class LiveKitVideoGrant
    {
        public string? Room { get; set; }
        public bool RoomJoin { get; set; }
        public bool RoomAdmin { get; set; }
        public bool RoomCreate { get; set; }
        public bool RoomList { get; set; }
        public bool RoomRecord { get; set; }
        public bool CanPublish { get; set; }
        public bool CanSubscribe { get; set; }
        public bool CanPublishData { get; set; }
        public string[]? CanPublishSources { get; set; }
        public bool? Hidden { get; set; }
        public bool? Recorder { get; set; }
    }
}
