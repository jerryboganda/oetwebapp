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
}
