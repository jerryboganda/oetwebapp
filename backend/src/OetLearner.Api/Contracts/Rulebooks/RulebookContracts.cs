using System.Collections.Generic;

namespace OetLearner.Api.Contracts.Rulebooks;

/// <summary>Brief listing row for the admin rulebook index.</summary>
public sealed record RulebookSummaryDto(
    string Id,
    string Kind,
    string Profession,
    string Version,
    string Status,
    string? AuthoritySource,
    int SectionCount,
    int RuleCount,
    string? UpdatedByUserId,
    string CreatedAt,
    string UpdatedAt,
    string? PublishedAt);

/// <summary>Full version detail with sections + rules embedded.</summary>
public sealed record RulebookDetailDto(
    string Id,
    string Kind,
    string Profession,
    string Version,
    string Status,
    string? AuthoritySource,
    string CreatedAt,
    string UpdatedAt,
    string? PublishedAt,
    IReadOnlyList<RulebookSectionDto> Sections,
    IReadOnlyList<RulebookRuleDto> Rules);

public sealed record RulebookSectionDto(
    string Id,
    string Code,
    string Title,
    int OrderIndex);

public sealed record RulebookRuleDto(
    string Id,
    string Code,
    string SectionCode,
    string Title,
    string Body,
    string Severity,
    string AppliesToJson,
    string? TurnStage,
    string? ExemplarPhrasesJson,
    string? ForbiddenPatternsJson,
    string? CheckId,
    string? ParamsJson,
    string? ExamplesJson,
    int OrderIndex);

// ── Mutation requests ────────────────────────────────────────────────────

public sealed record UpdateRulebookMetaRequest(
    string? Version,
    string? AuthoritySource);

public sealed record CreateSectionRequest(
    string Code,
    string Title,
    int? OrderIndex);

public sealed record UpdateSectionRequest(
    string? Title,
    int? OrderIndex);

public sealed record CreateRuleRequest(
    string Code,
    string SectionCode,
    string Title,
    string Body,
    string Severity,
    string? AppliesToJson,
    string? TurnStage,
    string? ExemplarPhrasesJson,
    string? ForbiddenPatternsJson,
    string? CheckId,
    string? ParamsJson,
    string? ExamplesJson,
    int? OrderIndex);

public sealed record UpdateRuleRequest(
    string? SectionCode,
    string? Title,
    string? Body,
    string? Severity,
    string? AppliesToJson,
    string? TurnStage,
    string? ExemplarPhrasesJson,
    string? ForbiddenPatternsJson,
    string? CheckId,
    string? ParamsJson,
    string? ExamplesJson,
    int? OrderIndex);

public sealed record PublishRulebookRequest(string? VersionLabel);
