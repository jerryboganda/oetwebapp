using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;
using Fleet.Manager.Vault;
using Microsoft.Extensions.DependencyInjection;

namespace Fleet.Manager.Tests.Web;

/// <summary>
/// The owner console, page by page, over the real application (cookie auth, antiforgery, step-up, Razor Pages, the real services over SQLite and
/// the fake outside world). No browser: what the owner reads and what each form does is judged from the rendered HTML and the state behind it.
/// </summary>
public sealed class ConsolePagesTests : IDisposable
{
    private const string ProtectedKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nPASSPHRASE-PROTECTED\n-----END OPENSSH PRIVATE KEY-----\n"; // secret-scan:allow (fake PEM framing, no key material)

    private ConsoleSession? _session;

    public void Dispose() => _session?.Dispose();

    private async Task<ConsoleSession> StartAsync()
    {
        _session = await ConsoleSession.StartAsync();
        return _session;
    }

    private static Dictionary<string, string> Form(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    private static string IdOf(string location) => location.Split('?')[0].Split('/').Last();

    private static Dictionary<string, string> PolicyFields(int max = 1, int cpu = 1500, int mem = 1536, int tmp = 768, string kind = "pdf.extract")
    {
        string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new Dictionary<string, string>
        {
            ["Input.MaxConcurrency"] = Number(max),
            ["Input.CpuMilli"] = Number(cpu),
            ["Input.MemMiB"] = Number(mem),
            ["Input.TmpMiB"] = Number(tmp),
            ["Input.ReduceCpuPct"] = "80",
            ["Input.ReduceMemFreePct"] = "20",
            ["Input.RestoreCpuPct"] = "60",
            ["Input.RestoreMemFreePct"] = "25",
            ["Input.RestoreAfterSeconds"] = "120",
            ["Input.PollIdle"] = "10",
            ["Input.PollMin"] = "5",
            ["Input.PollMax"] = "30",
            ["Input.Kinds[0].Kind"] = kind,
            ["Input.Kinds[0].Allowed"] = "true",
            ["Input.Kinds[0].Max"] = Number(max),
        };
    }

    private static async Task<string> TextOfAsync(HttpResponseMessage response) =>
        ConsoleSession.Visible(await response.Content.ReadAsStringAsync());

    private async Task<HostEntity> EnrollAndPollAsync(ConsoleSession s)
    {
        await s.Driver.EnrollToActiveAsync();
        await s.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);
        return await s.Driver.HostAsync();
    }

    // ---- who may see what ----------------------------------------------------------------------

    private static readonly string[] ConsoleGets =
    {
        "/", "/?handler=Fragment", "/Hosts/Add", "/Hosts/Detail/some-host", "/Hosts/Detail/some-host?handler=Fragment", "/Operations",
        "/Operations?view=releases", "/Operations?view=audit", "/Operations?handler=Fragment", "/Operations/Detail/some-op",
        "/Operations/Detail/some-op?handler=Fragment", "/Workloads", "/Workloads?handler=Fragment", "/Policies", "/Credentials", "/Projects",
        "/Projects?handler=Fragment", "/Health",
    };

    private static readonly string[] ConsolePosts =
    {
        "/?handler=Refresh", "/Hosts/Add", "/Hosts/Detail/some-host?handler=Run", "/Hosts/Detail/some-host?handler=Remove",
        "/Hosts/Detail/some-host?handler=Limits", "/Hosts/Detail/some-host?handler=RepinConfirm", "/Operations/Detail/some-op?handler=Key",
        "/Operations/Detail/some-op?handler=ConfirmKey", "/Operations/Detail/some-op?handler=Retry", "/Operations/Detail/some-op?handler=Cancel",
        "/Operations?handler=Approve", "/Operations?handler=Rollout", "/Policies?handler=Save", "/Credentials?handler=AddKey",
        "/Credentials?handler=RevokeKey", "/Credentials?handler=RotateToken", "/Credentials?handler=DiscardPullToken",
    };

    [Fact]
    public async Task Every_console_page_fragment_and_form_is_closed_to_a_visitor_without_a_session()
    {
        using var factory = new FleetWebFactory();
        _ = factory.Server;
        using var client = factory.CreateHttps();

        foreach (var path in ConsoleGets)
        {
            using var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.Redirect, "GET " + path + " answered " + response.StatusCode);
            Assert.StartsWith("https://localhost/Login", response.Headers.Location!.ToString());
        }

        foreach (var path in ConsolePosts)
        {
            using var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.True(response.StatusCode == HttpStatusCode.Redirect, "POST " + path + " answered " + response.StatusCode);
            Assert.StartsWith("https://localhost/Login", response.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task Every_form_of_the_console_refuses_a_post_without_the_antiforgery_token_even_with_a_valid_session_and_a_valid_code()
    {
        var s = await StartAsync();
        await EnrollAndPollAsync(s);
        var host = await s.Driver.HostAsync();

        foreach (var path in ConsolePosts.Where(p => !p.Contains("some-", StringComparison.Ordinal)))
        {
            using var response = await s.PostAsync(path, new Dictionary<string, string>(), code: false, antiforgery: false);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, "POST " + path + " answered " + response.StatusCode);
        }

        foreach (var handler in new[] { "Run", "Remove", "Limits", "RepinConfirm" })
        {
            using var response = await s.PostAsync("/Hosts/Detail/" + host.Id + "?handler=" + handler, new Dictionary<string, string>(), code: false, antiforgery: false);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var operations = await s.Get<HostService>().ListOperationsAsync(50, host.Id, CancellationToken.None);
        Assert.DoesNotContain(operations, op => op.Kind != "enroll");
    }

    [Fact]
    public async Task Pages_for_things_that_do_not_exist_are_404_and_say_nothing_about_other_hosts()
    {
        var s = await StartAsync();

        foreach (var path in new[] { "/Hosts/Detail/nope", "/Hosts/Detail/nope?handler=Fragment", "/Operations/Detail/nope", "/Operations/Detail/nope?handler=Fragment" })
        {
            using var response = await s.Client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, path + " answered " + response.StatusCode);
        }

        using var post = await s.PostAsync("/Hosts/Detail/nope?handler=Run", Form(("ActionName", "drain")));
        Assert.True(post.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict, "answered " + post.StatusCode);
    }

    // ---- what every page looks like ---------------------------------------------------------------

    [Fact]
    public async Task Every_page_renders_inside_the_shell_without_inline_script_style_or_event_handlers_and_stays_unframeable()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);
        var operation = (await s.Get<HostService>().ListOperationsAsync(10, host.Id, CancellationToken.None)).Single(op => op.Kind == "enroll");
        var paths = new[]
        {
            "/", "/Hosts/Add", "/Hosts/Detail/" + host.Id, "/Operations", "/Operations?view=releases", "/Operations?view=audit",
            "/Operations?view=audit&verify=true", "/Operations?kind=enroll&state=done", "/Operations/Detail/" + operation.Id, "/Workloads", "/Policies",
            "/Credentials", "/Projects", "/Health",
        };

        foreach (var path in paths)
        {
            using var response = await s.Client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();

            Assert.True(response.StatusCode == HttpStatusCode.OK, path + " answered " + response.StatusCode);
            Assert.Contains("<main id=\"main\"", html);
            Assert.Contains("href=\"/css/console.css\"", html);
            Assert.Contains("src=\"/js/console.js\"", html);
            Assert.Contains("lang=\"en\"", html);
            Assert.Contains("class=\"skip-link\"", html);
            Assert.DoesNotMatch(@"<script(?![^>]*\ssrc=)", html);
            Assert.DoesNotMatch(@"<[^>]*\sstyle\s*=", html);
            Assert.DoesNotMatch(@"<[^>]*\son[a-z]+\s*=", html);
            Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"<(script|link|img|iframe)[^>]*(src|href)=""https?://", html);
            var policy = response.Headers.GetValues("Content-Security-Policy").Single();
            Assert.DoesNotContain("unsafe-inline", policy);
            Assert.Contains("script-src 'self'", policy);
            Assert.Contains("frame-ancestors 'none'", policy);
            Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
            Assert.Contains("no-store", response.Headers.GetValues("Cache-Control").Single());
            Assert.Contains("noindex", html);
        }
    }

    [Fact]
    public async Task The_navigation_names_the_current_section_and_every_screen_of_the_spec_is_reachable_from_it()
    {
        var s = await StartAsync();

        var overview = await s.PageAsync("/");

        foreach (var (href, label) in new[]
                 {
                     ("/", "Fleet"), ("/Hosts/Add", "Add helper"), ("/Workloads", "Workloads"), ("/Policies", "Policies"), ("/Operations", "Operations"),
                     ("/Credentials", "Credentials"), ("/Projects", "Projects"), ("/Health", "Health"),
                 })
        {
            Assert.Matches("<a href=\"" + Regex.Escape(href) + "\"[^>]*>" + Regex.Escape(label) + "</a>", overview);
        }

        Assert.Contains("<a href=\"/\" aria-current=\"page\">Fleet</a>", overview);
        Assert.Contains("<a href=\"/Workloads\" aria-current=\"page\">Workloads</a>", await s.PageAsync("/Workloads"));
        Assert.Contains("<a href=\"/Credentials\" aria-current=\"page\">Credentials</a>", await s.PageAsync("/Credentials"));
        Assert.Contains("Sign out", overview);
        Assert.Contains("OET API", overview);
    }

    [Fact]
    public async Task The_static_assets_are_served_and_the_script_is_the_one_file_the_content_security_policy_allows()
    {
        using var factory = new FleetWebFactory();
        _ = factory.Server;
        using var client = factory.CreateHttps();

        using var script = await client.GetAsync("/js/console.js");
        using var style = await client.GetAsync("/css/console.css");

        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains("javascript", script.Content.Headers.ContentType!.MediaType);
        Assert.Contains("EventSource", await script.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, style.StatusCode);
        Assert.Equal("text/css", style.Content.Headers.ContentType!.MediaType);
        Assert.Contains("prefers-color-scheme: dark", await style.Content.ReadAsStringAsync());
    }

    // ---- overview ------------------------------------------------------------------------------

    [Fact]
    public async Task The_overview_of_an_empty_fleet_invites_the_owner_to_add_a_helper()
    {
        var s = await StartAsync();

        var html = await s.PageAsync("/");
        var text = ConsoleSession.Visible(html);

        Assert.Contains("Fleet overview", text);
        Assert.Contains("No helpers yet", text);
        Assert.Contains("Primary VPS", text);
        Assert.Contains("Headroom for local work", text);
        Assert.Contains("data-live-url=\"/?handler=Fragment\"", html);
        Assert.Contains("data-live-events=", html);
    }

    [Fact]
    public async Task The_overview_lists_the_helper_with_its_state_health_load_capacity_and_latency_and_flags_it_when_it_goes_offline()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);

        var online = ConsoleSession.Visible(await s.PageAsync("/"));

        Assert.Contains("Helper EU 01", online);
        Assert.Contains("203.0.113.10:22", online);
        Assert.Contains("Active", online);
        Assert.Contains("Online", online);
        Assert.Contains("CPU 20%, free memory 80%", online);
        Assert.Contains("slots 0 / 2", online);
        Assert.Contains("4 cores", online);
        Assert.Contains("7.7 GiB RAM", online);
        Assert.Matches("[0-9]+ ms", online);
        Assert.Contains("Nothing needs your attention.", online);
        Assert.Contains("Slots in use", online);

        s.World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!.AutoHeartbeat = false;
        await s.AdvanceAsync(TimeSpan.FromMinutes(15));
        await s.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);

        var offline = ConsoleSession.Visible(await s.PageAsync("/"));
        Assert.Contains("Offline", offline);
        Assert.Contains("Helper EU 01 is offline", offline);
        Assert.Contains("Needs your attention", offline);

        var detail = ConsoleSession.Visible(await s.PageAsync("/Hosts/Detail/" + host.Id));
        Assert.Contains("Health history", detail);
        Assert.Contains("Online", detail);
        Assert.Contains("Offline", detail);
    }

    [Fact]
    public async Task Refresh_now_polls_the_api_at_once_without_a_code_and_a_notice_is_only_ever_one_of_the_fixed_messages()
    {
        var s = await StartAsync();
        await s.Driver.EnrollToActiveAsync();

        var location = await s.PostExpectingRedirectAsync("/?handler=Refresh", code: false);

        Assert.Equal("/?notice=refreshed", location);
        Assert.Contains("Fleet state refreshed from the OET API.", ConsoleSession.Visible(await s.PageAsync(location)));
        Assert.True(s.Get<FleetState>().ApiReachable);

        var reflected = ConsoleSession.Visible(await s.PageAsync("/?notice=%3Cscript%3Ealert(1)%3C/script%3E"));
        Assert.DoesNotContain("alert(1)", reflected);
        Assert.DoesNotContain("Fleet state refreshed", reflected);

        Assert.Contains("Pushed to 3 helper(s); 1 could not be updated", ConsoleSession.Visible(await s.PageAsync("/Policies?notice=policy-saved&n=3&f=1")));
        Assert.Contains("Pushed to 999 helper(s); 0 could not be updated", ConsoleSession.Visible(await s.PageAsync("/Policies?notice=policy-saved&n=1000&f=-4")));
    }

    [Fact]
    public async Task A_live_refresh_does_not_keep_an_idle_session_alive()
    {
        var s = await StartAsync();

        // Only the console script's background polls for 24 minutes: each is answered inside the 20-minute window, but none is the owner doing
        // anything, so the window is never extended and the next request after it is sent to sign in.
        for (var poll = 0; poll < 4; poll++)
        {
            s.Factory.Time.Advance(TimeSpan.FromMinutes(4));
            using var inside = await s.Client.GetAsync("/?handler=Fragment");
            Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
        }

        s.Factory.Time.Advance(TimeSpan.FromMinutes(8));
        using var late = await s.Client.GetAsync("/?handler=Fragment");
        Assert.Equal(HttpStatusCode.Redirect, late.StatusCode);
        Assert.Contains("/Login", late.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_the_owner_opens_does_keep_the_session_alive_past_the_idle_window()
    {
        var s = await StartAsync();

        await s.AdvanceAsync(TimeSpan.FromMinutes(24));

        Assert.Contains("Fleet overview", ConsoleSession.Visible(await s.PageAsync("/")), StringComparison.Ordinal);
    }

    // ---- add a helper --------------------------------------------------------------------------------

    [Fact]
    public async Task The_add_form_asks_for_everything_in_the_spec_with_the_right_defaults_and_labels()
    {
        var s = await StartAsync();

        var html = await s.PageAsync("/Hosts/Add");
        var text = ConsoleSession.Visible(html);

        Assert.Contains("IP address or host name", text);
        Assert.Contains("Name (optional)", text);
        Assert.Contains("SSH user", text);
        Assert.Contains("SSH port", text);
        Assert.Contains("Region (optional)", text);
        Assert.Contains("Provider (optional)", text);
        Assert.Contains("Private key", text);
        Assert.Contains("Or choose the key file", text);
        Assert.Contains("Key passphrase (only if the key has one)", text);
        Assert.Contains("Authenticator code", text);
        Assert.Matches("name=\"SshUser\"[^>]*value=\"root\"|value=\"root\"[^>]*name=\"SshUser\"", html);
        Assert.Matches("name=\"Port\"[^>]*value=\"22\"|value=\"22\"[^>]*name=\"Port\"", html);
        Assert.Matches("<textarea[^>]*name=\"PrivateKey\"[^>]*autocomplete=\"off\"", html);
        Assert.Matches("<input[^>]*name=\"Passphrase\"[^>]*type=\"password\"|<input[^>]*type=\"password\"[^>]*name=\"Passphrase\"", html);
        Assert.Matches("type=\"file\"[^>]*data-fill-target=\"PrivateKey-add\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Equal(1, Regex.Matches(html, "<textarea").Count);
    }

    [Fact]
    public async Task A_form_with_a_problem_is_refused_before_the_authenticator_code_is_used_and_nothing_typed_is_echoed_back()
    {
        var s = await StartAsync();
        var helper = s.World.Provisioner.AddHost("203.0.113.10");
        var marker = helper.OwnerKeyText.Split('\n')[1];
        var code = s.NextCode();
        var bad = Form(
            ("Address", "185.252.233.186"),
            ("Port", "22"),
            ("SshUser", "root"),
            ("PrivateKey", helper.OwnerKeyText),
            ("Passphrase", "my-secret-passphrase-123"));

        using (var refused = await s.PostAsync("/Hosts/Add", bad, explicitCode: code))
        {
            var html = await refused.Content.ReadAsStringAsync();
            var text = ConsoleSession.Visible(html);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains("no authenticator code was used", text);
            Assert.Contains("address", text);
            Assert.DoesNotContain(marker, html);
            Assert.DoesNotContain("my-secret-passphrase-123", html);
        }

        Assert.Empty(await s.Get<HostService>().ListHostsAsync(CancellationToken.None));

        // The very same code still works for a good form: it was never spent.
        var good = Form(("Address", "203.0.113.10"), ("Port", "22"), ("SshUser", "root"), ("Name", "Helper One"));
        using var accepted = await s.PostAsync("/Hosts/Add", good, explicitCode: code);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Single(await s.Get<HostService>().ListHostsAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("Address", "203.0.113.10; rm -rf /", "address")]
    [InlineData("Address", "10.0.0.5", "address")]
    [InlineData("Address", "127.0.0.1", "address")]
    [InlineData("Address", "", "address")]
    [InlineData("Port", "0", "sshPort")]
    [InlineData("Port", "70000", "sshPort")]
    [InlineData("Port", "twenty", "sshPort")]
    [InlineData("SshUser", "Root User", "user")]
    [InlineData("Name", "<script>", "displayName")]
    [InlineData("Region", "eu;central", "region")]
    [InlineData("NodeRef", "UPPER", "nodeRef")]
    public async Task Every_field_is_validated_server_side_and_a_bad_one_creates_nothing(string field, string value, string issueField)
    {
        var s = await StartAsync();
        var form = Form(("Address", "203.0.113.10"), ("Port", "22"), ("SshUser", "root"));
        form[field] = value;

        using var response = await s.PostAsync("/Hosts/Add", form);
        var text = await TextOfAsync(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(issueField, text);
        Assert.Empty(await s.Get<HostService>().ListHostsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Without_the_antiforgery_token_or_with_a_wrong_or_missing_code_nothing_is_added()
    {
        var s = await StartAsync();
        var fields = Form(("Address", "203.0.113.10"), ("Port", "22"), ("SshUser", "root"));

        using (var noToken = await s.PostAsync("/Hosts/Add", fields, antiforgery: false))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        }

        using (var wrongCode = await s.PostAsync("/Hosts/Add", fields, code: false, explicitCode: "000000"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrongCode.StatusCode);
            Assert.Contains("authenticator code was not accepted", await TextOfAsync(wrongCode));
        }

        using (var noCode = await s.PostAsync("/Hosts/Add", fields, code: false))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noCode.StatusCode);
        }

        Assert.Empty(await s.Get<HostService>().ListHostsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_code_works_once_so_a_second_use_of_the_same_code_is_refused()
    {
        var s = await StartAsync();
        var code = s.NextCode();

        using (var first = await s.PostAsync("/Hosts/Add", Form(("Address", "203.0.113.10"), ("Port", "22"), ("SshUser", "root")), explicitCode: code))
        {
            Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        }

        using var replay = await s.PostAsync("/Hosts/Add", Form(("Address", "203.0.113.11"), ("Port", "22"), ("SshUser", "root")), explicitCode: code);
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Single(await s.Get<HostService>().ListHostsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_owner_enrolls_a_helper_through_the_pages_from_nothing_to_active_and_no_secret_is_on_any_page()
    {
        var s = await StartAsync();
        var helper = s.World.Provisioner.AddHost("203.0.113.10");
        var marker = helper.OwnerKeyText.Split('\n')[1];
        var seen = new List<string>();

        // 1. One form: the address, a name and the temporary key (saved encrypted, used only after the host key is pinned).
        var location = await s.PostExpectingRedirectAsync(
            "/Hosts/Add",
            Form(("Address", "203.0.113.10"), ("Name", "Helper EU 01"), ("SshUser", "root"), ("Port", "22"), ("Region", "eu-central"), ("Provider", "ExampleHost"), ("PrivateKey", helper.OwnerKeyText), ("Passphrase", string.Empty)));
        Assert.StartsWith("/Operations/Detail/op_", location);
        Assert.EndsWith("?notice=added-key-saved", location);
        var operationId = IdOf(location);
        var path = "/Operations/Detail/" + operationId;
        var created = await s.PageAsync(location);
        seen.Add(created);
        Assert.Contains("Enrollment progress", ConsoleSession.Visible(created));
        Assert.Contains("SSH key saved", ConsoleSession.Visible(created));
        Assert.Equal(0, helper.ApplyCounts.Values.Sum());

        // 2. The server is scanned; the page shows the fingerprint to compare out of band.
        await s.Driver.RunAsync(operationId);
        var pending = await s.PageAsync(path);
        seen.Add(pending);
        var pendingText = ConsoleSession.Visible(pending);
        Assert.Contains("Check the host key", pendingText);
        Assert.Contains("Waiting for your host-key check", pendingText);
        Assert.Contains(helper.Fingerprint, pendingText);
        Assert.Contains("Do not trust it yet", pendingText);
        Assert.Contains("A temporary SSH key is saved for this server", pendingText);

        // 3. A wrong fingerprint is refused and the operation stays; the right one pins the key and the saved key continues at once.
        using (var wrong = await s.PostAsync(path + "?handler=ConfirmKey", Form(("Fingerprint", "AAAAAAAA"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
            Assert.Contains("do not match", await TextOfAsync(wrong));
        }

        Assert.Equal("HostKeyPending", (await s.Driver.Hosts.GetOperationAsync(operationId, CancellationToken.None)).State);
        var typed = helper.Fingerprint["SHA256:".Length..][..8];
        var confirmed = await s.PostExpectingRedirectAsync(path + "?handler=ConfirmKey", Form(("Fingerprint", typed)));
        Assert.EndsWith("?notice=host-key-confirmed-continuing", confirmed);
        Assert.Equal("Bootstrapping", (await s.Driver.Hosts.GetOperationAsync(operationId, CancellationToken.None)).State);

        // 4. CI supplies the image and its token; the server finishes the job whether or not the browser is open.
        await s.Driver.ApproveReleaseAsync();
        await s.Driver.SyncAsync();
        await s.Driver.RunAsync(operationId);
        var done = await s.PageAsync(path);
        seen.Add(done);
        var doneText = ConsoleSession.Visible(done);
        Assert.Contains("13 of 13 steps finished", doneText);
        Assert.Contains("Activate", doneText);
        Assert.DoesNotMatch("<li class=\"step (running|failed|pending)", done);

        // 5. The helper is on the overview, with its detail page.
        await s.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);
        var host = await s.Get<HostStore>().FindByNodeRefAsync("helper-eu-01", CancellationToken.None);
        Assert.NotNull(host);
        seen.Add(await s.PageAsync("/"));
        var detail = await s.PageAsync("/Hosts/Detail/" + host!.Id);
        seen.Add(detail);
        Assert.Contains("Helper EU 01", ConsoleSession.Visible(detail));
        Assert.Equal("eu-central", host.Region);

        // 6. Nowhere in all of that: the key, its passphrase, the node token, the registry token.
        var nodeToken = s.World.Api.Nodes.Single().Tokens.First().Value;
        foreach (var page in seen)
        {
            Assert.DoesNotContain(marker, page);
            Assert.DoesNotContain(nodeToken, page);
            Assert.DoesNotContain("ghs_TESTONLYTOKEN0123456789abcdef", page);
        }
    }

    [Fact]
    public async Task A_helper_can_be_added_without_a_key_and_the_key_is_given_on_the_enrollment_page_after_the_host_key_check()
    {
        var s = await StartAsync();
        var helper = s.World.Provisioner.AddHost("203.0.113.10");

        var location = await s.PostExpectingRedirectAsync("/Hosts/Add", Form(("Address", "203.0.113.10"), ("Port", "22"), ("SshUser", "root"), ("Name", "Helper EU 01")));
        Assert.EndsWith("?notice=added", location);
        var path = "/Operations/Detail/" + IdOf(location);
        await s.Driver.RunAsync(IdOf(location));

        var waiting = await s.PageAsync(path);
        Assert.Contains("Provide the temporary SSH key", ConsoleSession.Visible(waiting));
        Assert.Contains("The key is saved now and used automatically once you have confirmed the host key.", ConsoleSession.Visible(waiting));

        await s.PostExpectingRedirectAsync(path + "?handler=ConfirmKey", Form(("Fingerprint", helper.Fingerprint["SHA256:".Length..][..8])));
        var confirmed = ConsoleSession.Visible(await s.PageAsync(path));
        Assert.Contains("Waiting for the SSH key", confirmed);
        Assert.Contains("The operation is waiting for this key.", confirmed);

        // A key that cannot be read is refused before a code is used; a good one starts the bootstrap.
        using (var unreadable = await s.PostAsync(path + "?handler=Key", Form(("SshUser", "root"), ("PrivateKey", "not a key"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unreadable.StatusCode);
            Assert.Contains("No authenticator code was used", await TextOfAsync(unreadable));
        }

        var submitted = await s.PostExpectingRedirectAsync(path + "?handler=Key", Form(("SshUser", "root"), ("PrivateKey", helper.OwnerKeyText)));
        Assert.EndsWith("?notice=key-submitted", submitted);
        Assert.Equal("Bootstrapping", (await s.Driver.Hosts.GetOperationAsync(IdOf(location), CancellationToken.None)).State);
    }

    [Fact]
    public async Task A_key_with_a_passphrase_is_opened_once_and_a_wrong_passphrase_still_adds_the_helper_but_asks_for_the_key_again()
    {
        var s = await StartAsync();
        s.World.Provisioner.AddHost("203.0.113.10");
        s.World.Provisioner.AddHost("203.0.113.11");

        var good = await s.PostExpectingRedirectAsync(
            "/Hosts/Add",
            Form(("Address", "203.0.113.10"), ("Port", "22"), ("SshUser", "root"), ("PrivateKey", ProtectedKey), ("Passphrase", Fleet.Manager.Tests.Infrastructure.FakeSshKeyTool.CorrectPassphrase)));
        Assert.EndsWith("?notice=added-key-saved", good);

        var wrong = await s.PostExpectingRedirectAsync(
            "/Hosts/Add",
            Form(("Address", "203.0.113.11"), ("Port", "22"), ("SshUser", "root"), ("PrivateKey", ProtectedKey), ("Passphrase", "not the passphrase")));
        Assert.EndsWith("?notice=added-key-rejected", wrong);
        Assert.Equal(2, (await s.Get<HostService>().ListHostsAsync(CancellationToken.None)).Count);
        var page = ConsoleSession.Visible(await s.PageAsync(wrong));
        Assert.Contains("the SSH key could not be used", page);
        Assert.Contains("Provide the temporary SSH key", page);
        var hostId = (await s.Get<HostStore>().FindLiveByAddressAsync("203.0.113.11", 22, CancellationToken.None))!.Id;
        Assert.False(await s.Get<CredentialStore>().ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));

        // A passphrase with no key, and a key that needs a passphrase it was not given, are both refused up front.
        using var orphan = await s.PostAsync("/Hosts/Add", Form(("Address", "203.0.113.12"), ("Port", "22"), ("SshUser", "root"), ("Passphrase", "alone")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, orphan.StatusCode);
        Assert.Contains("passphrase", await TextOfAsync(orphan));
    }

    [Fact]
    public async Task Adding_the_same_helper_twice_is_a_no_op_and_two_helpers_with_one_name_get_different_references()
    {
        var s = await StartAsync();

        var first = await s.PostExpectingRedirectAsync("/Hosts/Add", Form(("Address", "203.0.113.10"), ("Port", "22"), ("SshUser", "root"), ("Name", "Berlin")));
        var again = await s.PostExpectingRedirectAsync("/Hosts/Add", Form(("Address", "203.0.113.10"), ("Port", "22"), ("SshUser", "root"), ("Name", "Berlin")));
        var other = await s.PostExpectingRedirectAsync("/Hosts/Add", Form(("Address", "203.0.113.11"), ("Port", "22"), ("SshUser", "root"), ("Name", "Berlin")));

        Assert.EndsWith("?notice=added", first);
        Assert.EndsWith("?notice=already-added", again);
        Assert.Equal(IdOf(first), IdOf(again));
        Assert.NotEqual(IdOf(first), IdOf(other));
        var hosts = await s.Get<HostService>().ListHostsAsync(CancellationToken.None);
        Assert.Equal(2, hosts.Count);
        Assert.Equal(new[] { "helper-berlin", "helper-berlin-2" }, hosts.Select(h => h.NodeRef).OrderBy(r => r, StringComparer.Ordinal).ToArray());
        Assert.Equal("Berlin", hosts.First(h => h.NodeRef == "helper-berlin").DisplayName);
        Assert.Contains("already in the fleet", ConsoleSession.Visible(await s.PageAsync(again)));
    }

    [Fact]
    public async Task A_helper_that_never_answers_fails_visibly_with_guidance_and_can_be_retried_or_cancelled_from_the_page()
    {
        var s = await StartAsync();
        var helper = s.World.Provisioner.AddHost("203.0.113.10");
        helper.Reachable = false;
        var location = await s.PostExpectingRedirectAsync("/Hosts/Add", Form(("Address", "203.0.113.10"), ("Port", "22"), ("SshUser", "root"), ("Name", "Helper EU 01")));
        var id = IdOf(location);
        var path = "/Operations/Detail/" + id;
        await s.Driver.RunAsync(id);

        var failed = ConsoleSession.Visible(await s.PageAsync(path));

        Assert.Contains("Failed: host_key_unreachable", failed);
        Assert.Contains("did not answer on its SSH port", failed);
        Assert.Contains("Retry from the failed step", failed);
        Assert.Contains("Cancel this operation", failed);

        helper.Reachable = true;
        Assert.EndsWith("?notice=retried", await s.PostExpectingRedirectAsync(path + "?handler=Retry", code: false));
        await s.Driver.RunAsync(id);
        Assert.Equal("HostKeyPending", (await s.Driver.Hosts.GetOperationAsync(id, CancellationToken.None)).State);

        Assert.EndsWith("?notice=cancelled", await s.PostExpectingRedirectAsync(path + "?handler=Cancel", code: false));
        Assert.Equal("Cancelled", (await s.Driver.Hosts.GetOperationAsync(id, CancellationToken.None)).State);
        using var again = await s.PostAsync(path + "?handler=Cancel", code: false);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("cannot be cancelled", await TextOfAsync(again));
    }

    // ---- one helper -------------------------------------------------------------------------------

    [Fact]
    public async Task The_page_of_a_helper_shows_hardware_components_workloads_limits_health_history_and_credential_hints()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);

        var html = await s.PageAsync("/Hosts/Detail/" + host.Id);
        var text = ConsoleSession.Visible(html);

        foreach (var heading in new[] { "Actions", "Summary", "Hardware", "Installed components", "Assigned workloads and limits", "Health history", "Provisioning, repair and maintenance history", "Credentials (write-only)", "Audit trail of this helper", "Change limits", "Remove from the fleet" })
        {
            Assert.Contains(heading, text);
        }

        Assert.Contains("Ubuntu 24.04 LTS", text);
        Assert.Contains("27.3.1", text);
        Assert.Contains("running", text);
        Assert.Contains("pdf.extract", text);
        Assert.Contains("Maximum concurrent jobs", text);
        Assert.Contains("round trip", text);
        Assert.Contains("oet-fleet-ctl version 1", text);
        Assert.Contains("Manager SSH key", text);
        Assert.Contains("Node token (last rendered)", text);
        Assert.DoesNotContain("Temporary owner SSH key", text);
        Assert.Matches("data-live-url=\"/Hosts/Detail/[^\"]+\\?handler=Fragment\"", html);
        Assert.Contains("data-reload-on-change=\"true\"", html);
        Assert.Contains("Choose one action", text);
        Assert.Contains("name=\"ActionName\" value=\"drain\"", html);
        Assert.DoesNotContain("name=\"ActionName\" value=\"resume\"", html);
        Assert.Matches("<form[^>]*action=\"/Hosts/Detail/[^\"]+\\?handler=Run\"", html);
    }

    [Fact]
    public async Task The_action_form_runs_exactly_the_chosen_action_and_only_with_a_fresh_code()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);
        var path = "/Hosts/Detail/" + host.Id;
        var hosts = s.Get<HostService>();

        // Nothing chosen: refused before the code is used.
        using (var none = await s.PostAsync(path + "?handler=Run", new Dictionary<string, string>()))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, none.StatusCode);
            Assert.Contains("Choose an action first", await TextOfAsync(none));
        }

        // A wrong code: nothing happens.
        using (var wrong = await s.PostAsync(path + "?handler=Run", Form(("ActionName", "drain")), code: false, explicitCode: "000000"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        }

        Assert.DoesNotContain(await hosts.ListOperationsAsync(20, host.Id, CancellationToken.None), op => op.Kind == "drain");

        // An unknown action name is refused as "nothing chosen".
        using (var unknown = await s.PostAsync(path + "?handler=Run", Form(("ActionName", "format-disk"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        }

        var location = await s.PostExpectingRedirectAsync(path + "?handler=Run", Form(("ActionName", "drain")));
        Assert.Equal(path + "?notice=drain-requested", location);
        var drain = (await hosts.ListOperationsAsync(20, host.Id, CancellationToken.None)).Single(op => op.Kind == "drain");
        await s.Driver.Runner.RunAsync(drain.Id, CancellationToken.None);
        await s.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);

        var drained = await s.PageAsync(path);
        Assert.Contains("name=\"ActionName\" value=\"resume\"", drained);
        Assert.Contains("Drain requested", ConsoleSession.Visible(await s.PageAsync(location)));

        // Resume, then disable: each through the same form.
        Assert.EndsWith("?notice=resume-requested", await s.PostExpectingRedirectAsync(path + "?handler=Run", Form(("ActionName", "resume"))));
        var resume = (await hosts.ListOperationsAsync(20, host.Id, CancellationToken.None)).First(op => op.Kind == "enable");
        await s.Driver.Runner.RunAsync(resume.Id, CancellationToken.None);
        Assert.Equal("Active", (await s.Get<HostStore>().GetAsync(host.Id, CancellationToken.None))!.Lifecycle);

        Assert.EndsWith("?notice=disable-requested", await s.PostExpectingRedirectAsync(path + "?handler=Run", Form(("ActionName", "disable"))));
        Assert.EndsWith("?notice=rotate-requested", await s.PostExpectingRedirectAsync(path + "?handler=Run", Form(("ActionName", "rotate-token"))));
        Assert.Contains(await hosts.ListOperationsAsync(20, host.Id, CancellationToken.None), op => op.Kind == "rotate-token");
    }

    [Fact]
    public async Task Repair_goes_to_the_operation_page_which_asks_for_a_fresh_temporary_key()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);

        var location = await s.PostExpectingRedirectAsync("/Hosts/Detail/" + host.Id + "?handler=Run", Form(("ActionName", "repair")));

        Assert.StartsWith("/Operations/Detail/op_", location);
        Assert.EndsWith("?notice=repair-requested", location);
        var page = ConsoleSession.Visible(await s.PageAsync(location));
        Assert.Contains("Repair", page);
        Assert.Contains("Helper EU 01", page);
        Assert.Contains("Waiting for the SSH key", page);
        Assert.Contains("Provide the temporary SSH key", page);
        Assert.Contains("The operation is waiting for this key.", page);
    }

    [Fact]
    public async Task Removing_a_helper_needs_its_node_reference_typed_and_says_what_it_will_and_will_not_touch()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);
        var path = "/Hosts/Detail/" + host.Id;
        var form = ConsoleSession.Visible(await s.PageAsync(path));
        Assert.Contains("only what this fleet installed", form);
        Assert.Contains("Docker, the firewall, other containers and volumes are not touched", form);

        using (var wrong = await s.PostAsync(path + "?handler=Remove", Form(("ConfirmNodeRef", "something-else"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
            Assert.Contains("Type the node reference exactly", await TextOfAsync(wrong));
        }

        Assert.DoesNotContain(await s.Get<HostService>().ListOperationsAsync(20, host.Id, CancellationToken.None), op => op.Kind == "remove");

        var location = await s.PostExpectingRedirectAsync(path + "?handler=Remove", Form(("ConfirmNodeRef", host.NodeRef), ("Force", "true")));

        Assert.Equal(path + "?notice=remove-requested", location);
        var removal = (await s.Get<HostService>().ListOperationsAsync(20, host.Id, CancellationToken.None)).Single(op => op.Kind == "remove");
        Assert.Equal("Queued", removal.State);
        Assert.Contains("\"force\":true", (await s.Get<OperationStore>().GetAsync(removal.Id, CancellationToken.None))!.ParamsJson);
    }

    [Fact]
    public async Task The_limits_of_one_helper_can_be_changed_cleared_and_a_bad_policy_is_refused_before_a_code_is_used()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);
        var path = "/Hosts/Detail/" + host.Id;
        var page = await s.PageAsync(path);
        Assert.Contains("name=\"Input.MaxConcurrency\"", page);
        Assert.Contains("name=\"Input.Kinds[0].Kind\"", page);
        Assert.Contains("This helper follows the global policy.", ConsoleSession.Visible(page));

        var code = s.NextCode();
        using (var outOfRange = await s.PostAsync(path + "?handler=Limits", PolicyFields(max: 9), explicitCode: code))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, outOfRange.StatusCode);
            var text = await TextOfAsync(outOfRange);
            Assert.Contains("outside the allowed ranges", text);
            Assert.Contains("maxConcurrency", text);
        }

        var incomplete = PolicyFields();
        incomplete.Remove("Input.CpuMilli");
        using (var missing = await s.PostAsync(path + "?handler=Limits", incomplete, explicitCode: code))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
            Assert.Contains("budgets.cpuMilli", await TextOfAsync(missing));
        }

        var tooBigScratch = PolicyFields(mem: 1024, tmp: 2048);
        using (var scratch = await s.PostAsync(path + "?handler=Limits", tooBigScratch, explicitCode: code))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, scratch.StatusCode);
            Assert.Contains("tmpfs is RAM", await TextOfAsync(scratch));
        }

        using (var notANumber = await s.PostAsync(path + "?handler=Limits", new Dictionary<string, string>(PolicyFields()) { ["Input.MemMiB"] = "lots" }, explicitCode: code))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, notANumber.StatusCode);
            Assert.Contains("not whole numbers", await TextOfAsync(notANumber));
        }

        // The same code, never spent by the four refusals, saves a good one.
        using var saved = await s.PostAsync(path + "?handler=Limits", PolicyFields(), explicitCode: code);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(path + "?notice=limits-saved", saved.Headers.Location!.ToString());
    }

    [Fact]
    public async Task A_good_limits_post_stores_the_override_pushes_it_to_the_api_and_clearing_it_goes_back_to_the_global_policy()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);
        var path = "/Hosts/Detail/" + host.Id;

        var location = await s.PostExpectingRedirectAsync(path + "?handler=Limits", PolicyFields());

        Assert.Equal(path + "?notice=limits-saved", location);
        Assert.Equal(1, s.World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!.Policy!.MaxConcurrency);
        var saved = ConsoleSession.Visible(await s.PageAsync(location));
        Assert.Contains("This helper has its own limits.", saved);
        Assert.Contains("Use the global policy", saved);
        Assert.Contains("Limits saved for this helper and pushed to the OET API.", saved);

        s.World.Api.Reachable = false;
        var notPushed = await s.PostExpectingRedirectAsync(path + "?handler=Limits", PolicyFields(max: 2, cpu: 2000, mem: 2048, tmp: 1024));
        Assert.EndsWith("?notice=limits-saved-not-pushed", notPushed);
        s.World.Api.Reachable = true;

        var cleared = await s.PostExpectingRedirectAsync(path + "?handler=ClearLimits");
        Assert.EndsWith("?notice=limits-cleared", cleared);
        Assert.Contains("This helper follows the global policy.", ConsoleSession.Visible(await s.PageAsync(path)));
        Assert.Equal(2, s.World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!.Policy!.MaxConcurrency);
    }

    [Fact]
    public async Task A_changed_host_key_shows_the_alert_and_the_two_step_repin_with_the_fingerprint_to_check()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);
        var helper = s.World.Provisioner.Find("203.0.113.10")!;
        var path = "/Hosts/Detail/" + host.Id;
        helper.KeyBlob = FakeKeys.HostKeyBlob("someone else answers on this address now");
        await s.Get<HostSecurityService>().OnHostKeyChangedAsync(host.Id, "system", CancellationToken.None);

        var alert = await s.PageAsync(path);
        var alertText = ConsoleSession.Visible(alert);
        Assert.Contains("The SSH host key changed", alertText);
        Assert.Contains("Verify the new fingerprint with your provider", alertText);
        Assert.Contains("host_key_changed", alertText);
        Assert.DoesNotContain("name=\"ActionName\" value=\"resume\"", alert);
        Assert.DoesNotContain("name=\"ActionName\" value=\"repair\"", alert);

        // Step 1 (no code, display only): the fingerprint the helper offers now.
        using (var start = await s.PostAsync(path + "?handler=RepinStart", code: false))
        {
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            Assert.Contains(helper.Fingerprint, await TextOfAsync(start));
        }

        // Step 2: too short is refused before a code is used; the right characters pin it and clear the alert.
        using (var tooShort = await s.PostAsync(path + "?handler=RepinConfirm", Form(("Fingerprint", "abc"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, tooShort.StatusCode);
        }

        using (var mismatch = await s.PostAsync(path + "?handler=RepinConfirm", Form(("Fingerprint", "ZZZZZZZZ"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatch.StatusCode);
            Assert.Contains("do not match", await TextOfAsync(mismatch));
        }

        var done = await s.PostExpectingRedirectAsync(path + "?handler=RepinConfirm", Form(("Fingerprint", helper.Fingerprint["SHA256:".Length..][..8])));
        Assert.EndsWith("?notice=repin-done", done);
        Assert.Null((await s.Get<HostStore>().GetAsync(host.Id, CancellationToken.None))!.Alert);
        Assert.DoesNotContain("The SSH host key changed", ConsoleSession.Visible(await s.PageAsync(path)));
    }

    // ---- operations, releases, audit ------------------------------------------------------------

    [Fact]
    public async Task The_operations_page_lists_every_kind_with_filters_and_the_audit_tab_verifies_the_hash_chain()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);
        await s.PostExpectingRedirectAsync("/Hosts/Detail/" + host.Id + "?handler=Run", Form(("ActionName", "rotate-token")));

        var history = ConsoleSession.Visible(await s.PageAsync("/Operations"));
        Assert.Contains("Enrollment", history);
        Assert.Contains("Token rotation", history);
        Assert.Contains("Helper EU 01", history);

        var operations = await s.Get<HostService>().ListOperationsAsync(20, host.Id, CancellationToken.None);
        var enrollId = operations.Single(op => op.Kind == "enroll").Id;
        var rotateId = operations.Single(op => op.Kind == "rotate-token").Id;
        var filtered = await s.PageAsync("/Operations?kind=rotate-token&state=open");
        Assert.Contains("/Operations/Detail/" + rotateId, filtered);
        Assert.DoesNotContain("/Operations/Detail/" + enrollId, filtered);

        var audit = ConsoleSession.Visible(await s.PageAsync("/Operations?view=audit"));
        Assert.Contains("host.added", audit);
        Assert.Contains("Verify the hash chain", audit);
        Assert.DoesNotContain("The audit chain is intact", audit);
        var verified = ConsoleSession.Visible(await s.PageAsync("/Operations?view=audit&verify=true"));
        Assert.Contains("The audit chain is intact", verified);
    }

    [Fact]
    public async Task A_release_is_approved_and_rolled_out_from_the_releases_tab_each_with_its_own_code()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);
        var release = await s.Driver.Releases.IngestAsync(s.Driver.Record('2', FleetWorld.NextDigest, FleetWorld.NextImageId), "ci", CancellationToken.None);

        var before = ConsoleSession.Visible(await s.PageAsync("/Operations?view=releases"));
        Assert.Contains("Not approved", before);
        Assert.Contains("Approve", before);
        Assert.Contains("Registry pull token", before);

        using (var bad = await s.PostAsync("/Operations?handler=Approve", Form(("releaseId", "rel_nope"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
            Assert.Contains("not a release", await TextOfAsync(bad));
        }

        var approved = await s.PostExpectingRedirectAsync("/Operations?handler=Approve", Form(("releaseId", release.Id)));
        Assert.Equal("/Operations?view=releases&notice=release-approved", approved);
        var after = ConsoleSession.Visible(await s.PageAsync(approved));
        Assert.Contains("Release approved", after);
        Assert.Contains("Roll out", after);
        Assert.Contains("In the rollback window", after);
        Assert.True((await s.Driver.Releases.ListAsync(CancellationToken.None)).All(r => r.Approved));

        using (var badDigest = await s.PostAsync("/Operations?handler=Rollout", Form(("digest", "latest"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, badDigest.StatusCode);
        }

        var rollout = await s.PostExpectingRedirectAsync("/Operations?handler=Rollout", Form(("digest", FleetWorld.NextDigest)));
        Assert.StartsWith("/Operations/Detail/op_", rollout);
        Assert.EndsWith("?notice=rollout-started", rollout);
        var page = ConsoleSession.Visible(await s.PageAsync(rollout));
        Assert.Contains("Image rollout", page);
        Assert.Contains("Stop new work (drain) (helper-eu-01)", page);
        Assert.Contains("Start the new image (helper-eu-01)", page);
        Assert.Equal(host.NodeRef, "helper-eu-01");
    }

    // ---- workloads, policies, credentials, projects ---------------------------------------------

    [Fact]
    public async Task The_workloads_page_shows_queue_depth_active_jobs_completion_eligible_helpers_and_the_reason_for_the_placement()
    {
        var s = await StartAsync();
        await EnrollAndPollAsync(s);
        s.World.Api.StatsJson = "{\"queue\":{\"pdf.extract\":{\"Queued\":4,\"Leased\":1,\"Succeeded\":9,\"Failed\":1,\"Quarantined\":0}},\"oldestQueuedAgeSeconds\":125,\"leasedCount\":1,\"leasedWeightByNode\":{}}";

        var html = await s.PageAsync("/Workloads");
        var text = ConsoleSession.Visible(html);

        Assert.Contains("Oldest queued job", text);
        Assert.Contains("2 min", text);
        Assert.Contains("pdf.extract", text);
        Assert.Contains("OET · Content papers", text);
        Assert.Contains("90%", text);
        Assert.Contains("1 of 1", text);
        Assert.Contains("The next job runs on helper-eu-01", text);
        Assert.Contains("Where work is running", text);
        Assert.Contains("data-live-url=\"/Workloads?handler=Fragment\"", html);

        s.World.Api.Reachable = false;
        var down = ConsoleSession.Visible(await s.PageAsync("/Workloads"));
        Assert.Contains("job statistics are not available right now", down);
        Assert.Contains("unreachable", down);
    }

    [Fact]
    public async Task Without_a_helper_the_workloads_page_says_the_primary_takes_the_job_and_when_it_would_wait()
    {
        var s = await StartAsync();

        var local = ConsoleSession.Visible(await s.PageAsync("/Workloads"));
        Assert.Contains("No eligible helper; the primary has headroom", local);
        Assert.Contains("No helper is registered with the OET API yet", local);

        ((FakePressureSource)s.Get<IPrimaryPressureSource>()).Pressure = null;
        var waiting = ConsoleSession.Visible(await s.PageAsync("/Workloads"));
        Assert.Contains("the job waits (after 60 minutes it runs on the primary regardless)", waiting);
    }

    [Fact]
    public async Task The_policies_page_edits_the_global_policy_with_a_code_pushes_it_and_explains_priorities_and_fallback()
    {
        var s = await StartAsync();
        await EnrollAndPollAsync(s);

        var page = await s.PageAsync("/Policies");
        var text = ConsoleSession.Visible(page);
        Assert.Contains("Global policy", text);
        Assert.Contains("built-in default", text);
        Assert.Contains("Priorities and fallback", text);
        Assert.Contains("After 60 minutes it runs on the primary regardless", text);
        Assert.Contains("CPU pressure under 25", text);
        Assert.Contains("Helpers and their limits", text);
        Assert.Contains("name=\"Input.Kinds[0].Max\"", page);

        // A kind the API does not know is refused up front.
        using (var unknown = await s.PostAsync("/Policies?handler=Save", PolicyFields(kind: "bogus.kind")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
            Assert.Contains("not in the API registry", await TextOfAsync(unknown));
        }

        using (var tooMany = await s.PostAsync("/Policies?handler=Save", PolicyFields(max: 9)))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.StatusCode);
        }

        var location = await s.PostExpectingRedirectAsync("/Policies?handler=Save", PolicyFields());
        Assert.Equal("/Policies?notice=policy-saved&n=1&f=0", location);
        Assert.Contains("Pushed to 1 helper(s); 0 could not be updated", ConsoleSession.Visible(await s.PageAsync(location)));
        Assert.Equal(1, s.World.Api.NodeByRef(EnrollmentDriver.DefaultNodeRef)!.Policy!.MaxConcurrency);
        var stored = ConsoleSession.Visible(await s.PageAsync("/Policies"));
        Assert.Contains("The saved policy every helper follows", stored);
        using var api = await s.Client.GetAsync("/api/v1/policy");
        using var json = JsonDocument.Parse(await api.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("maxConcurrency").GetInt32());
    }

    [Fact]
    public async Task A_push_that_fails_for_a_helper_is_counted_not_hidden()
    {
        var s = await StartAsync();
        await EnrollAndPollAsync(s);
        s.World.Api.RejectPolicyForNodeRefs.Add(EnrollmentDriver.DefaultNodeRef);

        var location = await s.PostExpectingRedirectAsync("/Policies?handler=Save", PolicyFields());

        Assert.Equal("/Policies?notice=policy-saved&n=0&f=1", location);
        Assert.Contains("Pushed to 0 helper(s); 1 could not be updated", ConsoleSession.Visible(await s.PageAsync(location)));
    }

    [Fact]
    public async Task The_credentials_page_lists_hints_only_adds_a_key_to_a_waiting_operation_and_revokes_early()
    {
        var s = await StartAsync();
        var helper = s.World.Provisioner.AddHost("203.0.113.10");
        var marker = helper.OwnerKeyText.Split('\n')[1];
        var (operation, _) = await s.Driver.AddAsync();
        await s.Driver.RunAsync(operation.Id);
        var hostId = (await s.Driver.HostAsync()).Id;

        // Nothing stored yet: the page offers to store a key for the operation that waits for one.
        var empty = ConsoleSession.Visible(await s.PageAsync("/Credentials"));
        Assert.Contains("No temporary key is stored. That is the normal state.", empty);
        Assert.Contains("Add or replace a temporary key", empty);
        Assert.Contains("Enrollment of Helper EU 01 (Waiting for your host-key check)", empty);

        var location = await s.PostExpectingRedirectAsync(
            "/Credentials?handler=AddKey",
            Form(("OperationId", operation.Id), ("SshUser", "root"), ("PrivateKey", helper.OwnerKeyText)));
        Assert.Equal("/Operations/Detail/" + operation.Id + "?notice=key-saved", location);

        var stored = await s.PageAsync("/Credentials");
        var info = (await s.Get<CredentialStore>().GetInfoAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None))!;
        Assert.Contains(info.FingerprintHint, stored);
        Assert.DoesNotContain(marker, stored);
        Assert.Contains("Erase now", stored);

        // A bad key is refused before a code is spent.
        using (var bad = await s.PostAsync("/Credentials?handler=AddKey", Form(("OperationId", operation.Id), ("SshUser", "root"), ("PrivateKey", "nope"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
            Assert.Contains("No authenticator code was used", await TextOfAsync(bad));
        }

        // Revoke: a non-host id is refused up front; the real one erases the key; a second time there is nothing to erase.
        using (var notAHost = await s.PostAsync("/Credentials?handler=RevokeKey", Form(("hostId", "not-a-guid"))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, notAHost.StatusCode);
        }

        Assert.EndsWith("?notice=owner-key-revoked", await s.PostExpectingRedirectAsync("/Credentials?handler=RevokeKey", Form(("hostId", hostId))));
        Assert.False(await s.Get<CredentialStore>().ExistsAnyAsync(hostId, CredentialPurposes.OwnerBootstrap, CancellationToken.None));
        Assert.EndsWith("?notice=owner-key-none", await s.PostExpectingRedirectAsync("/Credentials?handler=RevokeKey", Form(("hostId", hostId))));
    }

    [Fact]
    public async Task Node_tokens_are_rotated_from_the_credentials_page_and_the_registry_token_can_be_discarded()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);
        var nodeToken = s.World.Api.Nodes.Single().Tokens.First().Value;

        var page = await s.PageAsync("/Credentials");
        var text = ConsoleSession.Visible(page);
        Assert.Contains("Node tokens", text);
        Assert.Contains("Helper EU 01", text);
        Assert.DoesNotContain(nodeToken, page);
        Assert.Contains("Manager SSH keys", text);
        Assert.Contains("held in memory", text);
        Assert.Contains("Discard the pull token", text);

        var rotate = await s.PostExpectingRedirectAsync("/Credentials?handler=RotateToken", Form(("hostId", host.Id)));
        Assert.StartsWith("/Operations/Detail/op_", rotate);
        Assert.EndsWith("?notice=rotate-requested", rotate);

        var discarded = await s.PostExpectingRedirectAsync("/Credentials?handler=DiscardPullToken");
        Assert.EndsWith("?notice=pull-token-discarded", discarded);
        Assert.False(s.Get<RolloutTokenHolder>().HasToken);
        Assert.DoesNotContain("Discard the pull token", ConsoleSession.Visible(await s.PageAsync("/Credentials")));
    }

    [Fact]
    public async Task The_credentials_page_says_which_service_secrets_are_present_without_ever_reading_one_out()
    {
        var s = await StartAsync();
        await File.WriteAllTextAsync(Path.Combine(s.Factory.SecretsDirectory, "fleet_sync_token"), "SYNC-SECRET-VALUE\n");
        await File.WriteAllTextAsync(Path.Combine(s.Factory.SecretsDirectory, "fleet_api_credential"), "API-CREDENTIAL-SECRET-VALUE\n");

        var page = await s.PageAsync("/Credentials");
        var projects = await s.PageAsync("/Projects");

        var text = ConsoleSession.Visible(page);
        Assert.Contains("Fleet API credential present", text);
        Assert.Contains("CI sync token present", text);
        Assert.Contains("Metrics scrape token absent", text);
        Assert.Contains("Vault master key", text);
        Assert.Contains("vault-rewrap", text);
        foreach (var html in new[] { page, projects })
        {
            Assert.DoesNotContain("SYNC-SECRET-VALUE", html);
            Assert.DoesNotContain("API-CREDENTIAL-SECRET-VALUE", html);
        }

        var master = await File.ReadAllBytesAsync(Path.Combine(s.Factory.SecretsDirectory, "fleet_master_key"));
        Assert.DoesNotContain(Convert.ToHexString(master[..8]).ToLowerInvariant(), page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_project_overview_shows_the_primary_the_oet_integration_and_what_it_cannot_see()
    {
        var s = await StartAsync();
        await EnrollAndPollAsync(s);

        var html = await s.PageAsync("/Projects");
        var text = ConsoleSession.Visible(html);

        Assert.Contains("Primary VPS (shared by every project on it)", text);
        Assert.Contains("Projects on the primary", text);
        Assert.Contains("OET learner platform", text);
        Assert.Contains("Fleet manager (this console)", text);
        Assert.Contains("Other projects on the primary", text);
        Assert.Contains("cannot attribute CPU or memory to a project", text);
        Assert.Contains("OET integration", text);
        Assert.Contains("reachable", text);
        Assert.Contains("protocol 1, minimum 1", text);
        Assert.Contains("1 registered, 1 active, 1 online", text);
        Assert.Contains("data-live-url=\"/Projects?handler=Fragment\"", html);
    }

    // ---- live fragments -------------------------------------------------------------------------

    [Fact]
    public async Task The_live_fragments_are_bare_html_without_the_shell_and_carry_the_state_the_script_watches()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);
        var operation = (await s.Get<HostService>().ListOperationsAsync(10, host.Id, CancellationToken.None)).Single(op => op.Kind == "enroll");

        foreach (var path in new[]
                 {
                     "/?handler=Fragment", "/Hosts/Detail/" + host.Id + "?handler=Fragment", "/Operations/Detail/" + operation.Id + "?handler=Fragment",
                     "/Operations?handler=Fragment", "/Workloads?handler=Fragment", "/Projects?handler=Fragment",
                 })
        {
            using var response = await s.Client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();

            Assert.True(response.StatusCode == HttpStatusCode.OK, path + " answered " + response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
            Assert.DoesNotContain("<html", html);
            Assert.DoesNotContain("<header", html);
            Assert.DoesNotContain("<form", html);
            Assert.Contains("data-live-state=", html);
        }
    }

    [Fact]
    public async Task A_live_region_starts_with_the_same_markup_the_fragment_serves_and_the_page_never_puts_a_form_inside_one()
    {
        var s = await StartAsync();
        var host = await EnrollAndPollAsync(s);

        foreach (var path in new[] { "/", "/Hosts/Detail/" + host.Id, "/Operations", "/Workloads", "/Projects" })
        {
            var html = await s.PageAsync(path);

            // Every region the script refreshes is a data-live-url element: none contains a <form>, so what you type is never replaced.
            foreach (Match region in Regex.Matches(html, "<div id=\"[a-z-]+-live\"[^>]*data-live-url=\"[^\"]+\"[^>]*>(?<body>.*?)\\n</div>", RegexOptions.Singleline))
            {
                Assert.DoesNotContain("<form", region.Groups["body"].Value);
            }

            Assert.Contains("data-live-url=", html);
        }
    }

    // ---- text that comes from a helper ---------------------------------------------------------------

    [Fact]
    public async Task Text_that_comes_from_a_helper_or_a_child_process_is_encoded_on_every_page_that_shows_it()
    {
        var s = await StartAsync();
        var (operation, _) = await s.Driver.AddAsync();
        var hostId = (await s.Driver.HostAsync()).Id;
        await s.Get<HostStore>().UpdateAsync(
            hostId,
            h =>
            {
                h.DisplayName = "<i>x</i> & y";
                h.Alert = "<b>alert</b>";
                h.LastStatusJson = "{\"status\":\"Active\",\"health\":\"Online\",\"host\":{\"polledAt\":\"2026-10-05T11:59:00.0000000+00:00\",\"status\":{\"schema\":\"oet-fleet-ctl.status/1\","
                    + "\"host\":{\"os\":\"<script>alert(1)</script>Ubuntu\",\"arch\":\"<svg onload=alert(2)>\"},\"docker\":{\"version\":\"<img src=x onerror=alert(3)>\",\"running\":true},"
                    + "\"agent\":{\"present\":true,\"state\":\"<b>running</b>\"},\"ctl\":{\"version\":1}}}}";
            },
            CancellationToken.None);
        await s.Get<OperationStore>().UpdateAsync(
            operation.Id,
            null,
            o =>
            {
                o.State = "Failed";
                o.ResumeState = "Created";
                o.FailureReason = "ssh_unreachable";
                o.FailureDetail = "&lt;img src=x onerror=alert(4)&gt; &amp;amp;";
            },
            CancellationToken.None);
        await s.Get<OperationStore>().UpdateStepAsync(operation.Id, 1, step =>
        {
            step.State = "failed";
            step.Summary = "&lt;b&gt;bold&lt;/b&gt;";
        }, CancellationToken.None);

        foreach (var path in new[] { "/", "/Hosts/Detail/" + hostId, "/Operations", "/Operations/Detail/" + operation.Id, "/Credentials", "/Policies", "/Workloads", "/Projects" })
        {
            var html = await s.PageAsync(path);

            Assert.DoesNotContain("<script>alert", html);
            Assert.DoesNotContain("<img src=x", html);
            Assert.DoesNotContain("<svg onload", html);
            Assert.DoesNotContain("<i>x</i>", html);
            Assert.DoesNotContain("<b>alert</b>", html);
            Assert.DoesNotContain("<b>bold</b>", html);
            Assert.DoesNotContain("&amp;lt;", html);
        }

        var detail = await s.PageAsync("/Hosts/Detail/" + hostId);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;Ubuntu", detail);
        Assert.Contains("&lt;img src=x onerror=alert(3)&gt;", detail);
        Assert.Contains("&lt;i&gt;x&lt;/i&gt; &amp; y", detail);
        var failure = await s.PageAsync("/Operations/Detail/" + operation.Id);
        Assert.Contains("&lt;img src=x onerror=alert(4)&gt; &amp;amp;", failure);
        Assert.Contains("&lt;b&gt;bold&lt;/b&gt;", failure);
        Assert.Contains("The manager could not reach the helper over SSH", ConsoleSession.Visible(failure));
    }

    // ---- no secret on any page -----------------------------------------------------------------------

    [Fact]
    public async Task No_credential_the_manager_holds_appears_on_any_page_response_or_fragment()
    {
        var s = await StartAsync();
        var helper = s.World.Provisioner.AddHost("203.0.113.10");
        var marker = helper.OwnerKeyText.Split('\n')[1];
        await File.WriteAllTextAsync(Path.Combine(s.Factory.SecretsDirectory, "fleet_api_credential"), "ofs1-API-CREDENTIAL-VALUE-0123456789\n");
        await File.WriteAllTextAsync(Path.Combine(s.Factory.SecretsDirectory, "fleet_sync_token"), "SYNC-SECRET-VALUE-0123456789\n");
        await File.WriteAllTextAsync(Path.Combine(s.Factory.SecretsDirectory, "fleet_metrics_token"), "METRICS-SECRET-VALUE-0123456789\n");
        var master = await File.ReadAllBytesAsync(Path.Combine(s.Factory.SecretsDirectory, "fleet_master_key"));

        // A second helper is fully enrolled (node token, manager key); a third has a saved owner key waiting for its host-key check.
        await s.Driver.EnrollToActiveAsync();
        var third = s.World.Provisioner.AddHost("203.0.113.30");
        var (pending, _) = await s.Driver.AddAsync("helper-eu-03", "203.0.113.30");
        await s.Driver.Enrollment.StageOwnerCredentialAsync(pending.Id, "root", third.OwnerKeyText, null, "owner", CancellationToken.None);
        await s.Driver.Releases.SyncAsync(new SyncPayload(s.Driver.Record(), "ci-user", "ghs_TESTONLYTOKEN0123456789abcdef"), "ci-sync", CancellationToken.None);
        await s.Get<NodeMonitor>().PollOnceAsync(CancellationToken.None);
        var host = await s.Driver.HostAsync();
        var nodeToken = s.World.Api.Nodes.First().Tokens.First().Value;

        var secrets = new[]
        {
            marker, third.OwnerKeyText.Split('\n')[1], nodeToken, "ofs1-API-CREDENTIAL-VALUE-0123456789", "SYNC-SECRET-VALUE-0123456789",
            "METRICS-SECRET-VALUE-0123456789", "ghs_TESTONLYTOKEN0123456789abcdef", Convert.ToHexString(master).ToLowerInvariant(),
            Convert.ToHexString(master[..8]).ToLowerInvariant(), Convert.ToBase64String(master), FleetWebFactory.Password,
        };
        var paths = new[]
        {
            "/", "/?handler=Fragment", "/Hosts/Add", "/Hosts/Detail/" + host.Id, "/Hosts/Detail/" + host.Id + "?handler=Fragment", "/Operations",
            "/Operations?view=releases", "/Operations?view=audit&verify=true", "/Operations/Detail/" + pending.Id, "/Operations/Detail/" + pending.Id + "?handler=Fragment",
            "/Workloads", "/Policies", "/Credentials", "/Projects", "/Health",
        };

        foreach (var path in paths)
        {
            var html = await s.PageAsync(path);
            foreach (var secret in secrets)
            {
                Assert.False(html.Contains(secret, StringComparison.Ordinal), path + " contains a secret");
            }
        }

        // The event stream and metrics never carry them either (the console subscribes to the stream).
        using var metrics = await s.Client.GetAsync("/metrics");
        var metricsText = await metrics.Content.ReadAsStringAsync();
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, metricsText);
        }
    }

    // ---- the script itself --------------------------------------------------------------------------

    [Fact]
    public async Task The_script_uses_no_eval_no_browser_storage_no_document_write_and_reads_key_files_only_into_the_key_box()
    {
        var s = await StartAsync();

        var script = await (await s.Client.GetAsync("/js/console.js")).Content.ReadAsStringAsync();

        foreach (var forbidden in new[] { "eval(", "new Function", "document.write", "localStorage", "sessionStorage", "indexedDB", "XMLHttpRequest", "insertAdjacentHTML", "outerHTML", ".open(", "WebSocket", "navigator.sendBeacon" })
        {
            Assert.DoesNotContain(forbidden, script);
        }

        Assert.Equal(1, Regex.Matches(script, "innerHTML").Count);
        Assert.Contains("template.innerHTML = html", script);
        Assert.Contains("readAsText", script);
        Assert.Contains("pagehide", script);
        Assert.Contains("data-secret", script);
        Assert.Contains("/api/v1/events", script);
    }
}
