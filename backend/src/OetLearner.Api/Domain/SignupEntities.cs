using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

[Index(nameof(ApplicationUserAccountId), IsUnique = true)]
[Index(nameof(LearnerUserId), IsUnique = true)]
public class LearnerRegistrationProfile
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ApplicationUserAccountId { get; set; } = default!;

    [MaxLength(64)]
    public string LearnerUserId { get; set; } = default!;

    [MaxLength(128)]
    public string FirstName { get; set; } = default!;

    [MaxLength(128)]
    public string LastName { get; set; } = default!;

    [MaxLength(32)]
    public string ExamTypeId { get; set; } = default!;

    [MaxLength(32)]
    public string ProfessionId { get; set; } = default!;

    [MaxLength(64)]
    public string SessionId { get; set; } = default!;

    [MaxLength(64)]
    public string CountryTarget { get; set; } = default!;

    [MaxLength(32)]
    public string MobileNumber { get; set; } = default!;

    public bool AgreeToTerms { get; set; }
    public bool AgreeToPrivacy { get; set; }
    public bool MarketingOptIn { get; set; }

    // ── UTM / Acquisition Attribution ──
    [MaxLength(128)]
    public string? UtmSource { get; set; }

    [MaxLength(128)]
    public string? UtmMedium { get; set; }

    [MaxLength(256)]
    public string? UtmCampaign { get; set; }

    [MaxLength(128)]
    public string? UtmTerm { get; set; }

    [MaxLength(128)]
    public string? UtmContent { get; set; }

    [MaxLength(512)]
    public string? ReferrerUrl { get; set; }

    [MaxLength(512)]
    public string? LandingPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ApplicationUserAccount ApplicationUserAccount { get; set; } = default!;
    public LearnerUser LearnerUser { get; set; } = default!;
}

public class SignupExamTypeCatalog
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Label { get; set; } = default!;

    [MaxLength(16)]
    public string Code { get; set; } = default!;

    [MaxLength(256)]
    public string Description { get; set; } = default!;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SignupProfessionCatalog
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;

    public string CountryTargetsJson { get; set; } = "[]";
    public string ExamTypeIdsJson { get; set; } = "[]";

    [MaxLength(256)]
    public string Description { get; set; } = default!;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SignupSessionCatalog
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(160)]
    public string Name { get; set; } = default!;

    [MaxLength(32)]
    public string ExamTypeId { get; set; } = default!;

    public string ProfessionIdsJson { get; set; } = "[]";

    [MaxLength(32)]
    public string PriceLabel { get; set; } = default!;

    [MaxLength(32)]
    public string StartDate { get; set; } = default!;

    [MaxLength(32)]
    public string EndDate { get; set; } = default!;

    [MaxLength(32)]
    public string DeliveryMode { get; set; } = default!;

    public int Capacity { get; set; }
    public int SeatsRemaining { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
