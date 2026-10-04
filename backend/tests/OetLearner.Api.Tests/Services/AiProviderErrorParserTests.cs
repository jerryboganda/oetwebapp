using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// The provider error model: classification precedence per vendor dialect, the safe
/// capture rules (allow-listed tokens, redaction before truncation, head-only text for
/// non-vendor hosts) and exception classification. Bodies mirror the documented vendor
/// envelopes; the OpenAI live-session 429 body itself is undocumented, so the parser
/// must also degrade to a status-only class.
/// </summary>
public sealed class AiProviderErrorParserTests
{
    // ── OpenAI ──────────────────────────────────────────────────────────
    private const string OpenAiInsufficientQuota =
        """{"error":{"message":"You exceeded your current quota, please check your plan and billing details.","type":"insufficient_quota","param":null,"code":"insufficient_quota"}}""";
    private const string OpenAiRateLimitMentioningBilling =
        """{"error":{"message":"Rate limit reached for gpt-live-1 on requests per min (RPM): Limit 3, Used 3, Requested 1. Please try again in 20s. You can increase your rate limit by adding a payment method to your account at https://platform.openai.com/account/billing.","type":"requests","param":null,"code":"rate_limit_exceeded"}}""";
    private const string OpenAiInvalidKey =
        """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error","param":null,"code":"invalid_api_key"}}""";
    private const string OpenAiCountry =
        """{"error":{"code":"unsupported_country_region_territory","message":"Country, region, or territory not supported","param":null,"type":"request_forbidden"}}""";
    private const string OpenAiUnknownParameter =
        """{"error":{"message":"Unknown parameter: 'foo'.","type":"invalid_request_error","param":"foo","code":"unknown_parameter"}}""";
    private const string OpenAiModelNotFound =
        """{"error":{"message":"The model does not exist or you do not have access to it.","type":"invalid_request_error","param":null,"code":"model_not_found"}}""";
    private const string OpenAiOverloaded =
        """{"error":{"message":"The engine is currently overloaded, please try again later","type":"server_error","param":null,"code":null}}""";
    private const string OpenAiServerError =
        """{"error":{"message":"The server had an error while processing your request.","type":"server_error","param":null,"code":null}}""";

    // ── Anthropic ───────────────────────────────────────────────────────
    private const string AnthropicCreditBalance =
        """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to upgrade or purchase credits."},"request_id":"req_011CTexample"}""";
    private const string AnthropicUsageLimits =
        """{"type":"error","error":{"type":"invalid_request_error","message":"You have reached your specified API usage limits. You will regain access on 2026-10-01 at 00:00 UTC."}}""";
    private const string AnthropicBillingError =
        """{"type":"error","error":{"type":"billing_error","message":"Payment required."}}""";
    private const string AnthropicMaxTokens =
        """{"type":"error","error":{"type":"invalid_request_error","message":"max_tokens: 128000 > 64000, which is the maximum allowed number of output tokens for claude-opus-5-5"}}""";
    private const string AnthropicBetaHeader =
        """{"type":"error","error":{"type":"invalid_request_error","message":"Unexpected value(s) `prompt-caching-2024-07-31` for the `anthropic-beta` header. Please consult our documentation at docs.anthropic.com or try again without the header."}}""";
    private const string AnthropicAuthentication =
        """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""";
    private const string AnthropicPermission =
        """{"type":"error","error":{"type":"permission_error","message":"Your API key does not have permission to use the specified resource."}}""";
    private const string AnthropicRateLimit =
        """{"type":"error","error":{"type":"rate_limit_error","message":"This request would exceed your organization's rate limit of 30,000 input tokens per minute."}}""";
    private const string AnthropicOverloaded =
        """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""";
    private const string AnthropicApiError =
        """{"type":"error","error":{"type":"api_error","message":"Internal server error"}}""";
    private const string AnthropicTimeout =
        """{"type":"error","error":{"type":"timeout_error","message":"Request timed out"}}""";

    // ── Claude subscription sidecar (writing-ai-sidecars/shared/http.mjs) ─
    private const string SidecarQuota =
        """{"error":{"code":"quota_exceeded","message":"Claude subscription quota/rate limit: you have hit your limit","type":"rate_limit_error"}}""";
    private const string SidecarEngineError =
        """{"error":{"code":"engine_error","message":"claude exited 1: something went wrong"}}""";
    // WAI-04 sidecar hardening: a full lane answers THAT request 503 (retryable, fails over inside the
    // grade only) and a dead login answers 401 (Auth). Neither ever disables the Max route.
    private const string SidecarLaneBusy =
        """{"error":{"code":"lane_busy","message":"Claude lane is full: try again shortly","type":"overloaded_error"}}""";
    private const string SidecarAuthExpired =
        """{"error":{"code":"auth_expired","message":"Claude login expired or invalid: please sign in again","type":"authentication_error"}}""";

    // ── Gemini ──────────────────────────────────────────────────────────
    private const string GeminiPerDayQuota =
        """{"error":{"code":429,"message":"You exceeded your current quota, please check your plan and billing details.","status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaMetric":"generativelanguage.googleapis.com/generate_content_free_tier_requests","quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier","quotaValue":"50"}]},{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"33s"}]}}""";
    private const string GeminiPerMinuteQuota =
        """{"error":{"code":429,"message":"You exceeded your current quota, please check your plan and billing details.","status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaId":"GenerateRequestsPerMinutePerProjectPerModel-FreeTier"}]},{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"33s"}]}}""";
    private const string GeminiNoDetails =
        """{"error":{"code":429,"message":"Resource has been exhausted (e.g. check quota).","status":"RESOURCE_EXHAUSTED"}}""";
    private const string GeminiInvalidKey =
        """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"API_KEY_INVALID","domain":"googleapis.com"}]}}""";
    private const string GeminiUnknownField =
        """{"error":{"code":400,"message":"Invalid JSON payload received. Unknown name foo: Cannot find field.","status":"INVALID_ARGUMENT"}}""";
    private const string GeminiPermissionDenied =
        """{"error":{"code":403,"message":"Method doesn't allow unregistered callers","status":"PERMISSION_DENIED"}}""";
    private const string GeminiUnavailable =
        """{"error":{"code":503,"message":"The model is overloaded. Please try again later.","status":"UNAVAILABLE"}}""";
    private const string GeminiInternal =
        """{"error":{"code":500,"message":"Internal error encountered.","status":"INTERNAL"}}""";
    private const string GeminiBillingPrecondition =
        """{"error":{"code":400,"message":"Gemini API free tier is not available in your country. Please enable billing on your project in Google AI Studio.","status":"FAILED_PRECONDITION"}}""";

    // ── OpenCode inference gateway ({"type":"error","error":{"type":"<PascalCase>","message":"..."}}) ──
    // 401 is overloaded there: a bad key (AuthError) and an empty balance or spent plan (CreditsError,
    // MonthlyLimitError) share it, so the error type decides, not the status.
    private const string OpenCodeAuthError =
        """{"type":"error","error":{"type":"AuthError","message":"Invalid API key."}}""";
    private const string OpenCodeCreditsError =
        """{"type":"error","error":{"type":"CreditsError","message":"No payment method. Add a payment method in the console."}}""";
    private const string OpenCodeMonthlyLimitError =
        """{"type":"error","error":{"type":"MonthlyLimitError","message":"Monthly limit reached."}}""";
    private const string OpenCodeFundsBody =
        """{"error":{"message":"Insufficient account funds"}}""";
    private const string OpenCodeRateLimitError =
        """{"type":"error","error":{"type":"RateLimitError","message":"Rate limit exceeded. Please try again later."}}""";
    private const string OpenCodeGoUsageLimitError =
        """{"type":"error","error":{"type":"GoUsageLimitError","message":"Go usage limit reached."}}""";
    private const string OpenCodeFreeUsageLimitError =
        """{"type":"error","error":{"type":"FreeUsageLimitError","message":"Free usage limit reached."}}""";
    private const string OpenCodeApiError =
        """{"type":"error","error":{"type":"api_error","message":"Upstream unavailable."}}""";
    private const string OpenCodeModelError =
        """{"type":"error","error":{"type":"ModelError","message":"Model rejected the request."}}""";
    private const string OpenCodeProtocolUnsupported =
        """{"type":"error","error":{"type":"ModelProtocolUnsupported","message":"This model does not serve chat/completions."}}""";

    [Theory]
    [InlineData(401, OpenCodeAuthError, AiProviderErrorClass.Auth, "autherror")]
    [InlineData(401, OpenCodeCreditsError, AiProviderErrorClass.QuotaExhausted, "creditserror")]
    [InlineData(401, OpenCodeMonthlyLimitError, AiProviderErrorClass.QuotaExhausted, "monthlylimiterror")]
    [InlineData(402, OpenCodeFundsBody, AiProviderErrorClass.QuotaExhausted, null)]
    [InlineData(429, OpenCodeRateLimitError, AiProviderErrorClass.RateLimited, "ratelimiterror")]
    [InlineData(429, OpenCodeGoUsageLimitError, AiProviderErrorClass.QuotaExhausted, "gousagelimiterror")]
    [InlineData(429, OpenCodeFreeUsageLimitError, AiProviderErrorClass.QuotaExhausted, "freeusagelimiterror")]
    [InlineData(503, OpenCodeApiError, AiProviderErrorClass.Overloaded, "api_error")]
    [InlineData(500, OpenCodeApiError, AiProviderErrorClass.ServerError, "api_error")]
    [InlineData(400, OpenCodeModelError, AiProviderErrorClass.InvalidRequest, "modelerror")]
    [InlineData(400, OpenCodeProtocolUnsupported, AiProviderErrorClass.InvalidRequest, "modelprotocolunsupported")]
    public void Parse_ClassifiesOpenCodeGatewayErrors_FromTheErrorTypeBeforeTheStatus(
        int status, string body, AiProviderErrorClass expectedClass, string? expectedType)
    {
        // The runtime adapter parses OpenCode replies with the OpenAI envelope dialect.
        var error = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.OpenAi, status, body, headers: null, apiKey: null, retainProviderText: true);

        Assert.Equal(expectedClass, error.Class);
        Assert.Equal(status, error.HttpStatus);
        Assert.Equal(expectedType, error.Type);
    }

    [Fact]
    public void Parse_OpenCodeRateLimitMentioningUsageLimits_StaysRateLimited()
    {
        // The rate token blocks the phrase-based quota match ("usage limits" is a quota phrase).
        var body = """{"error":{"type":"RateLimitError","message":"Slow down: you are close to your usage limits."}}""";

        var error = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.OpenAi, 429, body, headers: null, apiKey: null, retainProviderText: true);

        Assert.Equal(AiProviderErrorClass.RateLimited, error.Class);
    }

    [Fact]
    public void Parse_OpenCodeEchoedPromptTextOnA400_NeverFlipsTheClassToQuota()
    {
        // A 400 can echo learner text; for the OpenAI dialect the message phrases are ignored there.
        var body = """{"error":{"type":"ModelError","message":"Bad request: your spend limit and out of credits"}}""";

        var error = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.OpenAi, 400, body, headers: null, apiKey: null, retainProviderText: true);

        Assert.Equal(AiProviderErrorClass.InvalidRequest, error.Class);
    }

    [Fact]
    public void Parse_OpenCodeKeyShapedTokenInTheMessage_IsRedacted_EvenWhenItIsNotTheLiveKey()
    {
        // Built at runtime: a deliberately fake key of the gateway's shape, never a real credential.
        var fakeKey = string.Concat("oc", "_sk_", "TESTFAKE", new string('0', 16));
        var body = JsonSerializer.Serialize(new { error = new { type = "AuthError", message = $"rejected {fakeKey} for this request" } });

        var error = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.OpenAi, 401, body, headers: null, apiKey: "some-other-live-key-12345", retainProviderText: true);

        Assert.Equal(AiProviderErrorClass.Auth, error.Class);
        Assert.NotNull(error.Message);
        Assert.DoesNotContain(fakeKey, error.Message);
        Assert.DoesNotContain("TESTFAKE", error.Message);
        Assert.Contains("***REDACTED***", error.Message);
    }

    [Theory]
    [InlineData(AiProviderErrorDialect.OpenAi, 429, OpenAiInsufficientQuota, AiProviderErrorClass.QuotaExhausted, "insufficient_quota", "insufficient_quota")]
    [InlineData(AiProviderErrorDialect.OpenAi, 429, OpenAiRateLimitMentioningBilling, AiProviderErrorClass.RateLimited, "requests", "rate_limit_exceeded")]
    [InlineData(AiProviderErrorDialect.OpenAi, 401, OpenAiInvalidKey, AiProviderErrorClass.Auth, "invalid_request_error", "invalid_api_key")]
    [InlineData(AiProviderErrorDialect.OpenAi, 403, OpenAiCountry, AiProviderErrorClass.Auth, "request_forbidden", "unsupported_country_region_territory")]
    [InlineData(AiProviderErrorDialect.OpenAi, 400, OpenAiUnknownParameter, AiProviderErrorClass.InvalidRequest, "invalid_request_error", "unknown_parameter")]
    [InlineData(AiProviderErrorDialect.OpenAi, 404, OpenAiModelNotFound, AiProviderErrorClass.InvalidRequest, "invalid_request_error", "model_not_found")]
    [InlineData(AiProviderErrorDialect.OpenAi, 503, OpenAiOverloaded, AiProviderErrorClass.Overloaded, "server_error", null)]
    [InlineData(AiProviderErrorDialect.OpenAi, 500, OpenAiServerError, AiProviderErrorClass.ServerError, "server_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 400, AnthropicCreditBalance, AiProviderErrorClass.QuotaExhausted, "invalid_request_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 400, AnthropicUsageLimits, AiProviderErrorClass.QuotaExhausted, "invalid_request_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 402, AnthropicBillingError, AiProviderErrorClass.QuotaExhausted, "billing_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 400, AnthropicMaxTokens, AiProviderErrorClass.InvalidRequest, "invalid_request_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 400, AnthropicBetaHeader, AiProviderErrorClass.InvalidRequest, "invalid_request_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 401, AnthropicAuthentication, AiProviderErrorClass.Auth, "authentication_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 403, AnthropicPermission, AiProviderErrorClass.Auth, "permission_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 429, AnthropicRateLimit, AiProviderErrorClass.RateLimited, "rate_limit_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 529, AnthropicOverloaded, AiProviderErrorClass.Overloaded, "overloaded_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 500, AnthropicApiError, AiProviderErrorClass.ServerError, "api_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 504, AnthropicTimeout, AiProviderErrorClass.ServerError, "timeout_error", null)]
    [InlineData(AiProviderErrorDialect.Anthropic, 429, SidecarQuota, AiProviderErrorClass.QuotaExhausted, "rate_limit_error", "quota_exceeded")]
    [InlineData(AiProviderErrorDialect.Anthropic, 502, SidecarEngineError, AiProviderErrorClass.ServerError, null, "engine_error")]
    [InlineData(AiProviderErrorDialect.Anthropic, 503, SidecarLaneBusy, AiProviderErrorClass.Overloaded, "overloaded_error", "lane_busy")]
    [InlineData(AiProviderErrorDialect.Anthropic, 401, SidecarAuthExpired, AiProviderErrorClass.Auth, "authentication_error", "auth_expired")]
    [InlineData(AiProviderErrorDialect.Gemini, 429, GeminiPerDayQuota, AiProviderErrorClass.QuotaExhausted, "resource_exhausted", "generaterequestsperdayperprojectpermodel-freetier")]
    [InlineData(AiProviderErrorDialect.Gemini, 429, GeminiPerMinuteQuota, AiProviderErrorClass.RateLimited, "resource_exhausted", "generaterequestsperminuteperprojectpermodel-freetier")]
    [InlineData(AiProviderErrorDialect.Gemini, 429, GeminiNoDetails, AiProviderErrorClass.RateLimited, "resource_exhausted", null)]
    [InlineData(AiProviderErrorDialect.Gemini, 400, GeminiInvalidKey, AiProviderErrorClass.Auth, "invalid_argument", "api_key_invalid")]
    [InlineData(AiProviderErrorDialect.Gemini, 400, GeminiUnknownField, AiProviderErrorClass.InvalidRequest, "invalid_argument", null)]
    [InlineData(AiProviderErrorDialect.Gemini, 403, GeminiPermissionDenied, AiProviderErrorClass.Auth, "permission_denied", null)]
    [InlineData(AiProviderErrorDialect.Gemini, 503, GeminiUnavailable, AiProviderErrorClass.Overloaded, "unavailable", null)]
    [InlineData(AiProviderErrorDialect.Gemini, 500, GeminiInternal, AiProviderErrorClass.ServerError, "internal", null)]
    // Anchored on Gemini's own FAILED_PRECONDITION status token (not on free text alone), so it keeps
    // classifying on HTTP 400 even though message phrases are ignored there.
    [InlineData(AiProviderErrorDialect.Gemini, 400, GeminiBillingPrecondition, AiProviderErrorClass.QuotaExhausted, "failed_precondition", null)]
    public void Parse_ClassifiesVendorErrorBodies(
        AiProviderErrorDialect dialect,
        int status,
        string body,
        AiProviderErrorClass expectedClass,
        string? expectedType,
        string? expectedCode)
    {
        var error = AiProviderErrorParser.Parse(dialect, status, body, headers: null, apiKey: null, retainProviderText: true);

        Assert.Equal(expectedClass, error.Class);
        Assert.Equal(status, error.HttpStatus);
        Assert.Equal(expectedType, error.Type);
        Assert.Equal(expectedCode, error.Code);
    }

    // ── Message phrases must not decide the class of a rejected OpenAI / Gemini request ──
    // The live-voice mint calls carry learner-controlled text (an SDP offer, the instructions) and a
    // 400/413/415/422 reply can echo it back. A crafted "spend limit" must not hard-open the breaker.

    [Theory]
    [InlineData(AiProviderErrorDialect.OpenAi, 400)]
    [InlineData(AiProviderErrorDialect.OpenAi, 413)]
    [InlineData(AiProviderErrorDialect.OpenAi, 415)]
    [InlineData(AiProviderErrorDialect.OpenAi, 422)]
    [InlineData(AiProviderErrorDialect.Gemini, 400)]
    [InlineData(AiProviderErrorDialect.Gemini, 413)]
    [InlineData(AiProviderErrorDialect.Gemini, 415)]
    [InlineData(AiProviderErrorDialect.Gemini, 422)]
    public void Parse_OpenAiAndGeminiRejectedRequests_IgnoreEchoedQuotaAuthAndOverloadPhrases(
        AiProviderErrorDialect dialect, int status)
    {
        foreach (var echoed in new[]
                 {
                     "spend limit", "usage limits", "credit balance is too low", "out of credits",
                     "exceeded your current quota", "api key not valid", "account has been disabled",
                     "the model is overloaded",
                 })
        {
            var body = JsonSerializer.Serialize(new
            {
                error = new { type = "invalid_request_error", message = $"Invalid SDP. Expect line: v= Got: {echoed}" },
            });

            var error = AiProviderErrorParser.Parse(dialect, status, body, headers: null, apiKey: null, retainProviderText: false);

            Assert.Equal(AiProviderErrorClass.InvalidRequest, error.Class);
        }
    }

    [Theory]
    [InlineData(AiProviderErrorDialect.OpenAi, 400, "insufficient_quota", "insufficient_quota", AiProviderErrorClass.QuotaExhausted)]
    [InlineData(AiProviderErrorDialect.OpenAi, 400, "authentication_error", "invalid_api_key", AiProviderErrorClass.Auth)]
    [InlineData(AiProviderErrorDialect.OpenAi, 422, "overloaded_error", null, AiProviderErrorClass.Overloaded)]
    [InlineData(AiProviderErrorDialect.Gemini, 400, "invalid_argument", "api_key_invalid", AiProviderErrorClass.Auth)]
    public void Parse_OpenAiAndGeminiRejectedRequests_StillClassifyFromTheProviderTokens(
        AiProviderErrorDialect dialect, int status, string type, string? code, AiProviderErrorClass expected)
    {
        var body = JsonSerializer.Serialize(new { error = new { type, code, message = "anything at all" } });

        var error = AiProviderErrorParser.Parse(dialect, status, body, headers: null, apiKey: null, retainProviderText: false);

        Assert.Equal(expected, error.Class);
    }

    [Theory]
    [InlineData(AiProviderErrorDialect.OpenAi, 429, "You have reached your specified API usage limits.", AiProviderErrorClass.QuotaExhausted)]
    [InlineData(AiProviderErrorDialect.OpenAi, 500, "The engine is currently overloaded, please try again later", AiProviderErrorClass.Overloaded)]
    [InlineData(AiProviderErrorDialect.Gemini, 429, "Your monthly spend limit has been reached.", AiProviderErrorClass.QuotaExhausted)]
    [InlineData(AiProviderErrorDialect.Gemini, 500, "API key not valid. Please pass a valid API key.", AiProviderErrorClass.Auth)]
    [InlineData(AiProviderErrorDialect.Anthropic, 400, "You have reached your specified API usage limits.", AiProviderErrorClass.QuotaExhausted)]
    [InlineData(AiProviderErrorDialect.Anthropic, 422, "Your credit balance is too low to access the Anthropic API.", AiProviderErrorClass.QuotaExhausted)]
    public void Parse_MessagePhrasesStillDecideTheClass_ForAnthropicAndForStatusesThatCannotEchoTheRequest(
        AiProviderErrorDialect dialect, int status, string message, AiProviderErrorClass expected)
    {
        var body = JsonSerializer.Serialize(new { error = new { message } });

        var error = AiProviderErrorParser.Parse(dialect, status, body, headers: null, apiKey: null, retainProviderText: true);

        Assert.Equal(expected, error.Class);
    }

    [Theory]
    [InlineData(1960)]
    [InlineData(1985)]
    [InlineData(1990)]
    public void SanitizeMessage_RedactsBeforeItCutsTheRawText(int secretStart)
    {
        // The 2000 character raw cap used to run BEFORE redaction: a secret starting just before it was cut
        // to a fragment too short for any pattern, and a run of whitespace ahead of it (collapsed later)
        // let that fragment through the 300 character cap.
        const string apiKey = "zq9-secret-key-abcdef123456";
        const string patternSecret = "sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789";

        foreach (var secret in new[] { apiKey, patternSecret })
        {
            var sanitised = AiProviderErrorParser.SanitizeMessage(new string(' ', secretStart) + secret, apiKey, headOnly: false);

            Assert.NotNull(sanitised);
            Assert.DoesNotContain("zq9", sanitised);
            Assert.DoesNotContain("sk-ant", sanitised);
        }
    }

    [Theory]
    [InlineData(401, AiProviderErrorClass.Auth)]
    [InlineData(403, AiProviderErrorClass.Auth)]
    [InlineData(402, AiProviderErrorClass.QuotaExhausted)]
    [InlineData(400, AiProviderErrorClass.InvalidRequest)]
    [InlineData(404, AiProviderErrorClass.InvalidRequest)]
    [InlineData(413, AiProviderErrorClass.InvalidRequest)]
    [InlineData(422, AiProviderErrorClass.InvalidRequest)]
    [InlineData(408, AiProviderErrorClass.ServerError)]
    [InlineData(429, AiProviderErrorClass.RateLimited)]
    [InlineData(503, AiProviderErrorClass.Overloaded)]
    [InlineData(529, AiProviderErrorClass.Overloaded)]
    [InlineData(500, AiProviderErrorClass.ServerError)]
    [InlineData(502, AiProviderErrorClass.ServerError)]
    [InlineData(504, AiProviderErrorClass.ServerError)]
    [InlineData(599, AiProviderErrorClass.ServerError)]
    [InlineData(200, AiProviderErrorClass.Unknown)]
    public void Parse_WithoutBody_FallsBackToTheStatusClass(int status, AiProviderErrorClass expected)
    {
        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.Generic, status, body: null, headers: null, apiKey: null, retainProviderText: true);

        Assert.Equal(expected, error.Class);
        Assert.Null(error.Message);
        Assert.Null(error.Type);
        Assert.Null(error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("""{"status":"nope"}""")]
    [InlineData("""[{"error":{"type":"insufficient_quota"}}]""")]
    [InlineData("""{"error":""")]
    [InlineData("""{"error":{"message":42,"type":["x"],"code":{"a":1}}}""")]
    public void Parse_UnusableBodies_DegradeToStatusOnly(string body)
    {
        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.OpenAi, 429, body, headers: null, apiKey: null, retainProviderText: true);

        Assert.Equal(AiProviderErrorClass.RateLimited, error.Class);
        Assert.Null(error.Message);
        Assert.Null(error.Type);
        Assert.Null(error.Code);
    }

    [Fact]
    public void Parse_OversizedBody_IsNotParsed()
    {
        var body = "{\"error\":{\"type\":\"insufficient_quota\",\"message\":\"" + new string('x', 70_000) + "\"}}";

        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.OpenAi, 500, body, headers: null, apiKey: null, retainProviderText: true);

        Assert.Equal(AiProviderErrorClass.ServerError, error.Class);
        Assert.Null(error.Message);
        Assert.Null(error.Type);
    }

    [Fact]
    public void Parse_DeeplyNestedBody_DoesNotThrow()
    {
        var body = string.Concat(Enumerable.Repeat("{\"a\":", 200)) + "1" + new string('}', 200);

        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.Generic, 503, body, headers: null, apiKey: null, retainProviderText: true);

        Assert.Equal(AiProviderErrorClass.Overloaded, error.Class);
        Assert.Null(error.Message);
    }

    [Fact]
    public void Parse_StringErrorAndFastApiDetail_KeepOnlyTheMessage()
    {
        var stringError = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.Generic, 400, """{"error":"model is required"}""", null, null, true);
        var fastApi = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.Generic, 422, """{"detail":"field required"}""", null, null, true);

        Assert.Equal("model is required", stringError.Message);
        Assert.Equal(AiProviderErrorClass.InvalidRequest, stringError.Class);
        Assert.Equal("field required", fastApi.Message);
        Assert.Equal(AiProviderErrorClass.InvalidRequest, fastApi.Class);
    }

    [Fact]
    public void Parse_LongMessage_IsCappedAtThreeHundredCharacters()
    {
        var body = JsonSerializer.Serialize(new { error = new { type = "invalid_request_error", message = new string('m', 2000) } });

        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.Anthropic, 400, body, null, null, retainProviderText: true);

        Assert.NotNull(error.Message);
        Assert.True(error.Message!.Length <= 300, $"message length was {error.Message.Length}");
    }

    [Fact]
    public void Parse_RedactsSecretsBeforeTruncating()
    {
        const string apiKey = "zq9-secret-key-abcdef123456";
        var message = "bad key " + apiKey
            + " and sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789"
            + " and sk-proj-ABCDEFGHIJKLMNOPQRSTUVWX1234"
            + " and AIzaSyDabcdefghijklmnopqrstuvwxyz0123456";
        var body = JsonSerializer.Serialize(new { error = new { type = "invalid_request_error", message } });

        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.Anthropic, 400, body, null, apiKey, retainProviderText: true);

        Assert.NotNull(error.Message);
        Assert.Contains("***REDACTED***", error.Message);
        Assert.DoesNotContain(apiKey, error.Message);
        Assert.DoesNotContain("sk-ant-", error.Message);
        Assert.DoesNotContain("sk-proj-", error.Message);
        Assert.DoesNotContain("AIza", error.Message);
    }

    [Fact]
    public void Parse_SecretStraddlingTheCap_IsNeverPartiallyKept()
    {
        const string apiKey = "zq9-secret-key-abcdef123456";
        var message = new string('a', 290) + " " + apiKey;
        var body = JsonSerializer.Serialize(new { error = new { type = "invalid_request_error", message } });

        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.Anthropic, 400, body, null, apiKey, retainProviderText: true);

        Assert.NotNull(error.Message);
        Assert.True(error.Message!.Length <= 300);
        Assert.DoesNotContain("zq9", error.Message);
    }

    [Fact]
    public void Parse_CollapsesControlCharactersAndAngleBrackets()
    {
        var message = "line1\r\nline2\t<script>alert(1)</script>\u0007bell";
        var body = JsonSerializer.Serialize(new { error = new { type = "api_error", message } });

        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.Anthropic, 500, body, null, null, retainProviderText: true);

        Assert.Equal("line1 line2 script alert(1) /script bell", error.Message);
    }

    [Fact]
    public void Parse_TypeAndCodeAreLowerCasedAllowListedTokens()
    {
        var mixedCase = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.Generic, 400,
            """{"error":{"type":"Rate_Limit_Error","code":"Some.Code-1","message":"x"}}""", null, null, true);
        var hostile = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.Generic, 400,
            """{"error":{"type":"<b>bad</b>","code":"has space","message":"x"}}""", null, null, true);
        var tooLong = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.Generic, 400,
            "{\"error\":{\"type\":\"" + new string('a', 65) + "\",\"message\":\"x\"}}", null, null, true);

        Assert.Equal("rate_limit_error", mixedCase.Type);
        Assert.Equal("some.code-1", mixedCase.Code);
        Assert.Null(hostile.Type);
        Assert.Null(hostile.Code);
        Assert.Null(tooLong.Type);
    }

    [Fact]
    public void Parse_RequestId_ComesFromEitherHeaderOrTheBody()
    {
        using var anthropicStyle = new HttpResponseMessage(HttpStatusCode.BadRequest);
        anthropicStyle.Headers.TryAddWithoutValidation("request-id", "req_abc123");
        using var openAiStyle = new HttpResponseMessage(HttpStatusCode.BadRequest);
        openAiStyle.Headers.TryAddWithoutValidation("x-request-id", "req_def456");
        using var hostile = new HttpResponseMessage(HttpStatusCode.BadRequest);
        hostile.Headers.TryAddWithoutValidation("request-id", "req abc <script>");

        var fromRequestId = AiProviderErrorParser.Parse(AiProviderErrorDialect.Anthropic, 400, null, anthropicStyle.Headers, null, true);
        var fromXRequestId = AiProviderErrorParser.Parse(AiProviderErrorDialect.OpenAi, 400, null, openAiStyle.Headers, null, true);
        var fromBody = AiProviderErrorParser.Parse(AiProviderErrorDialect.Anthropic, 400, AnthropicCreditBalance, null, null, true);
        var rejected = AiProviderErrorParser.Parse(AiProviderErrorDialect.Anthropic, 400, null, hostile.Headers, null, true);

        Assert.Equal("req_abc123", fromRequestId.RequestId);
        Assert.Equal("req_def456", fromXRequestId.RequestId);
        Assert.Equal("req_011CTexample", fromBody.RequestId);
        Assert.Null(rejected.RequestId);
    }

    [Fact]
    public void Parse_RetryAfterHeader_IsCaptured()
    {
        using var response = new HttpResponseMessage((HttpStatusCode)429);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.Anthropic, 429, AnthropicRateLimit, response.Headers, null, true);

        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
        Assert.Equal(AiProviderErrorClass.RateLimited, error.Class);
    }

    [Fact]
    public void Parse_GeminiRetryInfo_ProvidesRetryAfter()
    {
        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.Gemini, 429, GeminiPerMinuteQuota, null, null, true);

        Assert.Equal(TimeSpan.FromSeconds(33), error.RetryAfter);
        Assert.Equal(AiProviderErrorClass.RateLimited, error.Class);
    }

    [Fact]
    public void Parse_WithoutRetainingVendorText_KeepsOnlyTheHeadBeforeTheFirstColon()
    {
        var quota = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.Anthropic, 429,
            """{"error":{"code":"quota_exceeded","type":"rate_limit_error","message":"Claude subscription quota/rate limit: the candidate said an evidence quote here"}}""",
            null, null, retainProviderText: false);
        var engine = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.Anthropic, 502,
            """{"error":{"code":"engine_error","message":"claude exited 1: transcript says evidence quote"}}""",
            null, null, retainProviderText: false);
        var noColon = AiProviderErrorParser.Parse(
            AiProviderErrorDialect.Anthropic, 502,
            "{\"error\":{\"code\":\"engine_error\",\"message\":\"" + new string('y', 200) + "\"}}",
            null, null, retainProviderText: false);

        Assert.Equal(AiProviderErrorClass.QuotaExhausted, quota.Class);
        Assert.Equal("Claude subscription quota/rate limit", quota.Message);
        Assert.Equal("claude exited 1", engine.Message);
        Assert.DoesNotContain("evidence quote", engine.Message);
        Assert.Equal(60, noColon.Message!.Length);
    }

    [Fact]
    public void Parse_RetainingVendorText_KeepsTheWholeSanitisedMessage()
    {
        var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.Anthropic, 400, AnthropicCreditBalance, null, null, retainProviderText: true);

        Assert.Equal(
            "Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to upgrade or purchase credits.",
            error.Message);
    }

    [Theory]
    [InlineData(AiProviderErrorClass.QuotaExhausted, "quota_exhausted")]
    [InlineData(AiProviderErrorClass.RateLimited, "rate_limited")]
    [InlineData(AiProviderErrorClass.InvalidRequest, "invalid_request")]
    [InlineData(AiProviderErrorClass.Auth, "auth")]
    [InlineData(AiProviderErrorClass.Overloaded, "overloaded")]
    [InlineData(AiProviderErrorClass.ServerError, "server_error")]
    [InlineData(AiProviderErrorClass.Network, "network")]
    [InlineData(AiProviderErrorClass.Unknown, "unknown")]
    public void ToCode_ReturnsTheStableCode(AiProviderErrorClass errorClass, string expected)
    {
        Assert.Equal(expected, errorClass.ToCode());
    }

    // ── Classify(Exception) ─────────────────────────────────────────────

    [Fact]
    public void Classify_TransportFailures_AreNetwork()
    {
        Assert.Equal(AiProviderErrorClass.Network,
            AiProviderErrorParser.Classify(new HttpRequestException("connection refused", new SocketException())));
        Assert.Equal(AiProviderErrorClass.Network,
            AiProviderErrorParser.Classify(new HttpRequestException("no route")));
        Assert.Equal(AiProviderErrorClass.Network,
            AiProviderErrorParser.Classify(new TaskCanceledException("timed out", new TimeoutException())));
        Assert.Equal(AiProviderErrorClass.Network,
            AiProviderErrorParser.Classify(new IOException("connection reset")));
        Assert.Equal(AiProviderErrorClass.Network,
            AiProviderErrorParser.Classify(new InvalidOperationException("wrapper", new SocketException())));
    }

    [Fact]
    public void Classify_CallerCancellation_IsNotANetworkFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Equal(AiProviderErrorClass.Unknown,
            AiProviderErrorParser.Classify(new OperationCanceledException(cts.Token)));
    }

    [Fact]
    public void Classify_HttpRequestExceptionWithStatus_UsesTheStatusClass()
    {
        Assert.Equal(AiProviderErrorClass.RateLimited,
            AiProviderErrorParser.Classify(new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests)));
        Assert.Equal(AiProviderErrorClass.Auth,
            AiProviderErrorParser.Classify(new HttpRequestException("denied", null, HttpStatusCode.Unauthorized)));
    }

    [Fact]
    public void Classify_TypedException_UsesItsErrorClass()
    {
        var withProviderError = new AiProviderHttpException(
            "Anthropic", 400, "Bad Request", null,
            new AiProviderError(AiProviderErrorClass.QuotaExhausted, 400, "invalid_request_error", null, null, null, null));
        var statusOnly = new AiProviderHttpException("Anthropic", 529, "Overloaded");

        Assert.Equal(AiProviderErrorClass.QuotaExhausted, AiProviderErrorParser.Classify(withProviderError));
        Assert.Equal(AiProviderErrorClass.Overloaded, AiProviderErrorParser.Classify(statusOnly));
        Assert.Equal(AiProviderErrorClass.QuotaExhausted,
            AiProviderErrorParser.Classify(new InvalidOperationException("wrapped", withProviderError)));
    }

    [Fact]
    public void Classify_UntypedProviderMessages_UseTheLegacyConvention()
    {
        Assert.Equal(AiProviderErrorClass.QuotaExhausted,
            AiProviderErrorParser.Classify(new InvalidOperationException(
                "AI provider call failed: HTTP 429 Too Many Requests. You exceeded your current quota, please check your plan and billing details.")));
        Assert.Equal(AiProviderErrorClass.ServerError,
            AiProviderErrorParser.Classify(new InvalidOperationException("AI provider call failed: HTTP 502 Bad Gateway.")));
        Assert.Equal(AiProviderErrorClass.Overloaded,
            AiProviderErrorParser.Classify(new InvalidOperationException("HTTP 503 Provider circuit is open.")));
        Assert.Equal(AiProviderErrorClass.Unknown,
            AiProviderErrorParser.Classify(new Exception("something else entirely")));
    }
}
