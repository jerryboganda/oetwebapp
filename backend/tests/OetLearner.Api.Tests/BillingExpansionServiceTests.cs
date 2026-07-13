using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Tests;

public class BillingExpansionServiceTests
{
    private static LearnerDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LearnerDbContext(options);
    }

    private static ManualPaymentService NewManualPaymentService(LearnerDbContext db)
        => new(db, new MemoryFileStorage());

    /// <summary>A valid PNG byte sequence (8-byte signature + salt) so the proof
    /// magic-byte validation passes. Same salt → same bytes → same SHA-256 (for
    /// duplicate-detection tests); a different salt yields a different hash.</summary>
    private static byte[] ValidProof(string salt = "seed")
        => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(Encoding.UTF8.GetBytes(salt)).ToArray();

    private static ManualPaymentSubmitRequest ManualRequest(string reference)
        => new(
            QuoteId: null,
            AmountAmount: 100m,
            Currency: "GBP",
            Method: "uk_monzo_transfer",
            Reference: reference,
            ProofUrl: "",
            CandidateFullName: "Candidate One",
            CandidateEmail: "candidate@example.com",
            CandidateWhatsApp: "+447961725989",
            CourseName: "OET Premium",
            CourseId: "plan_basic",
            PaymentCategory: "international");

    private static void AddPlan(LearnerDbContext db, int bundledAiCredits = 0)
    {
        var now = DateTimeOffset.UtcNow;
        db.BillingPlans.Add(new BillingPlan
        {
            Id = "plan_basic",
            Code = "plan_basic",
            Name = "OET Premium",
            Price = 100m,
            Currency = "GBP",
            Interval = "one_time",
            DurationMonths = 6,
            AccessDurationDays = 180,
            BundledAiCredits = bundledAiCredits,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static void SeedManualPaymentTemplates(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var code in new[] { "manual_payment_received", "manual_payment_approved", "manual_payment_rejected" })
        {
            db.BillingNotificationTemplates.Add(new BillingNotificationTemplate
            {
                Id = code,
                Code = code,
                Channel = "email",
                LocaleTag = "en",
                Subject = code,
                BodyTemplate = "Hello {{fullName}} — {{courseName}} {{amount}} {{reason}}",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }

    private static (ManualPaymentService svc, CapturingBillingChannel channel) NewServiceWithNotifier(LearnerDbContext db)
    {
        var channel = new CapturingBillingChannel();
        var dispatcher = new BillingNotificationDispatcher(db, new IBillingNotificationChannel[] { channel }, NullLogger<BillingNotificationDispatcher>.Instance);
        var svc = new ManualPaymentService(db, new MemoryFileStorage(), dispatcher, NullLogger<ManualPaymentService>.Instance);
        return (svc, channel);
    }

    private static TaxRule UkVat() => new()
    {
        Id = "uk_vat", Country = "GB", Region = "UK", TaxType = "vat", DisplayName = "UK VAT",
        RatePercent = 20m, EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-30),
        ZeroRateForB2BReverseCharge = true, IsTaxInclusiveDisplay = true, IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static TaxRule EgVat() => new()
    {
        Id = "eg_vat", Country = "EG", Region = "EGYPT", TaxType = "vat", DisplayName = "Egypt VAT",
        RatePercent = 14m, EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-30),
        ZeroRateForB2BReverseCharge = true, IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task TaxResolver_ReturnsEmptyWhenCountryUnknown()
    {
        await using var db = NewContext(nameof(TaxResolver_ReturnsEmptyWhenCountryUnknown));
        var resolver = new TaxResolver(db);
        var breakdown = await resolver.ResolveAsync(new TaxResolutionRequest("US", null, 100m, "USD", "subscription", false), CancellationToken.None);
        Assert.True(breakdown.IsEmpty);
    }

    [Fact]
    public async Task TaxResolver_AppliesUkVatToB2C()
    {
        await using var db = NewContext(nameof(TaxResolver_AppliesUkVatToB2C));
        db.TaxRules.Add(UkVat());
        await db.SaveChangesAsync();

        var resolver = new TaxResolver(db);
        var breakdown = await resolver.ResolveAsync(new TaxResolutionRequest("GB", null, 100m, "GBP", "subscription", false), CancellationToken.None);

        Assert.Equal(20m, breakdown.TotalTaxAmount);
        Assert.Single(breakdown.Lines);
        Assert.Equal("vat", breakdown.Lines[0].TaxType);
        Assert.Equal(20m, breakdown.Lines[0].Amount);
    }

    [Fact]
    public async Task TaxResolver_ReverseChargesB2BWithForeignVatId()
    {
        await using var db = NewContext(nameof(TaxResolver_ReverseChargesB2BWithForeignVatId));
        db.TaxRules.Add(UkVat());
        await db.SaveChangesAsync();

        var resolver = new TaxResolver(db);
        var breakdown = await resolver.ResolveAsync(new TaxResolutionRequest("GB", "DE123456789", 100m, "GBP", "subscription", true), CancellationToken.None);

        Assert.Equal(0m, breakdown.TotalTaxAmount);
        Assert.Single(breakdown.Lines);
        Assert.Contains("reverse-charge", breakdown.Lines[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaxResolver_AppliesEgyptVat()
    {
        await using var db = NewContext(nameof(TaxResolver_AppliesEgyptVat));
        db.TaxRules.Add(EgVat());
        await db.SaveChangesAsync();

        var resolver = new TaxResolver(db);
        var breakdown = await resolver.ResolveAsync(new TaxResolutionRequest("EG", null, 100m, "EGP", "subscription", false), CancellationToken.None);

        Assert.Equal(14m, breakdown.TotalTaxAmount);
    }

    [Fact]
    public async Task ManualPaymentService_RejectsDuplicateProof()
    {
        await using var db = NewContext(nameof(ManualPaymentService_RejectsDuplicateProof));
        var svc = NewManualPaymentService(db);
        var proof = ValidProof("same-receipt");

        await svc.SubmitAsync("user_a", ManualRequest("REF-001"), proof, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitAsync("user_b", ManualRequest("REF-002"), proof, CancellationToken.None));
    }

    [Fact]
    public async Task ManualPaymentService_AllowsSameUserResubmittingSameProof()
    {
        await using var db = NewContext(nameof(ManualPaymentService_AllowsSameUserResubmittingSameProof));
        var svc = NewManualPaymentService(db);
        var proof = ValidProof("same-receipt");

        await svc.SubmitAsync("user_a", ManualRequest("REF-001"), proof, CancellationToken.None);
        var second = await svc.SubmitAsync("user_a", ManualRequest("REF-002"), proof, CancellationToken.None);

        Assert.Equal("user_a", second.UserId);
    }

    [Fact]
    public async Task ManualPaymentService_ApproveTransitionsStatus()
    {
        await using var db = NewContext(nameof(ManualPaymentService_ApproveTransitionsStatus));
        AddPlan(db);
        await db.SaveChangesAsync();
        var svc = NewManualPaymentService(db);
        var submitted = await svc.SubmitAsync("user_a", ManualRequest("REF"), ValidProof("approve"), CancellationToken.None);

        var approved = await svc.ApproveAsync(submitted.Id, "admin_1", "Verified", CancellationToken.None);

        Assert.Equal("paid", approved.Status);
        Assert.Equal("admin_1", approved.ReviewedByAdminId);
        Assert.NotNull(approved.AccessGrantedSubscriptionId);
    }

    [Fact]
    public async Task ManualPaymentService_RejectsAlreadyResolvedRequest()
    {
        await using var db = NewContext(nameof(ManualPaymentService_RejectsAlreadyResolvedRequest));
        AddPlan(db);
        await db.SaveChangesAsync();
        var svc = NewManualPaymentService(db);
        var submitted = await svc.SubmitAsync("user_a", ManualRequest("REF"), ValidProof("reject"), CancellationToken.None);

        await svc.ApproveAsync(submitted.Id, "admin", null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RejectAsync(submitted.Id, "admin", "late rejection", CancellationToken.None));
    }

    [Fact]
    public async Task ManualPaymentService_RejectsNonImageProof()
    {
        await using var db = NewContext(nameof(ManualPaymentService_RejectsNonImageProof));
        var svc = NewManualPaymentService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitAsync("user_a", ManualRequest("REF"), Encoding.UTF8.GetBytes("not-an-image-or-pdf"), CancellationToken.None));
    }

    [Fact]
    public async Task ManualPaymentService_SetStatus_PendingNeedsReviewRoundTrip()
    {
        await using var db = NewContext(nameof(ManualPaymentService_SetStatus_PendingNeedsReviewRoundTrip));
        var svc = NewManualPaymentService(db);
        var submitted = await svc.SubmitAsync("user_a", ManualRequest("REF"), ValidProof(), CancellationToken.None);

        var flagged = await svc.SetStatusAsync(submitted.Id, "admin", "needs_review", "verify id", CancellationToken.None);
        Assert.Equal("needs_review", flagged.Status);
        Assert.Null(flagged.ReviewedAt); // not a terminal review outcome

        var back = await svc.SetStatusAsync(submitted.Id, "admin", "pending", null, CancellationToken.None);
        Assert.Equal("pending", back.Status);
    }

    [Fact]
    public async Task ManualPaymentService_SetStatus_RejectsInvalidTargetAndTerminal()
    {
        await using var db = NewContext(nameof(ManualPaymentService_SetStatus_RejectsInvalidTargetAndTerminal));
        AddPlan(db);
        await db.SaveChangesAsync();
        var svc = NewManualPaymentService(db);
        var submitted = await svc.SubmitAsync("user_a", ManualRequest("REF"), ValidProof(), CancellationToken.None);

        // 'paid' is not a settable target here — only pending/needs_review.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SetStatusAsync(submitted.Id, "admin", "paid", null, CancellationToken.None));

        // Once approved (terminal), it cannot be re-opened.
        await svc.ApproveAsync(submitted.Id, "admin", null, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SetStatusAsync(submitted.Id, "admin", "needs_review", null, CancellationToken.None));
    }

    [Fact]
    public async Task ManualPaymentService_Approve_GrantsBundledAiCredits()
    {
        await using var db = NewContext(nameof(ManualPaymentService_Approve_GrantsBundledAiCredits));
        AddPlan(db, bundledAiCredits: 5);
        await db.SaveChangesAsync();
        var svc = NewManualPaymentService(db);
        var submitted = await svc.SubmitAsync("user_a", ManualRequest("REF"), ValidProof(), CancellationToken.None);

        var approved = await svc.ApproveAsync(submitted.Id, "admin", null, CancellationToken.None);

        var ledger = await db.AiCreditLedger.Where(e => e.UserId == "user_a").ToListAsync();
        Assert.Single(ledger);
        Assert.Equal(5, ledger[0].TokensDelta);
        Assert.Equal(AiCreditSource.Purchase, ledger[0].Source);
        Assert.Equal($"manual:{submitted.Id}:plan_basic", ledger[0].ReferenceId);

        var sub = await db.Subscriptions.FirstAsync(s => s.Id == approved.AccessGrantedSubscriptionId);
        Assert.Equal(5, sub.AiCreditsRemaining);
    }

    [Fact]
    public async Task ManualPaymentService_DispatchesNotifications()
    {
        await using var db = NewContext(nameof(ManualPaymentService_DispatchesNotifications));
        AddPlan(db);
        SeedManualPaymentTemplates(db);
        await db.SaveChangesAsync();
        var (svc, channel) = NewServiceWithNotifier(db);

        var submitted = await svc.SubmitAsync("user_a", ManualRequest("REF"), ValidProof("a"), CancellationToken.None);
        await svc.ApproveAsync(submitted.Id, "admin", null, CancellationToken.None);

        var rejected = await svc.SubmitAsync("user_b", ManualRequest("REF2"), ValidProof("b"), CancellationToken.None);
        await svc.RejectAsync(rejected.Id, "admin", "blurry screenshot", CancellationToken.None);

        var logs = await db.BillingNotificationDispatchLogs.ToListAsync();
        Assert.Contains(logs, l => l.EventCode == "manual_payment_received" && l.EventId == submitted.Id);
        Assert.Contains(logs, l => l.EventCode == "manual_payment_approved" && l.EventId == submitted.Id);
        Assert.Contains(logs, l => l.EventCode == "manual_payment_rejected" && l.EventId == rejected.Id);

        // The rejection reason flows through to the rendered email body.
        Assert.Contains(channel.Sent, m => m.body.Contains("blurry screenshot"));
    }

    [Fact]
    public async Task ManualPaymentService_NotificationFailureDoesNotBlockSubmit()
    {
        await using var db = NewContext(nameof(ManualPaymentService_NotificationFailureDoesNotBlockSubmit));
        // Dispatcher throws — the payment must still persist (best-effort guard).
        var svc = new ManualPaymentService(db, new MemoryFileStorage(), new ThrowingDispatcher(), NullLogger<ManualPaymentService>.Instance);

        var submitted = await svc.SubmitAsync("user_a", ManualRequest("REF"), ValidProof(), CancellationToken.None);

        Assert.Equal("pending", submitted.Status);
        Assert.NotNull(await db.ManualPaymentRequests.FindAsync(submitted.Id));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, "application/pdf")]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 }, "application/octet-stream")]
    public void ManualPaymentProof_SniffContentType(byte[] header, string expected)
    {
        Assert.Equal(expected, ManualPaymentProof.SniffContentType(header));
    }

    private static PaymentMethodConfig PmConfig(string key, bool active = true)
    {
        var now = DateTimeOffset.UtcNow;
        return new PaymentMethodConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Key = key,
            Label = key,
            Category = "international",
            Detail = "detail",
            Instructions = "do the thing",
            IsActive = active,
            DisplayOrder = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    [Fact]
    public async Task ManualPaymentService_RejectsUnknownMethod()
    {
        await using var db = NewContext(nameof(ManualPaymentService_RejectsUnknownMethod));
        db.PaymentMethodConfigs.Add(PmConfig("instapay_qr_link"));
        await db.SaveChangesAsync();
        var svc = NewManualPaymentService(db);

        var request = ManualRequest("REF") with { Method = "unknown_gateway" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitAsync("user_a", request, ValidProof("unknown"), CancellationToken.None));
    }

    [Fact]
    public async Task ManualPaymentService_AcceptsKnownActiveMethod()
    {
        await using var db = NewContext(nameof(ManualPaymentService_AcceptsKnownActiveMethod));
        db.PaymentMethodConfigs.Add(PmConfig("uk_monzo_transfer"));
        await db.SaveChangesAsync();
        var svc = NewManualPaymentService(db);

        var row = await svc.SubmitAsync("user_a", ManualRequest("REF"), ValidProof("known"), CancellationToken.None);
        Assert.Equal("uk_monzo_transfer", row.Method);
    }

    [Fact]
    public async Task ManualPaymentService_RejectsInactiveMethodWhenOthersActive()
    {
        await using var db = NewContext(nameof(ManualPaymentService_RejectsInactiveMethodWhenOthersActive));
        db.PaymentMethodConfigs.Add(PmConfig("instapay_qr_link"));            // active
        db.PaymentMethodConfigs.Add(PmConfig("uk_monzo_transfer", active: false)); // disabled
        await db.SaveChangesAsync();
        var svc = NewManualPaymentService(db);

        // ManualRequest uses uk_monzo_transfer, which is present but inactive.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitAsync("user_a", ManualRequest("REF"), ValidProof("inactive"), CancellationToken.None));
    }

    [Fact]
    public async Task ManualPaymentService_FallsBackToKnownKeysWhenTableEmpty()
    {
        await using var db = NewContext(nameof(ManualPaymentService_FallsBackToKnownKeysWhenTableEmpty));
        var svc = NewManualPaymentService(db);

        // Empty config table → fallback allowlist accepts the seeded key…
        var row = await svc.SubmitAsync("user_a", ManualRequest("REF"), ValidProof("fb-ok"), CancellationToken.None);
        Assert.Equal("uk_monzo_transfer", row.Method);

        // …but still rejects a method outside the fallback list.
        var bogus = ManualRequest("REF2") with { Method = "bogus_method" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitAsync("user_a", bogus, ValidProof("fb-bad"), CancellationToken.None));
    }

    [Fact]
    public void PaymentMethodConfigDto_FromEntity_MapsHasQrImageAndStripsKey()
    {
        var withQr = PmConfig("instapay_qr_link");
        withQr.QrImageKey = "billing/payment-methods/qr/instapay_qr_link-abc.bin";
        var dtoWith = OetLearner.Api.Endpoints.PaymentMethodConfigDto.FromEntity(withQr);
        Assert.True(dtoWith.HasQrImage);

        var withoutQr = PmConfig("qnb_egypt");
        var dtoWithout = OetLearner.Api.Endpoints.PaymentMethodConfigDto.FromEntity(withoutQr);
        Assert.False(dtoWithout.HasQrImage);
    }

    [Fact]
    public async Task DunningService_StartsActiveCampaign()
    {
        await using var db = NewContext(nameof(DunningService_StartsActiveCampaign));
        var svc = new DunningCampaignService(db);

        var campaign = await svc.StartAsync("sub_1", "user_a", "card_declined", "insufficient_funds", CancellationToken.None);

        Assert.Equal("active", campaign.Status);
        Assert.Equal("card_declined", campaign.LastFailureCode);
    }

    [Fact]
    public async Task DunningService_AdvanceMarksStepsCompleted()
    {
        await using var db = NewContext(nameof(DunningService_AdvanceMarksStepsCompleted));
        var svc = new DunningCampaignService(db);
        var campaign = await svc.StartAsync("sub_1", "user_a", null, null, CancellationToken.None);

        // Simulate the campaign started 4 days ago so day0/1/3 steps fire.
        campaign.StartedAt = DateTimeOffset.UtcNow.AddDays(-4);
        await db.SaveChangesAsync();

        await svc.AdvanceAsync(campaign, CancellationToken.None);

        Assert.Contains("day0_email", campaign.StepsCompletedCsv);
        Assert.Contains("day1_retry", campaign.StepsCompletedCsv);
        Assert.Contains("day3_retry_email", campaign.StepsCompletedCsv);
    }

    [Fact]
    public async Task DunningService_TerminalCancelAfter21Days()
    {
        await using var db = NewContext(nameof(DunningService_TerminalCancelAfter21Days));
        var svc = new DunningCampaignService(db);
        var campaign = await svc.StartAsync("sub_1", "user_a", null, null, CancellationToken.None);
        campaign.StartedAt = DateTimeOffset.UtcNow.AddDays(-22);
        await db.SaveChangesAsync();

        await svc.AdvanceAsync(campaign, CancellationToken.None);

        Assert.Equal("cancelled", campaign.Status);
        Assert.NotNull(campaign.CancelledAt);
    }

    [Fact]
    public async Task AffiliateService_AttributesFirstClickOnly()
    {
        await using var db = NewContext(nameof(AffiliateService_AttributesFirstClickOnly));
        db.Affiliates.AddRange(
            new Affiliate { Id = "a1", Code = "CODE1", OwnerName = "Ag1", ContactEmail = "a@b", CommissionPercent = 15m, PayoutThresholdAmount = 100m, Status = "active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow },
            new Affiliate { Id = "a2", Code = "CODE2", OwnerName = "Ag2", ContactEmail = "a2@b", CommissionPercent = 25m, PayoutThresholdAmount = 100m, Status = "active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var svc = new AffiliateService(db);
        await svc.AttributeUserAsync("u1", "CODE1", CancellationToken.None);
        await svc.AttributeUserAsync("u1", "CODE2", CancellationToken.None); // ignored — first-click wins

        var attribution = await db.AffiliateAttributions.FirstAsync(a => a.UserId == "u1");
        Assert.Equal("a1", attribution.AffiliateId);
    }

    [Fact]
    public async Task AffiliateService_AccruesAndReversesCommission()
    {
        await using var db = NewContext(nameof(AffiliateService_AccruesAndReversesCommission));
        db.Affiliates.Add(new Affiliate { Id = "a1", Code = "CODE", OwnerName = "Ag", ContactEmail = "a@b", CommissionPercent = 20m, PayoutThresholdAmount = 100m, Status = "active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var svc = new AffiliateService(db);
        await svc.AttributeUserAsync("u1", "CODE", CancellationToken.None);
        var commission = await svc.AccrueCommissionAsync("u1", "pt_1", 100m, "GBP", CancellationToken.None);

        Assert.NotNull(commission);
        Assert.Equal(20m, commission!.AmountAmount);
        Assert.Equal("accrued", commission.Status);

        await svc.ReverseCommissionAsync("pt_1", CancellationToken.None);
        var after = await db.AffiliateCommissions.FirstAsync(c => c.PaymentTransactionId == "pt_1");
        Assert.Equal("reversed", after.Status);
        Assert.NotNull(after.ReversedAt);
    }
}

internal sealed class MemoryFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public async Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        _files[key] = buffer.ToArray();
        return _files[key].LongLength;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        => Task.FromResult<Stream>(new MemoryStream(_files[key], writable: false));

    public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
        => Task.FromResult<Stream>(new MemoryStream());

    public Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_files.ContainsKey(key));
    }

    public Task<bool> DeleteAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_files.Remove(key));
    }

    public Task<long> LengthAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_files.TryGetValue(key, out var data) ? data.LongLength : 0);
    }

    public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_files.TryGetValue(sourceKey, out var data)) return Task.CompletedTask;
        if (!overwrite && _files.ContainsKey(destKey)) return Task.CompletedTask;
        _files[destKey] = data;
        _files.Remove(sourceKey);
        return Task.CompletedTask;
    }

    public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var keys = _files.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var key in keys) _files.Remove(key);
        return Task.FromResult(keys.Count);
    }

    public string? TryResolveLocalPath(string key) => null;
    public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
}

/// <summary>Records every dispatched message so tests can assert on rendered content.</summary>
internal sealed class CapturingBillingChannel : IBillingNotificationChannel
{
    public string Channel => "email";
    public List<(string userId, string subject, string body)> Sent { get; } = new();

    public Task SendAsync(string userId, string subject, string body, CancellationToken ct)
    {
        Sent.Add((userId, subject, body));
        return Task.CompletedTask;
    }
}

/// <summary>Dispatcher that always throws — proves the service's best-effort
/// notification guard never rolls back the persisted payment.</summary>
internal sealed class ThrowingDispatcher : IBillingNotificationDispatcher
{
    public Task DispatchAsync(BillingNotificationEvent evt, CancellationToken ct)
        => throw new InvalidOperationException("notification backend unavailable");
}
