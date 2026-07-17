using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetWithDrHesham.Api.Domain.Billing;

[Index(nameof(TriggerProductCode))]
public class CrossSellRule
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string TriggerProductCode { get; set; } = default!;

    [MaxLength(64)]
    public string SuggestedProductCode { get; set; } = default!;

    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
