using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public class MagicByteValidatorTests
{
    private readonly MagicByteValidator _v = new();

    private static Stream Bytes(params byte[] bs) => new MemoryStream(bs);

    [Fact]
    public async Task Accepts_PDF_when_extension_matches()
    {
        var r = await _v.ValidateAsync(
            Bytes(0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x0A, 0x0A, 0x0A),
            "pdf", default);
        Assert.True(r.Accepted);
        Assert.Equal("application/pdf", r.DetectedMime);
    }

    [Fact]
    public async Task Rejects_PDF_when_declared_mp3()
    {
        var r = await _v.ValidateAsync(
            Bytes(0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x0A, 0x0A, 0x0A),
            "mp3", default);
        Assert.False(r.Accepted);
    }

    [Fact]
    public async Task Accepts_MP3_ID3_header()
    {
        var r = await _v.ValidateAsync(
            Bytes(0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00),
            "mp3", default);
        Assert.True(r.Accepted);
        Assert.Equal("audio/mpeg", r.DetectedMime);
    }

    [Fact]
    public async Task Accepts_MP3_framesync_header()
    {
        var r = await _v.ValidateAsync(
            Bytes(0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00),
            "mp3", default);
        Assert.True(r.Accepted);
    }

    [Fact]
    public async Task Rejects_random_bytes()
    {
        var r = await _v.ValidateAsync(
            Bytes(0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B),
            "pdf", default);
        Assert.False(r.Accepted);
        Assert.Equal("Unrecognised file format.", r.Reason);
    }

    [Fact]
    public async Task Rejects_empty_file()
    {
        var r = await _v.ValidateAsync(new MemoryStream(), "pdf", default);
        Assert.False(r.Accepted);
    }

    [Fact]
    public async Task Accepts_PNG_when_declared_png()
    {
        var r = await _v.ValidateAsync(
            Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D),
            "png", default);
        Assert.True(r.Accepted);
    }

    [Fact]
    public async Task Accepts_ZIP_for_bulk_import()
    {
        var r = await _v.ValidateAsync(
            Bytes(0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00),
            "zip", default);
        Assert.True(r.Accepted);
    }

    [Fact]
    public async Task Rewinds_seekable_stream_after_peek()
    {
        var stream = new MemoryStream(
            [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x0A, 0x0A, 0x0A, 0x0A, 0x0A]);
        await _v.ValidateAsync(stream, "pdf", default);
        Assert.Equal(0, stream.Position);
    }
}
