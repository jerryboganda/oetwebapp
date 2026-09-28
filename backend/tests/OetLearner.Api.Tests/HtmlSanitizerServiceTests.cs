using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public class HtmlSanitizerServiceTests
{
    [Fact]
    public void SanitizePassage_StripsScriptHandlersStylesAndUnsafeLinks()
    {
        var sanitizer = new HtmlSanitizerService();

        var html = """
            <p onclick="alert(1)" style="background:url(javascript:alert(1))">Clinical note</p>
            <script>alert(1)</script>
            <a href="javascript:alert(1)" target="_self">unsafe</a>
            <a href="https://example.com/resource">safe</a>
            """;

        var sanitized = sanitizer.SanitizePassage(html);

        Assert.DoesNotContain("<script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Clinical note</p>", sanitized);
        Assert.Contains("href=\"https://example.com/resource\"", sanitized);
        Assert.Contains("rel=\"noopener noreferrer\"", sanitized);
    }

    // Moved from the deleted regex SafeHtmlSanitizer (pronunciation TipsHtml path).
    [Fact]
    public void SanitizePassageHtml_StripsScriptBlocksAndDangerousAttributes()
    {
        var html = "<p onclick=\"alert(1)\" style=\"position:absolute\">Safe</p><script>alert('xss')</script>";

        var sanitized = HtmlSanitizerService.SanitizePassageHtml(html);

        Assert.Equal("<p>Safe</p>", sanitized);
    }

    [Fact]
    public void SanitizePassageHtml_StripsDangerousUrlsAndEmbeds()
    {
        var html = "<a href=\"javascript:alert(1)\">go</a><iframe src=\"https://evil.example\"></iframe><img src=\"data:text/html;base64,abc\">";

        var sanitized = HtmlSanitizerService.SanitizePassageHtml(html);

        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:text/html", sanitized, StringComparison.OrdinalIgnoreCase);
        // Ganss drops the disallowed href attribute entirely.
        Assert.Contains("<a>go</a>", sanitized);
    }

    [Fact]
    public void SanitizePassageHtml_PreservesBasicFormatting()
    {
        const string html = "<p><strong>Round your lips</strong> and repeat <em>three</em> times.</p>";

        var sanitized = HtmlSanitizerService.SanitizePassageHtml(html);

        Assert.Equal(html, sanitized);
    }

    // Payloads the old regex sanitizer let through unchanged.
    [Theory]
    [InlineData("<img/src=x/onerror=alert(1)>", "onerror")]
    [InlineData("<svg/onload=alert(1)>", "onload")]
    [InlineData("<a href=\"jav&#x61;script:alert(1)\">x</a>", "script:")]
    public void SanitizePassageHtml_BlocksRegexBypassPayloads(string html, string forbidden)
    {
        var sanitized = HtmlSanitizerService.SanitizePassageHtml(html);

        Assert.DoesNotContain(forbidden, sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", sanitized, StringComparison.OrdinalIgnoreCase);
    }
}
