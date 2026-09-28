using System.Text.Json;
using Microsoft.AspNetCore.Http;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// ApiErrorResult must write exactly what the central ApiException handler in Program.cs
/// writes: same content type and the property set/order
/// <c>code, message, fieldErrors, retryable, supportHint, correlationId</c>.
/// </summary>
public sealed class ApiErrorResultTests
{
    private static async Task<(HttpContext Http, JsonElement Body)> RunAsync(IResult result, string? correlationId = "cid-123")
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        if (correlationId is not null) http.Items["CorrelationId"] = correlationId;

        await result.ExecuteAsync(http);

        http.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(http.Response.Body);
        return (http, doc.RootElement.Clone());
    }

    [Fact]
    public async Task Writes_the_central_handler_shape()
    {
        var (http, body) = await RunAsync(new ApiErrorResult(
            StatusCodes.Status400BadRequest,
            "paper_invalid",
            "Paper is invalid.",
            FieldErrors: [new ApiFieldError("title", "required", "Title is required.")],
            SupportHint: "hint"));

        Assert.Equal(400, http.Response.StatusCode);
        Assert.Equal("application/problem+json", http.Response.ContentType);
        Assert.Equal(
            new[] { "code", "message", "fieldErrors", "retryable", "supportHint", "correlationId" },
            body.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("paper_invalid", body.GetProperty("code").GetString());
        Assert.Equal("Paper is invalid.", body.GetProperty("message").GetString());
        Assert.False(body.GetProperty("retryable").GetBoolean());
        Assert.Equal("hint", body.GetProperty("supportHint").GetString());
        Assert.Equal("cid-123", body.GetProperty("correlationId").GetString());
        var fieldError = Assert.Single(body.GetProperty("fieldErrors").EnumerateArray());
        Assert.Equal(
            new[] { "field", "code", "message" },
            fieldError.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task Defaults_match_the_legacy_client_assumptions()
    {
        var (_, badRequest) = await RunAsync(new ApiErrorResult(400, "x", "m"), correlationId: null);
        Assert.Empty(badRequest.GetProperty("fieldErrors").EnumerateArray());
        Assert.False(badRequest.GetProperty("retryable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, badRequest.GetProperty("supportHint").ValueKind);
        Assert.Equal(JsonValueKind.Null, badRequest.GetProperty("correlationId").ValueKind);
        Assert.False(badRequest.TryGetProperty("error", out _));

        var (_, badGateway) = await RunAsync(new ApiErrorResult(502, "x", "m"));
        Assert.True(badGateway.GetProperty("retryable").GetBoolean());

        var (_, pinned) = await RunAsync(new ApiErrorResult(502, "x", "m", Retryable: false));
        Assert.False(pinned.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task Legacy_alias_adds_error_only_when_asked()
    {
        var (_, body) = await RunAsync(new ApiErrorResult(400, "x", "Readable.", LegacyErrorAlias: true));
        Assert.Equal("Readable.", body.GetProperty("error").GetString());
        Assert.Equal("Readable.", body.GetProperty("message").GetString());
    }

    [Fact]
    public void Exposes_status_code_for_result_inspection()
    {
        IResult result = new ApiErrorResult(409, "x", "m");
        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public void SafeMessage_keeps_our_messages_and_hides_framework_text()
    {
        var framework = Assert.ThrowsAny<InvalidOperationException>(() => Enumerable.Empty<int>().First());
        Assert.Equal("fallback", ApiErrorResult.SafeMessage(framework, "fallback"));

        var ours = Assert.ThrowsAny<ArgumentException>(() => ContentAddressed.PublishedKey("root", "ab", "mp3"));
        Assert.Equal(ours.Message, ApiErrorResult.SafeMessage(ours, "fallback"));
    }
}
