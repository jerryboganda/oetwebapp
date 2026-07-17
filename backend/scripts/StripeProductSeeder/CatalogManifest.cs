using System.Text.Json;
using System.Text.Json.Serialization;

namespace OetWithDrHesham.Scripts.StripeProductSeeder;

/// <summary>Canonical Stripe seed catalogue parsed from catalog.json.</summary>
public sealed class CatalogManifest
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("defaultCurrency")]
    public string DefaultCurrency { get; set; } = "usd";

    [JsonPropertyName("products")]
    public List<CatalogProduct> Products { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class CatalogProduct
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>package | subscription | addon | class_pack</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "package";

    [JsonPropertyName("prices")]
    public List<CatalogPrice> Prices { get; set; } = new();
}

public sealed class CatalogPrice
{
    [JsonPropertyName("unitAmount")]
    public long UnitAmount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "usd";

    /// <summary>month | year | null for one-time</summary>
    [JsonPropertyName("interval")]
    public string? Interval { get; set; }

    [JsonPropertyName("intervalCount")]
    public long? IntervalCount { get; set; }
}
