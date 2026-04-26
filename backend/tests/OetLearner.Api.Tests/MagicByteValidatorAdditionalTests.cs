using OetLearner.Api.Services.Content;
using Xunit;

namespace OetLearner.Api.Tests;

public class MagicByteValidatorAdditionalTests
{
    private static MemoryStream Stream(byte[] bytes) => new MemoryStream(bytes);

    private static byte[] Pad(byte[] header, int totalLength = 16)
    {
        if (header.Length >= totalLength) return header;
        var padded = new byte[totalLength];
        Array.Copy(header, padded, header.Length);
        return padded;
    }

    [Theory]
    [InlineData("m4a")]
    [InlineData("mp4")]
    public async Task MP4_M4A_ftyp_box_is_accepted_for_matching_extension(string ext)
    {
        var bytes = Pad([0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0]);
        var r = await new MagicByteValidator().ValidateAsync(Stream(bytes), ext, default);
        Assert.True(r.Accepted);
        Assert.Equal("audio/mp4", r.DetectedMime);
    }

    [Fact]
    public async Task MP4_with_wrong_extension_is_rejected()
    {
        var bytes = Pad([0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0]);
        var r = await new MagicByteValidator().ValidateAsync(Stream(bytes), "mp3", default);
        Assert.False(r.Accepted);
    }

    [Fact]
    public async Task WAV_RIFF_header_with_wav_extension_is_accepted()
    {
        var bytes = Pad([0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45]);
        var r = await new MagicByteValidator().ValidateAsync(Stream(bytes), "wav", default);
        Assert.True(r.Accepted);
        Assert.Equal("audio/wav", r.DetectedMime);
    }

    [Fact]
    public async Task OGG_header_with_ogg_extension_is_accepted()
    {
        var bytes = Pad([0x4F, 0x67, 0x67, 0x53]);
        var r = await new MagicByteValidator().ValidateAsync(Stream(bytes), "ogg", default);
        Assert.True(r.Accepted);
        Assert.Equal("audio/ogg", r.DetectedMime);
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    public async Task JPEG_header_is_accepted_for_jpg_or_jpeg_extension(string ext)
    {
        var bytes = Pad([0xFF, 0xD8, 0xFF, 0xE0]);
        var r = await new MagicByteValidator().ValidateAsync(Stream(bytes), ext, default);
        Assert.True(r.Accepted);
        Assert.Equal("image/jpeg", r.DetectedMime);
    }

    [Theory]
    [InlineData((byte)0x37)]
    [InlineData((byte)0x39)]
    public async Task GIF_header_with_gif_extension_is_accepted(byte version)
    {
        var bytes = Pad([0x47, 0x49, 0x46, 0x38, version, 0x61]);
        var r = await new MagicByteValidator().ValidateAsync(Stream(bytes), "gif", default);
        Assert.True(r.Accepted);
        Assert.Equal("image/gif", r.DetectedMime);
    }

    [Fact]
    public async Task WebP_header_with_webp_extension_is_accepted()
    {
        var bytes = Pad([0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50]);
        var r = await new MagicByteValidator().ValidateAsync(Stream(bytes), "webp", default);
        Assert.True(r.Accepted);
        Assert.Equal("image/webp", r.DetectedMime);
    }

    [Fact]
    public async Task File_too_short_is_rejected()
    {
        var r = await new MagicByteValidator().ValidateAsync(Stream([0x00, 0x01]), "pdf", default);
        Assert.False(r.Accepted);
        Assert.Contains("too short", r.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Extension_with_leading_dot_is_normalised()
    {
        var bytes = Pad([0x25, 0x50, 0x44, 0x46, 0x2D]);
        var r = await new MagicByteValidator().ValidateAsync(Stream(bytes), ".pdf", default);
        Assert.True(r.Accepted);
    }

    [Fact]
    public async Task Extension_is_case_insensitive()
    {
        var bytes = Pad([0x25, 0x50, 0x44, 0x46, 0x2D]);
        var r = await new MagicByteValidator().ValidateAsync(Stream(bytes), "PDF", default);
        Assert.True(r.Accepted);
    }

    [Fact]
    public async Task Stream_position_is_preserved_when_seekable()
    {
        var bytes = Pad([0x89, 0x50, 0x4E, 0x47]);
        var stream = new MemoryStream(bytes);
        stream.Position = 0;
        await new MagicByteValidator().ValidateAsync(stream, "png", default);
        Assert.Equal(0, stream.Position);
    }
}

public class NoOpUploadScannerTests
{
    private sealed class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Warnings { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task ScanAsync_always_returns_clean_true()
    {
        var logger = new TestLogger<NoOpUploadScanner>();
        var scanner = new NoOpUploadScanner(logger);
        var result = await scanner.ScanAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "file.bin", default);
        Assert.True(result.clean);
        Assert.Null(result.reason);
    }

    [Fact]
    public async Task ScanAsync_logs_warning_only_once()
    {
        var logger = new TestLogger<NoOpUploadScanner>();
        var scanner = new NoOpUploadScanner(logger);
        await scanner.ScanAsync(new MemoryStream(), "a", default);
        await scanner.ScanAsync(new MemoryStream(), "b", default);
        await scanner.ScanAsync(new MemoryStream(), "c", default);
        Assert.Single(logger.Warnings);
    }
}
