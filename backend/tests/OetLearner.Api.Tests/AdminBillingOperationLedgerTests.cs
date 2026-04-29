using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class AdminBillingOperationLedgerTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public AdminBillingOperationLedgerTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task AdminBillingOperations_RequiresBillingReadPermission()
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.ContentRead);

            var response = await client.GetAsync("/v1/admin/billing/operations");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Fact]
    public async Task AdminBillingOperations_CreateRequiresBillingWritePermission()
    {
        var previous = Environment.GetEnvironmentVariable("Auth__UseDevelopmentAuth");
        Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", "true");
        try
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.BillingRead);

            var response = await client.PostAsJsonAsync("/v1/admin/billing/operations", new
            {
                userId = "billing-operation-write-user",
                operationType = "reconciliation_note",
                reason = "Permission gate check."
            });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Auth__UseDevelopmentAuth", previous);
        }
    }

    [Fact]
    public async Task AdminBillingOperations_CreateRefundRequestWritesSanitizedLedgerEventAndAudit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"billing-operation-user-{suffix}";
        var gatewayReference = $"refund-gateway-reference-{suffix}";
        var now = DateTimeOffset.UtcNow;

        await SeedLearnerAsync(userId, $"Billing Operation Learner {suffix}", now);

        var response = await _client.PostAsJsonAsync("/v1/admin/billing/operations", new
        {
            userId,
            operationType = "refund_request",
            amount = 49.5m,
            currency = "aud",
            paymentTransactionId = $"payment-transaction-{suffix}",
            invoiceId = $"invoice-{suffix}",
            subscriptionId = $"subscription-{suffix}",
            quoteId = $"quote-{suffix}",
            gateway = "stripe",
            gatewayReference,
            evidenceUrl = "https://support.example.test/evidence/refund-request",
            reason = "Learner requested a refund after duplicate payment.",
            adminNotes = "Validated against support ticket only."
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("payloadJson", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("metadataJson", body, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var operationId = root.GetProperty("id").GetString()!;
        Assert.Equal(userId, root.GetProperty("userId").GetString());
        Assert.Equal($"Billing Operation Learner {suffix}", root.GetProperty("learnerName").GetString());
        Assert.Equal("refund_request", root.GetProperty("operationType").GetString());
        Assert.Equal("open", root.GetProperty("status").GetString());
        Assert.Equal("AUD", root.GetProperty("currency").GetString());
        Assert.Equal(gatewayReference, root.GetProperty("gatewayReference").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var operation = await db.BillingOperations.AsNoTracking().SingleAsync(item => item.Id == operationId);
        Assert.Equal(49.5m, operation.Amount);
        Assert.Equal("AUD", operation.Currency);

        var billingEvent = await db.BillingEvents.AsNoTracking()
            .SingleAsync(item => item.EntityType == "BillingOperation" && item.EntityId == operationId && item.EventType == "billing_operation_created");
        Assert.DoesNotContain("adminNotes", billingEvent.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(gatewayReference, billingEvent.PayloadJson, StringComparison.OrdinalIgnoreCase);

        var audit = await db.AuditEvents.AsNoTracking()
            .SingleAsync(item => item.ResourceType == "BillingOperation" && item.ResourceId == operationId && item.Action == "Created Billing Operation");
        Assert.Contains("refund_request", audit.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("manual_payment", 0, null, "manual payment requires amount")]
    [InlineData("credit_adjustment", null, 0, "credit adjustment requires nonzero delta")]
    [InlineData("reconciliation_note", null, null, "reconciliation note requires reason")]
    [InlineData("refund_request", 10, null, "create status must stay open")]
    public async Task AdminBillingOperations_CreateRejectsInvalidPayloads(string operationType, int? amount, int? creditDelta, string caseName)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"billing-operation-invalid-{suffix}";
        await SeedLearnerAsync(userId, "Invalid Billing Operation Learner", DateTimeOffset.UtcNow);

        var response = await _client.PostAsJsonAsync("/v1/admin/billing/operations", new
        {
            userId,
            operationType,
            amount,
            currency = "AUD",
            creditDelta,
            reason = caseName.Contains("reason", StringComparison.OrdinalIgnoreCase) ? "" : "Invalid operation test.",
            status = caseName.Contains("status", StringComparison.OrdinalIgnoreCase) ? "completed" : null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("billing_operation_invalid", json.RootElement.GetProperty("code").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("fieldErrors").EnumerateArray());
    }

    [Fact]
    public async Task AdminBillingOperations_ListFiltersClampsPagingAndReturnsNewestFirst()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"billing-operation-list-{suffix}";
        var searchToken = $"operation-filter-{suffix}";
        var now = DateTimeOffset.UtcNow;
        await SeedLearnerAsync(userId, "Filtered Billing Learner", now);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.BillingOperations.AddRange(
                Operation($"bop-old-{suffix}", userId, "refund_request", "open", now.AddMinutes(-5), $"older {searchToken}"),
                Operation($"bop-new-{suffix}", userId, "refund_request", "open", now, $"newer {searchToken}"),
                Operation($"bop-other-{suffix}", userId, "manual_payment", "completed", now.AddMinutes(1), $"other {searchToken}"));
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/v1/admin/billing/operations?operationType=refund_request&status=open&userId={Uri.EscapeDataString(userId)}&search={Uri.EscapeDataString(searchToken)}&page=0&pageSize=500");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(100, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        Assert.Equal($"bop-new-{suffix}", root.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal($"bop-old-{suffix}", root.GetProperty("items")[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task AdminBillingOperations_ResolveUpdatesStatusAndWritesAuditAndEvent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = $"billing-operation-resolve-{suffix}";
        var operationId = $"bop-resolve-{suffix}";
        var now = DateTimeOffset.UtcNow;
        await SeedLearnerAsync(userId, "Resolve Billing Learner", now);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.BillingOperations.Add(Operation(operationId, userId, "credit_adjustment", "open", now, "Resolve adjustment", creditDelta: 5));
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync($"/v1/admin/billing/operations/{Uri.EscapeDataString(operationId)}/resolve", new
        {
            status = "completed",
            resolutionNotes = "Credits adjusted manually after finance approval."
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("completed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Credits adjusted manually after finance approval.", json.RootElement.GetProperty("resolutionNotes").GetString());

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var operation = await verifyDb.BillingOperations.AsNoTracking().SingleAsync(item => item.Id == operationId);
        Assert.Equal("completed", operation.Status);
        Assert.NotNull(operation.ResolvedAt);
        Assert.NotNull(operation.ResolvedByAdminId);

        Assert.True(await verifyDb.BillingEvents.AsNoTracking()
            .AnyAsync(item => item.EntityType == "BillingOperation" && item.EntityId == operationId && item.EventType == "billing_operation_resolved"));
        Assert.True(await verifyDb.AuditEvents.AsNoTracking()
            .AnyAsync(item => item.ResourceType == "BillingOperation" && item.ResourceId == operationId && item.Action == "Resolved Billing Operation"));
    }

    private async Task SeedLearnerAsync(string userId, string displayName, DateTimeOffset now)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (await db.Users.AnyAsync(user => user.Id == userId)) return;

        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = displayName,
            Email = $"{userId}@example.com",
            CreatedAt = now,
            LastActiveAt = now
        });
        await db.SaveChangesAsync();
    }

    private static BillingOperation Operation(
        string id,
        string userId,
        string operationType,
        string status,
        DateTimeOffset createdAt,
        string reason,
        int? creditDelta = null)
        => new()
        {
            Id = id,
            UserId = userId,
            OperationType = operationType,
            Status = status,
            Amount = operationType is "refund_request" or "manual_payment" ? 25m : null,
            Currency = "AUD",
            CreditDelta = creditDelta,
            Reason = reason,
            CreatedByAdminId = "admin-test",
            CreatedByAdminName = "Admin Test",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }