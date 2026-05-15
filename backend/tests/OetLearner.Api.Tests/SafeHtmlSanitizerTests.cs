using OetLearner.Api.Security;
using Xunit;

namespace OetLearner.Api.Tests;

public sealed class SafeHtmlSanitizerTests
{
    [Fact]
    public void SanitizeLimitedHtml_StripsScriptBlocksAndDangerousAttributes()
    {
        var html = "<p onclick=\"alert(1)\" style=\"position:absolute\">Safe</p><script>alert('xss')</script>";

        var sanitized = SafeHtmlSanitizer.SanitizeLimitedHtml(html);

        Assert.Equal("<p>Safe</p>", sanitized);
    }

    [Fact]
    public void SanitizeLimitedHtml_StripsDangerousUrlsAndEmbeds()
    {
        var html = "<a href=\"javascript:alert(1)\">go</a><iframe src=\"https://evil.example\"></iframe><img src=\"data:text/html;base64,abc\">";

        var sanitized = SafeHtmlSanitizer.SanitizeLimitedHtml(html);

        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:text/html", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<a>go</a>", sanitized);
    }

    [Fact]
    public void SanitizeLimitedHtml_PreservesBasicFormatting()
    {
        const string html = "<p><strong>Round your lips</strong> and repeat <em>three</em> times.</p>";

        var sanitized = SafeHtmlSanitizer.SanitizeLimitedHtml(html);

        Assert.Equal(html, sanitized);
    }
}
