using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

public class PaymentCallbackHmacTests
{
    [Fact]
    public void FawaterakHashMatches_AcceptsOfficialHmacFormat()
    {
        // Official Fawaterak callback format:
        // HMAC-SHA256("InvoiceId={id}&InvoiceKey={key}&PaymentMethod={method}", vendorKey)
        const string hashKey = "test-hash-key";
        const string invoiceId = "1000430";
        const string invoiceKey = "69zpnFIcIPYNBwG";
        const string paymentMethod = "Card";
        var hmac = PaymentCallbackHmac.HmacSha256Hex(
            hashKey,
            $"InvoiceId={invoiceId}&InvoiceKey={invoiceKey}&PaymentMethod={paymentMethod}");

        Assert.True(PaymentCallbackHmac.FawaterakHashMatches(hashKey, invoiceId, invoiceKey, paymentMethod, hmac));
    }

    [Fact]
    public void FawaterakHashMatches_RejectsWrongPaymentMethodInSignature()
    {
        const string hashKey = "test-hash-key";
        const string invoiceId = "1000430";
        const string invoiceKey = "69zpnFIcIPYNBwG";
        var hmac = PaymentCallbackHmac.HmacSha256Hex(
            hashKey,
            $"InvoiceId={invoiceId}&InvoiceKey={invoiceKey}&PaymentMethod=Fawry");

        Assert.False(PaymentCallbackHmac.FawaterakHashMatches(hashKey, invoiceId, invoiceKey, "Card", hmac));
    }

    [Fact]
    public void FawaterakHashMatches_AcceptsLegacyHmacSha256OfInvoiceIdAndKey()
    {
        const string hashKey = "test-hash-key";
        const string invoiceId = "inv-1";
        const string invoiceKey = "key-1";
        var hmac = PaymentCallbackHmac.HmacSha256Hex(hashKey, invoiceId + invoiceKey);

        Assert.True(PaymentCallbackHmac.FawaterakHashMatches(hashKey, invoiceId, invoiceKey, "Card", hmac));
    }

    [Fact]
    public void FawaterakHashMatches_AcceptsLegacySha256OfInvoiceIdKeyAndHashKey()
    {
        const string hashKey = "test-hash-key";
        const string invoiceId = "inv-1";
        const string invoiceKey = "key-1";
        var sha = PaymentCallbackHmac.Sha256Hex(invoiceId + invoiceKey + hashKey);

        Assert.True(PaymentCallbackHmac.FawaterakHashMatches(hashKey, invoiceId, invoiceKey, "Card", sha));
    }

    [Fact]
    public void FawaterakHashMatches_RejectsTamperedHash()
    {
        Assert.False(PaymentCallbackHmac.FawaterakHashMatches("key", "inv", "ik", "Card", "deadbeef"));
    }

    [Fact]
    public void FawaterakCancelHashMatches_AcceptsOfficialCancelFormat()
    {
        const string hashKey = "test-hash-key";
        const string referenceId = "778586510";
        const string paymentMethod = "Fawry";
        var hmac = PaymentCallbackHmac.HmacSha256Hex(hashKey, $"referenceId={referenceId}&PaymentMethod={paymentMethod}");

        Assert.True(PaymentCallbackHmac.FawaterakCancelHashMatches(hashKey, referenceId, paymentMethod, hmac));
        Assert.False(PaymentCallbackHmac.FawaterakCancelHashMatches(hashKey, referenceId, "Aman", hmac));
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
