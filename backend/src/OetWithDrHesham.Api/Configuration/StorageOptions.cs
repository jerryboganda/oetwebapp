namespace OetWithDrHesham.Api.Configuration;

public sealed class StorageOptions
{
    public string LocalRootPath { get; set; } = "App_Data/storage";
    public long MaxUploadBytes { get; set; } = 25L * 1024 * 1024;

    // ── S3 / Object-store (DigitalOcean Spaces, Cloudflare R2, AWS S3) ───────
    // Set Provider = "s3" to activate. All four fields are required when active.
    // Works with any S3-compatible endpoint — pass EndpointUrl for DO Spaces / R2.
    // Leave EndpointUrl empty for standard AWS S3 (regional endpoint auto-resolved).

    /// <summary><c>"local"</c> (default) or <c>"s3"</c>.</summary>
    public string Provider { get; set; } = "local";

    public string? BucketName { get; set; }

    /// <summary>Override endpoint — required for DO Spaces / Cloudflare R2.
    /// E.g. <c>https://ams3.digitaloceanspaces.com</c>. Omit for AWS S3.</summary>
    public string? EndpointUrl { get; set; }

    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }

    /// <summary>AWS region code. Default <c>us-east-1</c>.</summary>
    public string AwsRegion { get; set; } = "us-east-1";

    /// <summary>TTL (seconds) for presigned GET URLs. Default 3600 (1 h).</summary>
    public int SignedReadTtlSeconds { get; set; } = 3600;
    public string[] AllowedAudioContentTypes { get; set; } =
    [
        "audio/webm",
        "audio/ogg",
        "audio/mpeg",
        "audio/mp4",
        "audio/wav",
        "application/octet-stream"
    ];

    // ── Content Upload (Slice 2) ──────────────────────────────────────────
    // Per-role upload limits. Tune per deployment.
    public ContentUploadOptions ContentUpload { get; set; } = new();
}

public sealed class ContentUploadOptions
{
    /// <summary>Max bytes accepted for audio assets (Listening MP3/WAV).</summary>
    public long MaxAudioBytes { get; set; } = 350L * 1024 * 1024;        // 350 MB

    /// <summary>Max bytes accepted for PDF/document assets.</summary>
    public long MaxPdfBytes { get; set; } = 250L * 1024 * 1024;          // 250 MB

    /// <summary>Max bytes accepted for image assets (thumbnails, stamps).</summary>
    public long MaxImageBytes { get; set; } = 5L * 1024 * 1024;          // 5 MB

    /// <summary>Max bytes accepted for ZIP bulk imports (Slice 5).</summary>
    public long MaxZipBytes { get; set; } = 500L * 1024 * 1024;          // 500 MB

    /// <summary>Max files accepted inside one ZIP bulk import.</summary>
    public int MaxZipEntries { get; set; } = 5000;

    /// <summary>Max uncompressed bytes accepted for one ZIP entry.</summary>
    public long MaxZipEntryBytes { get; set; } = 150L * 1024 * 1024;      // 150 MB

    /// <summary>Max total uncompressed bytes accepted across a ZIP import.</summary>
    public long MaxZipUncompressedBytes { get; set; } = 2L * 1024 * 1024 * 1024; // 2 GB

    /// <summary>Max allowed uncompressed/compressed ratio for a ZIP entry.</summary>
    public double MaxZipCompressionRatio { get; set; } = 100;

    /// <summary>Per-chunk upload size.</summary>
    public long ChunkSizeBytes { get; set; } = 8L * 1024 * 1024;         // 8 MB

    /// <summary>Hours before an incomplete staging upload is cleaned up.</summary>
    public int StagingTtlHours { get; set; } = 24;

    /// <summary>Relative sub-path under LocalRootPath where staging parts live.</summary>
    public string StagingSubpath { get; set; } = "uploads/staging";

    /// <summary>Relative sub-path where content-addressed published files live.</summary>
    public string PublishedSubpath { get; set; } = "uploads/published";

    /// <summary>Relative sub-path for derived artefacts (thumbnails, extracted text, etc.).</summary>
    public string DerivedSubpath { get; set; } = "uploads/derived";
}
