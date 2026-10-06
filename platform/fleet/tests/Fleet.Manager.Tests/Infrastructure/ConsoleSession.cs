using System.Net;
using System.Text.RegularExpressions;
using Fleet.Manager.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Fleet.Manager.Tests.Infrastructure;

/// <summary>
/// A signed-in owner browsing the real console (no browser): a cookie-carrying client, the antiforgery token every form needs and a
/// fresh, never-used authenticator code for every privileged post (the test clock moves 90 seconds so the verifier's replay guard is satisfied).
/// </summary>
public sealed class ConsoleSession : IDisposable
{
    private ConsoleSession(FleetWebFactory factory, HttpClient client, string secret)
    {
        Factory = factory;
        Client = client;
        Secret = secret;
        Driver = new EnrollmentDriver(factory.World, factory.Services);
    }

    public FleetWebFactory Factory { get; }

    public HttpClient Client { get; }

    public string Secret { get; }

    public EnrollmentDriver Driver { get; }

    public FleetWorld World => Factory.World;

    public T Get<T>()
        where T : notnull => Factory.Services.GetRequiredService<T>();

    public static async Task<ConsoleSession> StartAsync(Action<Dictionary<string, string?>>? configure = null)
    {
        var factory = new FleetWebFactory(configure);
        var (client, secret, _) = await factory.SignedInAsync();
        return new ConsoleSession(factory, client, secret);
    }

    /// <summary>A code that has never been used (the verifier also looks one step ahead, hence 90 seconds).</summary>
    public string NextCode()
    {
        Factory.Time.Advance(TimeSpan.FromSeconds(90));
        return Factory.Code(Secret);
    }

    /// <summary>
    /// Moves the test clock forward in short steps with a request after each, the way an owner who keeps the console open would: a session that
    /// idles for 20 minutes ends, and the sliding renewal needs a request in the second half of the window.
    /// </summary>
    public async Task AdvanceAsync(TimeSpan total)
    {
        var step = TimeSpan.FromMinutes(4);
        var done = TimeSpan.Zero;
        while (done < total)
        {
            var next = total - done < step ? total - done : step;
            Factory.Time.Advance(next);
            done += next;
            using var response = await Client.GetAsync("/Health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    /// <summary>GET a page and require a 200.</summary>
    public async Task<string> PageAsync(string path)
    {
        using var response = await Client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, path + " answered " + response.StatusCode + ": " + Shorten(html));
        return html;
    }

    /// <summary>
    /// POST a form the way the browser would: the antiforgery token of a rendered page, the fields, and (unless <paramref name="code"/> is
    /// false or given) a fresh authenticator code.
    /// </summary>
    public async Task<HttpResponseMessage> PostAsync(
        string path,
        IDictionary<string, string>? fields = null,
        bool code = true,
        string? explicitCode = null,
        bool antiforgery = true)
    {
        var form = new Dictionary<string, string>(fields ?? new Dictionary<string, string>());
        if (antiforgery)
        {
            form["__RequestVerificationToken"] = await FleetWebFactory.AntiforgeryFieldAsync(Client, "/Health");
        }

        if (explicitCode is not null)
        {
            form["Code"] = explicitCode;
        }
        else if (code)
        {
            form["Code"] = NextCode();
        }

        return await Client.PostAsync(path, new FormUrlEncodedContent(form));
    }

    /// <summary>POSTs and requires a redirect, returning where it goes (a local path with the notice code).</summary>
    public async Task<string> PostExpectingRedirectAsync(string path, IDictionary<string, string>? fields = null, bool code = true)
    {
        using var response = await PostAsync(path, fields, code);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, path + " answered " + response.StatusCode + ": " + Shorten(Visible(body)));
        return response.Headers.Location!.ToString();
    }

    /// <summary>The visible text of a page: tags removed, entities decoded, whitespace collapsed (for assertions on what the owner reads).</summary>
    public static string Visible(string html)
    {
        var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), "\\s+", " ").Trim();
    }

    private static string Shorten(string text) => text.Length > 600 ? text[..600] + "..." : text;

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
