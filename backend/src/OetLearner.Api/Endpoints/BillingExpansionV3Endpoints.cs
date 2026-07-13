using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin-configurable payment methods (the InstaPay / Vodafone / QNB / Stripe /
/// PayPal / Monzo list shown on the learner manual-payment page). Replaces the
/// previously hard-coded frontend array so account details and ordering can be
/// edited without a deploy. Mirrors the bank-account CRUD pattern in
/// <see cref="BillingExpansionV2Endpoints"/>.
/// </summary>
public static class BillingExpansionV3Endpoints
{
    public static IEndpointRouteBuilder MapBillingExpansionV3Endpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        var billing = v1.MapGroup("/billing").RequireAuthorization();
        billing.MapGet("/payment-methods", ListActivePaymentMethods);
        billing.MapGet("/payment-methods/{key}/qr", GetPaymentMethodQr);

        var admin = v1.MapGroup("/admin/billing");
        admin.MapGet("/payment-methods", ListPaymentMethods).RequireAuthorization("AdminBillingRead");
        admin.MapPost("/payment-methods", UpsertPaymentMethod).WithAdminWrite("AdminBillingCatalogWrite");
        admin.MapPost("/payment-methods/{key}/qr", UploadPaymentMethodQr).WithAdminWrite("AdminBillingCatalogWrite");
        admin.MapDelete("/payment-methods/{id}", DeletePaymentMethod).WithAdminWrite("AdminBillingCatalogWrite");

        return app;
    }

    // ── Learner-facing ────────────────────────────────────────────────

    private static async Task<Ok<List<PaymentMethodConfigDto>>> ListActivePaymentMethods(LearnerDbContext db, CancellationToken ct)
    {
        var rows = await db.PaymentMethodConfigs
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(PaymentMethodConfigDto.FromEntity).ToList());
    }

    /// <summary>
    /// Stream a payment-method QR image. Stored as an opaque blob with no recorded
    /// MIME, so the content type is sniffed from magic bytes (same as proof files).
    /// Requires authentication — the learner manual-payment page fetches it as a
    /// blob, so no bearer token leaks into an &lt;img src&gt; URL.
    /// </summary>
    private static async Task<IResult> GetPaymentMethodQr(string key, LearnerDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var imageKey = await db.PaymentMethodConfigs
            .Where(m => m.Key == key && m.IsActive)
            .Select(m => m.QrImageKey)
            .FirstOrDefaultAsync(ct);
        return await StreamQrAsync(imageKey, storage, ct);
    }

    // ── Admin CRUD ────────────────────────────────────────────────────

    private static async Task<Ok<List<PaymentMethodConfigDto>>> ListPaymentMethods(LearnerDbContext db, CancellationToken ct)
    {
        var rows = await db.PaymentMethodConfigs.OrderBy(m => m.DisplayOrder).ThenBy(m => m.Key).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(PaymentMethodConfigDto.FromEntity).ToList());
    }

    private static async Task<Results<Ok<PaymentMethodConfigDto>, BadRequest<string>>> UpsertPaymentMethod(
        PaymentMethodConfigUpsertRequest request, LearnerDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Label))
        {
            return TypedResults.BadRequest("Key and label are required.");
        }

        var key = request.Key.Trim();
        // Keep keys to a safe slug — they form part of the QR blob storage path and
        // the submitted Method value. (Path traversal is also blocked by the storage
        // layer; this rejects bad input early with a clear message.)
        if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9_-]{1,64}$"))
        {
            return TypedResults.BadRequest("Key must be a lowercase slug (a-z, 0-9, '_' or '-'), max 64 chars.");
        }
        var category = NormalizeCategory(request.Category);
        var now = DateTimeOffset.UtcNow;
        var row = await db.PaymentMethodConfigs.FirstOrDefaultAsync(m => m.Key == key, ct);
        if (row is null)
        {
            row = new PaymentMethodConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Key = key,
                CreatedAt = now,
            };
            db.PaymentMethodConfigs.Add(row);
        }

        row.Label = request.Label.Trim();
        row.Category = category;
        row.Detail = request.Detail ?? string.Empty;
        row.Meta = string.IsNullOrWhiteSpace(request.Meta) ? null : request.Meta;
        row.Instructions = request.Instructions ?? string.Empty;
        row.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note;
        row.ReferenceRule = request.ReferenceRule;
        row.ShowQr = request.ShowQr;
        row.IconName = string.IsNullOrWhiteSpace(request.IconName) ? null : request.IconName;
        row.IsActive = request.IsActive;
        row.DisplayOrder = request.DisplayOrder;
        row.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(PaymentMethodConfigDto.FromEntity(row));
    }

    private static async Task<Results<NoContent, NotFound>> DeletePaymentMethod(string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.PaymentMethodConfigs.FindAsync(new object?[] { id }, ct);
        if (row is null) return TypedResults.NotFound();
        db.PaymentMethodConfigs.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Upload a QR image for a payment method as base64 JSON (same proven path as
    /// manual-payment proof). The bytes are magic-byte validated and stored as an
    /// opaque blob; the key is saved on the config row.
    /// </summary>
    private static async Task<Results<Ok<PaymentMethodConfigDto>, BadRequest<string>, NotFound>> UploadPaymentMethodQr(
        string key, PaymentMethodQrUploadRequest request, LearnerDbContext db, IFileStorage storage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            return TypedResults.BadRequest("imageBase64 is required.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.ImageBase64);
        }
        catch (FormatException)
        {
            return TypedResults.BadRequest("imageBase64 is not valid base64.");
        }

        if (bytes.Length == 0 || bytes.Length > ManualPaymentProof.MaxProofBytes)
        {
            return TypedResults.BadRequest("QR image must be between 1 byte and 10 MB.");
        }
        if (!ManualPaymentProof.IsAllowedProof(bytes))
        {
            return TypedResults.BadRequest("QR image must be an image (JPG, PNG, GIF, WEBP).");
        }

        var row = await db.PaymentMethodConfigs.FirstOrDefaultAsync(m => m.Key == key, ct);
        if (row is null) return TypedResults.NotFound();

        var hashHex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var imageKey = $"billing/payment-methods/qr/{key}-{hashHex[..12]}.bin";
        await using (var stream = new MemoryStream(bytes))
        {
            await storage.WriteAsync(imageKey, stream, ct);
        }

        row.QrImageKey = imageKey;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(PaymentMethodConfigDto.FromEntity(row));
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static async Task<IResult> StreamQrAsync(string? imageKey, IFileStorage storage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(imageKey) || !await storage.ExistsAsync(imageKey, ct))
        {
            return Results.NotFound();
        }

        byte[] bytes;
        await using (var source = await storage.OpenReadAsync(imageKey, ct))
        await using (var buffer = new MemoryStream())
        {
            await source.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        var contentType = ManualPaymentProof.SniffContentType(bytes);
        return Results.File(bytes, contentType);
    }

    private static string NormalizeCategory(string? value)
        => string.Equals(value, "egypt", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "inside_egypt", StringComparison.OrdinalIgnoreCase)
            ? "inside_egypt"
            : "international";
}

// ── DTOs ──────────────────────────────────────────────────────────────

public sealed record PaymentMethodConfigUpsertRequest(
    string Key,
    string Label,
    string Category,
    string Detail,
    string? Meta,
    string Instructions,
    string? Note,
    bool ReferenceRule,
    bool ShowQr,
    string? IconName,
    bool IsActive,
    int DisplayOrder);

public sealed record PaymentMethodQrUploadRequest(string ImageBase64);

public sealed record PaymentMethodConfigDto(
    string Id,
    string Key,
    string Label,
    string Category,
    string Detail,
    string? Meta,
    string Instructions,
    string? Note,
    bool ReferenceRule,
    bool ShowQr,
    bool HasQrImage,
    string? IconName,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static PaymentMethodConfigDto FromEntity(PaymentMethodConfig e) => new(
        e.Id,
        e.Key,
        e.Label,
        e.Category,
        e.Detail,
        e.Meta,
        e.Instructions,
        e.Note,
        e.ReferenceRule,
        e.ShowQr,
        !string.IsNullOrEmpty(e.QrImageKey),
        e.IconName,
        e.IsActive,
        e.DisplayOrder,
        e.CreatedAt,
        e.UpdatedAt);
}
