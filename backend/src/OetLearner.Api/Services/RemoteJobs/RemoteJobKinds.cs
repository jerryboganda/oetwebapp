using System.Text.Json;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.RemoteJobs;

/// <summary>Feature-flag keys (rows in <c>FeatureFlags</c>; an absent row is OFF, fail closed).</summary>
public static class RemoteJobFlagKeys
{
    /// <summary>Master switch for producers and <c>claim</c> (OET-RWP/1 section 5.3).</summary>
    public const string Master = "remote_jobs_enabled";
    public const string PdfExtract = "remote_jobs_kind_pdf_extract";
    public const string PdfExtractShadow = "remote_jobs_kind_pdf_extract_shadow";
    public const string CompanionIndexPrep = "remote_jobs_kind_companion_index_prep";
    public const string MediaAudioExtract = "remote_jobs_kind_media_audio_extract";
    public const string MediaSpeakingJoin = "remote_jobs_kind_media_speaking_join";
    public const string FleetService = "remote_fleet_service_enabled";

    /// <summary>EMERGENCY: ON makes <c>complete</c> answer <c>503 applies_frozen</c> and cancels the job.</summary>
    public const string FreezeApplies = "remote_jobs_freeze_applies";

    public static readonly IReadOnlyList<string> All =
    [
        Master, PdfExtract, PdfExtractShadow, CompanionIndexPrep, MediaAudioExtract, MediaSpeakingJoin,
        FleetService, FreezeApplies,
    ];
}

/// <summary>Resource limits a job carries; binding on the agent (OET-RWP/1 section 6.0).</summary>
public sealed record RemoteJobLimits(
    int Weight,
    int CpuMilli,
    int MemMiB,
    int TmpMiB,
    int TimeoutSeconds,
    int DeadlineSeconds,
    long MaxInputBytes,
    long MaxResultBytes,
    long MaxOutputBytes,
    int MaxOutputs)
{
    /// <summary>The stored form (includes <c>deadlineSeconds</c>, which the claim statement reads).</summary>
    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["weight"] = Weight,
        ["cpuMilli"] = CpuMilli,
        ["memMiB"] = MemMiB,
        ["tmpMiB"] = TmpMiB,
        ["timeoutSeconds"] = TimeoutSeconds,
        ["deadlineSeconds"] = DeadlineSeconds,
        ["maxInputBytes"] = MaxInputBytes,
        ["maxResultBytes"] = MaxResultBytes,
        ["maxOutputBytes"] = MaxOutputBytes,
        ["maxOutputs"] = MaxOutputs,
    });

    /// <summary>Reads limits back from the stored JSON; missing members fall back to the kind default.</summary>
    public static RemoteJobLimits FromJson(string? json, RemoteJobLimits fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return fallback;

            int Int(string name, int fallbackValue)
                => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v)
                    ? v : fallbackValue;
            long Long(string name, long fallbackValue)
                => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v)
                    ? v : fallbackValue;

            return new RemoteJobLimits(
                Int("weight", fallback.Weight),
                Int("cpuMilli", fallback.CpuMilli),
                Int("memMiB", fallback.MemMiB),
                Int("tmpMiB", fallback.TmpMiB),
                Int("timeoutSeconds", fallback.TimeoutSeconds),
                Int("deadlineSeconds", fallback.DeadlineSeconds),
                Long("maxInputBytes", fallback.MaxInputBytes),
                Long("maxResultBytes", fallback.MaxResultBytes),
                Long("maxOutputBytes", fallback.MaxOutputBytes),
                Int("maxOutputs", fallback.MaxOutputs));
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}

/// <summary>One row of the authoritative kind registry (the API owns it; agents receive limits per job).</summary>
public sealed record RemoteKindSpec(
    string Kind,
    int SchemaVersion,
    RemoteJobLimits Limits,
    string ApplyFlagKey)
{
    /// <summary>The <c>schema</c> member every result of this kind must echo.</summary>
    public string ResultSchema => $"{Kind}.result/{SchemaVersion}";
}

/// <summary>
/// Authoritative registry of remote job kinds (OET-RWP/1 section 6.0). Kinds, schema versions,
/// weights and limits live in API code; a node's own <c>kinds[]</c> report can only NARROW the offer.
/// </summary>
public static class RemoteJobKinds
{
    public const string PdfExtract = "pdf.extract";
    public const string CompanionIndexPrep = "companion.index-prep";
    public const string MediaAudioExtract = "media.audio-extract";
    public const string MediaSpeakingJoin = "media.speaking-join";

    /// <summary>Result cap of a shadow run (hash-only, no pages).</summary>
    public const long ShadowMaxResultBytes = 1_048_576;

    public static readonly IReadOnlyList<RemoteKindSpec> All =
    [
        new(PdfExtract, 1,
            new RemoteJobLimits(1, 1000, 2048, 256, 120, 300, 104_857_600, 8_388_608, 0, 0),
            RemoteJobFlagKeys.PdfExtract),
        new(CompanionIndexPrep, 1,
            new RemoteJobLimits(1, 1000, 2048, 256, 120, 300, 104_857_600, 8_388_608, 0, 0),
            RemoteJobFlagKeys.CompanionIndexPrep),
        new(MediaAudioExtract, 1,
            new RemoteJobLimits(2, 2000, 1024, 1536, 900, 1800, 805_306_368, 262_144, 134_217_728, 64),
            RemoteJobFlagKeys.MediaAudioExtract),
        new(MediaSpeakingJoin, 1,
            new RemoteJobLimits(1, 1000, 512, 192, 240, 420, 67_108_864, 16_384, 4_194_304, 1),
            RemoteJobFlagKeys.MediaSpeakingJoin),
    ];

    public static RemoteKindSpec? Find(string? kind)
        => kind is null ? null : All.FirstOrDefault(spec => string.Equals(spec.Kind, kind, StringComparison.Ordinal));

    public static IReadOnlyList<string> Names => All.Select(spec => spec.Kind).ToArray();

    /// <summary>Limits for a kind and purpose (a shadow run has a smaller result cap).</summary>
    public static RemoteJobLimits LimitsFor(RemoteKindSpec spec, string purpose)
        => purpose == RemoteJobPurpose.Shadow && spec.Kind == PdfExtract
            ? spec.Limits with { MaxResultBytes = ShadowMaxResultBytes }
            : spec.Limits;

    /// <summary>The feature-flag key that gates <paramref name="kind"/> for <paramref name="purpose"/>; null for canary (master only).</summary>
    public static string? FlagKeyFor(string kind, string purpose)
    {
        if (purpose == RemoteJobPurpose.Canary) return null;
        if (kind == PdfExtract && purpose == RemoteJobPurpose.Shadow) return RemoteJobFlagKeys.PdfExtractShadow;
        return Find(kind)?.ApplyFlagKey;
    }

    /// <summary>
    /// The pinned engine version offered for <paramref name="kind"/>: the configured override, else the
    /// computed one for the PDF kinds, else null (media kinds stay disabled until an operator pins one).
    /// </summary>
    public static string? EngineVersion(string kind, RemoteJobsOptions options)
    {
        var configured = options.EngineVersionOverride(kind);
        if (configured is not null) return configured;

        return kind switch
        {
            PdfExtract => PdfTextEngine.EngineVersion,
            CompanionIndexPrep => PdfTextEngine.EngineVersion + "/" + CompanionChunker.Version,
            _ => null,
        };
    }
}
