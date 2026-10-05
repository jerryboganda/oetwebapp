using System.Text.RegularExpressions;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Web;

/// <summary>
/// Static guarantees about the owner console's markup, page models and script (OET-RWP/1 section 8.8, the admin hallmark rules): no raw HTML, no
/// inline script or style, no external resource, an antiforgery token on every form, a step-up on every privileged action, and only fixed notice
/// texts. They read files; nothing is executed.
/// </summary>
public sealed class ConsoleStaticTests
{
    private static string Root => RepoPaths.FleetRoot();

    private static string PagesDirectory => Path.Combine(Root, "src", "Fleet.Manager", "Pages");

    private static string Text(string path) => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static IEnumerable<string> Views() => Directory.EnumerateFiles(PagesDirectory, "*.cshtml", SearchOption.AllDirectories);

    private static IEnumerable<string> PageModels() => Directory.EnumerateFiles(PagesDirectory, "*.cshtml.cs", SearchOption.AllDirectories);

    private static IEnumerable<string> ConsoleSources() =>
        PageModels().Concat(Directory.EnumerateFiles(Path.Combine(Root, "src", "Fleet.Manager", "Dashboard"), "*.cs", SearchOption.AllDirectories));

    [Fact]
    public void The_console_has_every_screen_of_the_spec_as_a_page_with_its_view_and_model()
    {
        foreach (var page in new[]
                 {
                     "Index", "Health", "Workloads", "Policies", "Credentials", "Projects", "Hosts/Add", "Hosts/Detail", "Operations/Index", "Operations/Detail",
                 })
        {
            Assert.True(File.Exists(Path.Combine(PagesDirectory, page + ".cshtml")), page + ".cshtml is missing");
            Assert.True(File.Exists(Path.Combine(PagesDirectory, page + ".cshtml.cs")), page + ".cshtml.cs is missing");
        }

        foreach (var shared in new[] { "_Layout", "_Messages", "_StepUp", "_StepUpInline", "_KeyFields", "_PolicyFields", "_FleetBody", "_HostLive", "_OperationLive", "_OperationsTable", "_WorkloadsBody", "_ProjectsBody" })
        {
            Assert.True(File.Exists(Path.Combine(PagesDirectory, "Shared", shared + ".cshtml")), shared + ".cshtml is missing");
        }

        Assert.True(File.Exists(Path.Combine(Root, "src", "Fleet.Manager", "wwwroot", "js", "console.js")));
        Assert.True(File.Exists(Path.Combine(Root, "src", "Fleet.Manager", "wwwroot", "css", "console.css")));
    }

    [Fact]
    public void Nothing_in_the_console_writes_raw_html_so_everything_a_helper_says_goes_through_the_encoder()
    {
        foreach (var path in Views().Concat(ConsoleSources()))
        {
            var text = Text(path);
            foreach (var forbidden in new[] { "Html.Raw", "HtmlString", "IHtmlContent", "HtmlContentBuilder", "WriteLiteral(", "MarkupString", "@Html.Raw" })
            {
                Assert.False(text.Contains(forbidden, StringComparison.Ordinal), Relative(path) + " uses " + forbidden);
            }
        }
    }

    [Fact]
    public void Views_carry_no_inline_script_no_inline_style_no_event_handler_and_no_external_resource()
    {
        foreach (var path in Views())
        {
            var text = Text(path);
            var name = Relative(path);

            Assert.False(Regex.IsMatch(text, "<script(?![^>]*\\ssrc=)", RegexOptions.IgnoreCase), name + " has an inline script");
            Assert.False(Regex.IsMatch(text, "<[a-zA-Z][^>]*\\sstyle\\s*=", RegexOptions.IgnoreCase), name + " has an inline style");
            Assert.False(Regex.IsMatch(text, "<[a-zA-Z][^>]*\\son[a-z]+\\s*=", RegexOptions.IgnoreCase), name + " has an inline event handler");
            Assert.False(Regex.IsMatch(text, "(src|href|action)\\s*=\\s*\"https?://", RegexOptions.IgnoreCase), name + " loads or links an external address");
            Assert.DoesNotContain("javascript:", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<style", text, StringComparison.OrdinalIgnoreCase);
        }

        var scripts = Views().Where(p => Regex.IsMatch(Text(p), "<script", RegexOptions.IgnoreCase)).Select(Relative).ToList();
        Assert.Equal(new[] { "src/Fleet.Manager/Pages/Shared/_Layout.cshtml" }, scripts.ToArray());
    }

    [Fact]
    public void The_stylesheet_is_self_contained_and_the_script_talks_only_to_its_own_origin()
    {
        var css = Text(Path.Combine(Root, "src", "Fleet.Manager", "wwwroot", "css", "console.css"));
        var js = Text(Path.Combine(Root, "src", "Fleet.Manager", "wwwroot", "js", "console.js"));

        Assert.DoesNotContain("@import", css);
        Assert.DoesNotContain("url(", css);
        Assert.DoesNotContain("http://", css);
        Assert.DoesNotContain("https://", css);
        Assert.Contains("prefers-color-scheme: dark", css);
        Assert.Contains("prefers-reduced-motion", css);
        Assert.Contains(":focus-visible", css);
        Assert.DoesNotContain("http://", js);
        Assert.DoesNotContain("https://", js);
        Assert.DoesNotContain("import(", js);
        Assert.StartsWith("/*", js.TrimStart());
    }

    [Fact]
    public void Every_form_that_posts_carries_an_antiforgery_token_and_no_form_posts_to_another_host()
    {
        var forms = 0;
        foreach (var path in Views())
        {
            foreach (Match form in Regex.Matches(Text(path), "<form\\b[^>]*>", RegexOptions.Singleline))
            {
                forms++;
                var tag = form.Value;
                if (Regex.IsMatch(tag, "method\\s*=\\s*\"post\"", RegexOptions.IgnoreCase))
                {
                    Assert.True(tag.Contains("asp-antiforgery=\"true\"", StringComparison.Ordinal), Relative(path) + ": a post form without asp-antiforgery=\"true\": " + tag);
                }

                Assert.False(Regex.IsMatch(tag, "action\\s*=\\s*\"(https?:)?//", RegexOptions.IgnoreCase), Relative(path) + ": a form posts to another host");
            }
        }

        Assert.True(forms >= 20, "expected the console to have many forms, found " + forms);
    }

    [Fact]
    public void Every_secret_field_is_not_remembered_by_the_browser_and_is_marked_so_the_script_empties_it()
    {
        var keyFields = Text(Path.Combine(PagesDirectory, "Shared", "_KeyFields.cshtml"));

        Assert.Matches("<textarea[^>]*name=\"PrivateKey\"[^>]*autocomplete=\"off\"[^>]*data-secret=\"true\"", keyFields);
        Assert.Matches("<input[^>]*name=\"Passphrase\"[^>]*type=\"password\"[^>]*data-secret=\"true\"", keyFields);
        Assert.DoesNotContain("@Model.PrivateKey", keyFields);
        Assert.DoesNotContain("@Model.Passphrase", keyFields);

        foreach (var path in Views())
        {
            var text = Text(path);
            Assert.DoesNotMatch("value=\"@[A-Za-z.]*(PrivateKey|Passphrase|Code)\\b", text);
            Assert.DoesNotContain("@Model.Code", text);
        }

        var login = Text(Path.Combine(PagesDirectory, "Login.cshtml"));
        Assert.Contains("name=\"Password\" type=\"password\" autocomplete=\"off\"", login);
    }

    [Fact]
    public void Every_post_handler_of_the_console_is_a_member_of_a_console_page_and_asks_for_the_step_up_unless_it_is_in_the_short_allow_list()
    {
        var withoutStepUp = new[] { "Login.cshtml.cs", "Logout.cshtml.cs" };
        var checkedFiles = 0;
        foreach (var path in PageModels().Where(p => Text(p).Contains("OnPost", StringComparison.Ordinal)))
        {
            var name = Path.GetFileName(path);
            if (withoutStepUp.Contains(name))
            {
                continue;
            }

            checkedFiles++;
            var text = Text(path);
            Assert.True(Regex.IsMatch(text, "class \\w+ : FleetPageModel"), Relative(path) + " is not a FleetPageModel");

            // The root index only refreshes (read-only towards the helpers); every other page asks for a fresh authenticator code.
            if (Relative(path) != "src/Fleet.Manager/Pages/Index.cshtml.cs")
            {
                Assert.Contains("AuthorizeAsync(", text);
            }
        }

        Assert.True(checkedFiles >= 6, "found only " + checkedFiles + " page models with post handlers");
    }

    [Fact]
    public void The_only_handlers_that_need_no_authenticator_code_are_the_read_only_and_the_non_privileged_ones_the_json_api_also_leaves_open()
    {
        // OET-RWP/1 section 8.8 and the JSON API: retry and cancel of an operation, the display-only first step of a re-pin, and the refresh need no
        // step-up. Everything else in a page model must reach AuthorizeAsync before it changes anything.
        var detail = Text(Path.Combine(PagesDirectory, "Operations", "Detail.cshtml.cs"));
        var retry = Slice(detail, "OnPostRetryAsync", "OnPostCancelAsync");
        var cancel = Slice(detail, "OnPostCancelAsync", "private static string PathOf");
        Assert.DoesNotContain("AuthorizeAsync", retry);
        Assert.DoesNotContain("AuthorizeAsync", cancel);
        foreach (var privileged in new[] { "OnPostConfirmKeyAsync", "OnPostKeyAsync", "OnPostContinueAsync" })
        {
            var start = detail.IndexOf(privileged, StringComparison.Ordinal);
            Assert.True(start > 0, privileged + " is missing");
            Assert.Contains("AuthorizeAsync", detail[start..Math.Min(detail.Length, start + 6000)]);
        }

        var host = Text(Path.Combine(PagesDirectory, "Hosts", "Detail.cshtml.cs"));
        Assert.DoesNotContain("AuthorizeAsync", Slice(host, "OnPostRepinStartAsync", "OnPostRepinConfirmAsync"));
        foreach (var privileged in new[] { "public async Task<IActionResult> OnPostLimitsAsync", "public async Task<IActionResult> OnPostRepinConfirmAsync", "public async Task<IActionResult> OnPostRepairAsync" })
        {
            var start = host.IndexOf(privileged, StringComparison.Ordinal);
            Assert.True(start > 0, privileged + " is missing");
            Assert.Contains("AuthorizeAsync", host[start..Math.Min(host.Length, start + 6000)]);
        }

        Assert.Contains("ActAsync", Slice(host, "public Task<IActionResult> OnPostDrainAsync", "public Task<IActionResult> OnPostResumeAsync"));
        Assert.Contains("AuthorizeAsync", Slice(host, "private async Task<IActionResult> ActAsync", "private async Task<IActionResult> RenderAsync"));
    }

    [Fact]
    public void Notices_are_only_ever_codes_from_the_fixed_table_and_every_code_a_page_redirects_with_exists_in_it()
    {
        var table = Text(Path.Combine(PagesDirectory, "FleetPageModel.cs"));
        var known = Regex.Matches(table, "\\[\"(?<code>[a-z][a-z-]*)\"\\]\\s*=").Select(m => m.Groups["code"].Value).ToHashSet(StringComparer.Ordinal);
        Assert.True(known.Count >= 25, "the notice table looks empty");

        var used = 0;
        foreach (var path in PageModels().Where(p => !Path.GetFileName(p).Equals("FleetPageModel.cs", StringComparison.Ordinal)))
        {
            foreach (Match statement in Regex.Matches(Text(path), "(Done|ActAsync)\\([^;]*;", RegexOptions.Singleline))
            {
                foreach (Match literal in Regex.Matches(statement.Value, "\"(?<code>[a-z][a-z-]+)\""))
                {
                    used++;
                    Assert.True(known.Contains(literal.Groups["code"].Value), Relative(path) + " redirects with an unknown notice '" + literal.Groups["code"].Value + "'");
                }
            }
        }

        Assert.True(used >= 25, "found only " + used + " notices in use");
        Assert.DoesNotContain("TempData", table);
    }

    [Fact]
    public void Redirects_after_a_post_are_local_and_built_from_constants_and_escaped_identifiers_only()
    {
        foreach (var path in PageModels())
        {
            var text = Text(path);
            Assert.DoesNotContain("Redirect(Request", text);
            Assert.DoesNotContain("returnUrl", text, StringComparison.OrdinalIgnoreCase);
            Assert.False(Regex.IsMatch(text, "(?<![A-Za-z])Redirect\\("), Relative(path) + " uses an open redirect");
        }

        var model = Text(Path.Combine(PagesDirectory, "FleetPageModel.cs"));
        Assert.Contains("LocalRedirect(", model);
        Assert.Contains("Uri.EscapeDataString", model);
    }

    [Fact]
    public void The_console_never_writes_to_the_console_or_the_log_with_interpolated_text_and_reads_no_environment()
    {
        foreach (var path in ConsoleSources())
        {
            var text = Text(path);
            var name = Relative(path);

            Assert.False(Regex.IsMatch(text, "Console\\.(Error\\.)?Write"), name + " writes to the console");
            Assert.False(Regex.IsMatch(text, "\\.Log(Trace|Debug|Information|Warning|Error|Critical)\\(\\s*\\$\""), name + " logs an interpolated string");
            Assert.DoesNotContain("GetEnvironmentVariable", text);
            Assert.DoesNotContain("ProcessStartInfo", text);
            Assert.DoesNotContain("Process.Start(", text);
            Assert.DoesNotContain("HttpClient", text);
        }
    }

    [Fact]
    public void Only_the_sign_in_page_is_open_to_a_visitor_and_the_rest_fall_under_the_authenticated_fallback_policy()
    {
        var registration = Text(Path.Combine(Root, "src", "Fleet.Manager", "Hosting", "ServiceRegistration.cs"));

        Assert.Equal(1, Regex.Matches(registration, "AllowAnonymousToPage").Count);
        Assert.Contains("AllowAnonymousToPage(\"/Login\")", registration);
        Assert.Contains("FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()", registration);
        foreach (var path in PageModels())
        {
            Assert.DoesNotContain("[AllowAnonymous]", Text(path));
        }
    }

    [Fact]
    public void Every_page_sets_its_title_and_every_data_table_is_captioned_and_scoped_for_screen_readers()
    {
        var titled = 0;
        foreach (var path in Views().Where(p => Text(p).StartsWith("@page", StringComparison.Ordinal) && !Path.GetFileName(p).Equals("Login.cshtml", StringComparison.Ordinal) && !Path.GetFileName(p).Equals("Logout.cshtml", StringComparison.Ordinal)))
        {
            titled++;
            Assert.Contains("ViewData[\"Title\"]", Text(path));
        }

        Assert.Equal(10, titled);

        foreach (var path in Views())
        {
            var text = Text(path);
            var tables = Regex.Matches(text, "<table\\b").Count;
            if (tables == 0)
            {
                continue;
            }

            Assert.True(Regex.Matches(text, "<caption\\b").Count >= tables, Relative(path) + " has a table without a caption");
            Assert.True(Regex.Matches(text, "scope=\"(col|row)\"").Count >= tables, Relative(path) + " has a table without header cells");
        }

        var layout = Text(Path.Combine(PagesDirectory, "Shared", "_Layout.cshtml"));
        Assert.Contains("class=\"skip-link\"", layout);
        Assert.Contains("<main id=\"main\"", layout);
        Assert.Contains("aria-label=\"Console sections\"", layout);
        Assert.Contains("aria-current=", layout);
        Assert.Contains("lang=\"en\"", layout);
        Assert.Contains("name=\"viewport\"", layout);
    }

    [Fact]
    public void The_readme_describes_the_dashboard_screens_and_its_security_rules()
    {
        var readme = Text(Path.Combine(Root, "README.md"));

        foreach (var heading in new[] { "## The owner console (dashboard)" })
        {
            Assert.Contains(heading, readme);
        }

        foreach (var word in new[] { "Fleet overview", "Add helper", "Workloads", "Policies", "Operations", "Credentials", "Projects", "write-only", "antiforgery", "nonce", "SSE" })
        {
            Assert.Contains(word, readme);
        }
    }

    private static string Slice(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, from + " is missing");
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, to + " is missing after " + from);
        return text[start..end];
    }
}
