using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class CursorPaginationTests
{
    [Fact]
    public void NormalizeLimit_returns_default_when_null()
    {
        Assert.Equal(CursorPagination.DefaultLimit, CursorPagination.NormalizeLimit(null));
    }

    [Fact]
    public void NormalizeLimit_returns_default_when_non_positive()
    {
        Assert.Equal(CursorPagination.DefaultLimit, CursorPagination.NormalizeLimit(0));
        Assert.Equal(CursorPagination.DefaultLimit, CursorPagination.NormalizeLimit(-5));
    }

    [Fact]
    public void NormalizeLimit_clamps_to_max()
    {
        Assert.Equal(CursorPagination.MaxLimit, CursorPagination.NormalizeLimit(10_000));
    }

    [Fact]
    public void NormalizeLimit_passes_through_valid_values()
    {
        Assert.Equal(25, CursorPagination.NormalizeLimit(25));
    }

    [Fact]
    public void Encode_and_decode_roundtrip_preserves_values()
    {
        var ts = new DateTimeOffset(2026, 4, 23, 12, 30, 45, TimeSpan.Zero);
        const string id = "attempt_abc-123";
        var cursor = CursorPagination.Encode(ts, id);

        Assert.True(CursorPagination.TryDecode(cursor, out var decoded));
        Assert.Equal(ts, decoded.Timestamp);
        Assert.Equal(id, decoded.Id);
    }

    [Fact]
    public void Encode_is_url_safe()
    {
        // Force a payload that would include + and / in vanilla base64.
        var ts = DateTimeOffset.UtcNow;
        var cursor = CursorPagination.Encode(ts, new string('A', 200));

        Assert.DoesNotContain('+', cursor);
        Assert.DoesNotContain('/', cursor);
        Assert.DoesNotContain('=', cursor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-valid-cursor!!!")]
    [InlineData("AAAA")] // valid base64 but not valid JSON
    public void TryDecode_returns_false_for_invalid_input(string? input)
    {
        Assert.False(CursorPagination.TryDecode(input, out _));
    }
}
