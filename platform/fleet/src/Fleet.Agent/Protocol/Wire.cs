using System.Text.RegularExpressions;

namespace Fleet.Agent;

/// <summary>Wire constants of OET-RWP/1 (REMOTE-WORKER-PROTOCOL.md sections 2 and 4).</summary>
internal static class Wire
{
    public const string BasePath = "/v1/internal/remote-worker";

    public const string HeaderProtocol = "X-Remote-Protocol";
    public const string HeaderProtocolMin = "X-Remote-Protocol-Min";
    public const string HeaderAgentVersion = "X-Remote-Agent-Version";
    public const string HeaderFence = "X-Remote-Fence";
    public const string HeaderContentSha = "X-Content-SHA256";
    public const string HeaderReason = "X-Remote-Reason";
    public const string HeaderDesiredRevision = "X-Remote-Desired-Revision";
    public const string HeaderCorrelation = "X-Correlation-Id";

    /// <summary>The set S of protocol numbers this build speaks (section 2.4).</summary>
    public static readonly int[] SupportedProtocols = [1];

    /// <summary>Conservative local lease safety margin (section 4.2.3 rule 2).</summary>
    public const long LeaseSafetyMarginMs = 5000;

    // Per-request timeouts (section 2.7).
    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan CompleteTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan InputIdleTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan OutputIdleTimeout = TimeSpan.FromSeconds(120);

    public static readonly Regex NodeTokenPattern = new("^orw1_[0-9a-f]{16}_[A-Za-z0-9_-]{43}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static readonly Regex NodeIdPattern = new("^rw_[0-9a-z]{26}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static readonly Regex JobIdPattern = new("^rj_[0-9a-z]{26}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static readonly Regex DigestPattern = new("^sha256:[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static readonly Regex Sha256HexPattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static readonly Regex InputNamePattern = new("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static readonly Regex EngineVersionPattern = new("^[A-Za-z0-9._:+/-]{1,96}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>fail.message grammar (section 4.6): no content, no paths.</summary>
    public static readonly Regex FailMessagePattern = new("^[A-Za-z0-9 _.:/=,()-]{0,200}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

/// <summary>Job kinds the registry (section 6.0) defines.</summary>
internal static class JobKinds
{
    public const string PdfExtract = "pdf.extract";
    public const string CompanionIndexPrep = "companion.index-prep";
    public const string MediaAudioExtract = "media.audio-extract";
    public const string MediaSpeakingJoin = "media.speaking-join";
}

/// <summary>Agent fail codes (section 4.6).</summary>
internal static class FailCodes
{
    public const string Timeout = "timeout";
    public const string Oom = "oom";
    public const string InternalError = "internal_error";
    public const string InputUnavailable = "input_unavailable";
    public const string InputHashMismatch = "input_hash_mismatch";
    public const string InputTooLarge = "input_too_large";
    public const string ExtractException = "extract_exception";
    public const string NotPdf = "not_pdf";
    public const string NoAudioStream = "no_audio_stream";
    public const string DurationExceeded = "duration_exceeded";
    public const string TranscoderUnavailable = "transcoder_unavailable";
    public const string LimitsExceeded = "limits_exceeded";
    public const string Shutdown = "shutdown";
    public const string Drain = "drain";
    public const string PressureShed = "pressure_shed";
    public const string ProtocolMismatch = "protocol_mismatch";
}
