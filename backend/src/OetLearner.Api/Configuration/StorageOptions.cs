namespace OetLearner.Api.Configuration;

public sealed class StorageOptions
{
    public string LocalRootPath { get; set; } = "App_Data/storage";
    public long MaxUploadBytes { get; set; } = 25L * 1024 * 1024;
    public string[] AllowedAudioContentTypes { get; set; } =
    [
        "audio/webm",
        "audio/ogg",
        "audio/mpeg",
        "audio/mp4",
        "audio/wav",
        "application/octet-stream"
    ];
}
