using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class AuthEmailAddressTests
{
    [Theory]
    [InlineData("user@example.com", "user@example.com")]
    [InlineData("  user@example.com  ", "user@example.com")]
    [InlineData("\tuser@example.com\n", "user@example.com")]
    public void TrimAndValidateOrThrow_returns_trimmed_email_for_valid_input(string input, string expected)
    {
        Assert.Equal(expected, AuthEmailAddress.TrimAndValidateOrThrow(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void TrimAndValidateOrThrow_throws_when_empty_or_whitespace(string input)
    {
        var ex = Assert.Throws<ApiException>(() => AuthEmailAddress.TrimAndValidateOrThrow(input));
        Assert.Equal("email_required", ex.ErrorCode);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("plain string")]
    [InlineData("a@b@c.com")]
    public void TrimAndValidateOrThrow_throws_for_invalid_email(string input)
    {
        var ex = Assert.Throws<ApiException>(() => AuthEmailAddress.TrimAndValidateOrThrow(input));
        Assert.Equal("invalid_email", ex.ErrorCode);
    }

    [Fact]
    public void NormalizeOrThrow_uppercases_and_trims()
    {
        Assert.Equal("USER@EXAMPLE.COM", AuthEmailAddress.NormalizeOrThrow("  User@Example.com  "));
    }

    [Fact]
    public void NormalizeOrThrow_throws_for_empty_input()
    {
        Assert.Throws<ApiException>(() => AuthEmailAddress.NormalizeOrThrow(""));
    }

    [Fact]
    public void NormalizeOrThrow_throws_for_invalid_input()
    {
        Assert.Throws<ApiException>(() => AuthEmailAddress.NormalizeOrThrow("invalid"));
    }

    [Fact]
    public void Mask_short_local_part_returns_full_mask()
    {
        Assert.Equal("*****@example.com", AuthEmailAddress.Mask("a@example.com"));
    }

    [Fact]
    public void Mask_longer_local_part_keeps_first_char()
    {
        Assert.Equal("u*****@example.com", AuthEmailAddress.Mask("user@example.com"));
    }

    [Fact]
    public void Mask_trims_whitespace_before_masking()
    {
        Assert.Equal("u*****@example.com", AuthEmailAddress.Mask("  user@example.com  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("noatsign")]
    [InlineData("@noLocal.com")]
    [InlineData("noDomain@")]
    public void Mask_returns_default_mask_for_malformed_input(string input)
    {
        Assert.Equal("*****", AuthEmailAddress.Mask(input));
    }

    [Fact]
    public void Mask_handles_email_with_long_local_part()
    {
        Assert.Equal("v*****@example.com", AuthEmailAddress.Mask("verylongusername@example.com"));
    }
}
