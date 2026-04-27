using System;
using System.Collections.Generic;

namespace OetLearner.Api.Domain;

/// <summary>
/// A versioned rulebook for a (Kind, Profession) tuple.
/// Lifecycle: Draft → Published. Only one Published version is "current"
/// per (Kind, Profession) at any given time; older Published rows go to
/// Archived so historical grades stay reproducible.
/// </summary>
public class RulebookVersion
{
    /// <summary>Surrogate id, e.g. "rb_speaking_medicine_1.0.0".</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Lowercase, matches the JSON folder, e.g. "speaking".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Lowercase, matches the JSON folder, e.g. "medicine".</summary>
    public string Profession { get; set; } = string.Empty;

    /// <summary>SemVer-ish version string ("1.0.0", "1.1.0-draft").</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Status: "draft" | "published" | "archived".</summary>
    public string Status { get; set; } = RulebookStatus.Draft;

    /// <summary>Free-form attribution carried into AI prompts.</summary>
    public string? AuthoritySource { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Admin user id that last edited this version.</summary>
    public string? UpdatedByUserId { get; set; }

    public List<RulebookSectionRow> Sections { get; set; } = new();
    public List<RulebookRuleRow> Rules { get; set; } = new();
}

public static class RulebookStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

/// <summary>Section under a versioned rulebook.</summary>
public class RulebookSectionRow
{
    /// <summary>Surrogate id (UUID).</summary>
    public string Id { get; set; } = string.Empty;

    public string RulebookVersionId { get; set; } = string.Empty;

    /// <summary>Stable section code shown in the UI, e.g. "01".</summary>
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int OrderIndex { get; set; }
}

/// <summary>One rule under a versioned rulebook.</summary>
public class RulebookRuleRow
{
    /// <summary>Surrogate id (UUID).</summary>
    public string Id { get; set; } = string.Empty;

    public string RulebookVersionId { get; set; } = string.Empty;

    /// <summary>Stable rule code, e.g. "RULE_22".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Section code this rule belongs to (FK by code).</summary>
    public string SectionCode { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>"critical" | "major" | "minor" | "info".</summary>
    public string Severity { get; set; } = "info";

    /// <summary>JSON: "all" | string[] of card/letter types.</summary>
    public string AppliesToJson { get; set; } = "\"all\"";

    public string? TurnStage { get; set; }

    /// <summary>JSON array of strings.</summary>
    public string? ExemplarPhrasesJson { get; set; }

    /// <summary>JSON array of regex strings.</summary>
    public string? ForbiddenPatternsJson { get; set; }

    public string? CheckId { get; set; }

    /// <summary>JSON object passed to the engine detector.</summary>
    public string? ParamsJson { get; set; }

    /// <summary>JSON object {good?:string[], bad?:string[]}.</summary>
    public string? ExamplesJson { get; set; }

    public int OrderIndex { get; set; }
}
