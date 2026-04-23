using System.Collections.Generic;
using OetLearner.Api.Observability;
using Sentry;
using Sentry.Protocol;
using Xunit;

namespace OetLearner.Api.Tests.Observability;

/// <summary>
/// H10: Verify that SentryBootstrap.ScrubPii removes user-identifying data and
/// sensitive headers before an event would leave the process. These assertions are
/// the privacy contract - if one fails, we are about to leak PII to Sentry.
/// </summary>
public class SentryBootstrapTests
{
    [Fact]
    public void ScrubPii_Removes_User_Email_Ip_Username_But_Keeps_Id()
    {
        var evt = new SentryEvent
        {
            User = new SentryUser
            {
                Id = "user-123",
                Email = "learner@example.com",
                IpAddress = "203.0.113.5",
                Username = "learner",
            },
        };

        var scrubbed = SentryBootstrap.ScrubPii(evt);

        Assert.NotNull(scrubbed);
        Assert.Equal("user-123", scrubbed!.User!.Id);
        Assert.Null(scrubbed.User.Email);
        Assert.Null(scrubbed.User.IpAddress);
        Assert.Null(scrubbed.User.Username);
    }

    [Fact]
    public void ScrubPii_Removes_Sensitive_Headers_Cookies_And_QueryString()
    {
        var request = new SentryRequest
        {
            Method = "POST",
            Url = "https://api.example.com/v1/auth/sign-in",
            QueryString = "next=/dashboard&token=secret",
            Cookies = "session=abc; oet_rt=refresh",
        };
        request.Headers["Authorization"] = "Bearer leaked-jwt";
        request.Headers["cookie"] = "session=abc";
        request.Headers["X-CSRF-Token"] = "csrf-xyz";
        request.Headers["X-Forwarded-For"] = "203.0.113.5";
        request.Headers["Content-Type"] = "application/json";
        request.Headers["User-Agent"] = "Mozilla/5.0";

        var evt = new SentryEvent { Request = request };

        var scrubbed = SentryBootstrap.ScrubPii(evt);

        Assert.NotNull(scrubbed);
        var req = scrubbed!.Request!;
        Assert.Null(req.QueryString);
        Assert.Null(req.Cookies);

        // Case-insensitive header scrub.
        Assert.False(req.Headers.ContainsKey("Authorization"));
        Assert.False(req.Headers.ContainsKey("cookie"));
        Assert.False(req.Headers.ContainsKey("X-CSRF-Token"));
        Assert.False(req.Headers.ContainsKey("X-Forwarded-For"));

        // Benign headers survive so stack grouping / triage still works.
        Assert.True(req.Headers.ContainsKey("Content-Type"));
        Assert.True(req.Headers.ContainsKey("User-Agent"));
    }

    [Fact]
    public void ScrubPii_Is_Null_Safe_When_User_And_Request_Are_Missing()
    {
        var evt = new SentryEvent();

        var scrubbed = SentryBootstrap.ScrubPii(evt);

        Assert.NotNull(scrubbed);
    }
}
