using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain.Billing;

[Index(nameof(Code), IsUnique = true)]
public class BillingProduct
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(256)]
    public string Name { get; set; } = default!;

    [MaxLength(1024)]
    public string? Description { get; set; }

    /// <summary>package | subscription | addon</summary>
    [MaxLength(32)]
    public string ProductType { get; set; } = default!;

    [MaxLength(64)]
    public string? StripeProductId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>JSON metadata blob for extensibility.</summary>
    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<BillingPrice> Prices { get; set; } = new List<BillingPrice>();
}
