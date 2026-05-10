using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public sealed class DisableAntiforgeryUploadEndpointAuthorizationTests
{
    private const string VocabularyImportCsvHeader = "Term,Definition,ExampleSentence,Category,Difficulty,ProfessionId,ExamTypeCode,AmericanSpelling,AudioUrl,AudioSlowUrl,AudioSentenceUrl,AudioMediaAssetId,Collocations,RelatedTerms,SourceProvenance\n";

    private static readonly AdminUploadEndpoint[] AdminUploadEndpointCases =
    [
        new("POST", "/v1/admin/users/import", AdminPermissions.UsersWrite, AdminPermissions.UsersRead),
        new("POST", "/v1/admin/vocabulary/import/preview", AdminPermissions.ContentRead, AdminPermissions.ContentWrite),
        new("POST", "/v1/admin/vocabulary/import?dryRun=true", AdminPermissions.ContentWrite, AdminPermissions.ContentRead),
        new("POST", "/v1/admin/vocabulary/import/batches/missing-batch/reconcile", AdminPermissions.ContentRead, AdminPermissions.ContentWrite),
        new("PUT", "/v1/admin/uploads/missing-upload/parts/1", AdminPermissions.ContentWrite, AdminPermissions.ContentRead),
        new("POST", "/v1/admin/imports/zip", AdminPermissions.ContentWrite, AdminPermissions.ContentRead),
    ];

    public static IEnumerable<object[]> AdminUploadRequestEndpoints =>
        AdminUploadEndpointCases.Select(endpoint => new object[] { endpoint.Method, endpoint.Path });

    public static IEnumerable<object[]> AdminUploadWrongPermissionEndpoints =>
        AdminUploadEndpointCases.Select(endpoint => new object[] { endpoint.Method, endpoint.Path, endpoint.WrongPermission });

    public static IEnumerable<object[]> AdminUploadRequiredPermissionEndpoints =>
        AdminUploadEndpointCases.Select(endpoint => new object[] { endpoint.Method, endpoint.Path, endpoint.RequiredPermission });

    [Theory]
    [MemberData(nameof(AdminUploadRequestEndpoints))]
    public async Task AdminUploadEndpoints_RejectUnauthenticatedRequests(
        string method,
        string path)
    {
        using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest(method, path);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminUploadWrongPermissionEndpoints))]
    public async Task AdminUploadEndpoints_RejectAdminWithoutRequiredPermission(
        string method,
        string path,
        string wrongPermission)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateDebugAdminClient(factory, wrongPermission);
        using var request = CreateRequest(method, path);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminUploadRequiredPermissionEndpoints))]
    public async Task AdminUploadEndpoints_WithRequiredPermissionReachHandlerWithoutCsrfToken(
        string method,
        string path,
        string requiredPermission)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateDebugAdminClient(factory, requiredPermission);
        using var request = CreateRequest(method, path);

        var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static HttpClient CreateDebugAdminClient(TestWebApplicationFactory factory, string permission)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", permission);
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"admin-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Debug-Email", "admin-upload-test@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Upload Evidence Admin");
        return client;
    }

    private static HttpRequestMessage CreateRequest(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Content = path switch
        {
            "/v1/admin/users/import" => CsvMultipart(
                "email,firstName,lastName,role,profession\n",
                "users.csv"),
            var route when route.StartsWith("/v1/admin/vocabulary/import", StringComparison.Ordinal) => CsvMultipart(
                VocabularyImportCsvHeader + $"csrf-evidence-{Guid.NewGuid():N},Definition,Example.,medical,medium,medicine,oet,,,,,,,,\"src=evidence;p=1;row=1\"\n",
                "vocabulary.csv"),
            "/v1/admin/imports/zip" => BinaryMultipart([], "file", "empty.zip", "application/zip"),
            var route when route.StartsWith("/v1/admin/uploads/", StringComparison.Ordinal) => new ByteArrayContent("chunk"u8.ToArray()),
            _ => throw new InvalidOperationException($"No request body configured for {path}.")
        };

        return request;
    }

    private static MultipartFormDataContent CsvMultipart(string csv, string fileName)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);
        return content;
    }

    private static MultipartFormDataContent BinaryMultipart(byte[] bytes, string name, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, name, fileName);
        return content;
    }

    private sealed record AdminUploadEndpoint(
        string Method,
        string Path,
        string RequiredPermission,
        string WrongPermission);
}