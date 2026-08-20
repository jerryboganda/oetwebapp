using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

public class PaymentCallbackHmacTests
{
    [Fact]
    public void FawaterakHashMatches_AcceptsHmacSha256OfInvoiceIdAndKey()
    {
        const string hashKey = "test-hash-key";
        const string invoiceId = "inv-1";
        const string invoiceKey = "key-1";
        var hmac = PaymentCallbackHmac.HmacSha256Hex(hashKey, invoiceId + invoiceKey);

        Assert.True(PaymentCallbackHmac.FawaterakHashMatches(hashKey, invoiceId, invoiceKey, hmac));
    }

    [Fact]
    public void FawaterakHashMatches_AcceptsSha256OfInvoiceIdKeyAndHashKey()
    {
        const string hashKey = "test-hash-key";
        const string invoiceId = "inv-1";
        const string invoiceKey = "key-1";
        var sha = PaymentCallbackHmac.Sha256Hex(invoiceId + invoiceKey + hashKey);

        Assert.True(PaymentCallbackHmac.FawaterakHashMatches(hashKey, invoiceId, invoiceKey, sha));
    }

    [Fact]
    public void FawaterakHashMatches_RejectsTamperedHash()
    {
        Assert.False(PaymentCallbackHmac.FawaterakHashMatches("key", "inv", "ik", "deadbeef"));
    }

    [Fact]
    public void WhopSignatureMatches_AcceptsTimestampedV1Header()
    {
        const string secret = "whop-secret";
        const string payload = "{\"type\":\"payment.succeeded\"}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = PaymentCallbackHmac.HmacSha256Hex(secret, $"{now}.{payload}");
        var header = $"t={now},v1={signature}";

        Assert.True(PaymentCallbackHmac.WhopSignatureMatches(secret, payload, header, now, 300));
    }

    [Fact]
    public void WhopSignatureMatches_RejectsReplayOutsideMaxAge()
    {
        const string secret = "whop-secret";
        const string payload = "{\"type\":\"payment.succeeded\"}";
        var old = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10_000;
        var signature = PaymentCallbackHmac.HmacSha256Hex(secret, $"{old}.{payload}");
        var header = $"t={old},v1={signature}";

        Assert.False(PaymentCallbackHmac.WhopSignatureMatches(secret, payload, header, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 300));
    }
}
