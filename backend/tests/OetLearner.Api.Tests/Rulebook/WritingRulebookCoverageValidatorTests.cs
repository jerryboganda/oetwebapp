using System.Text.Json;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Rulebooks;

namespace OetLearner.Api.Tests.Rulebook;

public class WritingRulebookCoverageValidatorTests
{
    private readonly RulebookLoader _loader = new();
    private readonly WritingRulebookCoverageValidator _validator;

    public WritingRulebookCoverageValidatorTests()
    {
        _validator = new WritingRulebookCoverageValidator(_loader);
    }

    [Fact]
    public void CoverageGate_AcceptsCanonicalWritingRulebooks()
    {
        foreach (var book in _loader.All().Where(book => book.Kind == RuleKind.Writing))
        {
            // Acceptance only — per-profession rule counts differ while the
            // canonical content migrates (OW-xxx model vs legacy R-id model).
            _validator.ValidateBook(book);
            Assert.NotEmpty(book.Rules);
        }
    }

    [Fact]
    public void CoverageGate_RejectsImportMissingCanonicalCriticalRule()
    {
        using var doc = JsonDocument.Parse(BuildImportJson(rule => rule.Id != "OW-001"));

        var ex = Assert.Throws<ApiException>(() =>
            _validator.ValidateForImport("writing", "medicine", doc.RootElement));

        Assert.Equal("writing_rulebook_coverage_failed", ex.ErrorCode);
        Assert.Contains("OW-001", ex.Message);
    }

    [Fact]
    public void CoverageGate_RejectsUnknownDeterministicCheckId()
    {
        using var doc = JsonDocument.Parse(BuildImportJson(
            _ => true,
            rule => rule.Id == "OW-001" ? "unknown_detector" : rule.CheckId));

        var ex = Assert.Throws<ApiException>(() =>
            _validator.ValidateForImport("writing", "medicine", doc.RootElement));

        Assert.Equal("writing_rulebook_coverage_failed", ex.ErrorCode);
        Assert.Contains("unsupported checkId", ex.Message);
    }

    [Fact]
    public void CoverageGate_RejectsRemovedCanonicalCheckId()
    {
        // CheckId-bearing canonical content survives only in not-yet-migrated
        // professions (e.g. dietetics); medicine is fully OW-xxx. When the
        // migration completes, this premise is void — delete, don't re-target.
        var canonicalDetectorRule = _loader.Load(RuleKind.Writing, ExamProfession.Dietetics)
            .Rules.First(rule => !string.IsNullOrWhiteSpace(rule.CheckId));
        using var doc = JsonDocument.Parse(BuildImportJson(
            _ => true,
            rule => rule.Id == canonicalDetectorRule.Id ? null : rule.CheckId,
            profession: ExamProfession.Dietetics));

        var ex = Assert.Throws<ApiException>(() =>
            _validator.ValidateForImport("writing", "dietetics", doc.RootElement));

        Assert.Equal("writing_rulebook_coverage_failed", ex.ErrorCode);
        Assert.Contains(canonicalDetectorRule.Id, ex.Message);
        Assert.Contains("checkId", ex.Message);
    }

    [Fact]
    public void CoverageGate_RejectsSupportedCheckIdMovedToWrongRule()
    {
        // Same migration note as RejectsRemovedCanonicalCheckId: medicine has
        // no checkIds left, so this pins the binding on dietetics.
        var canonical = _loader.Load(RuleKind.Writing, ExamProfession.Dietetics);
        var detectorRule = canonical.Rules.First(rule => !string.IsNullOrWhiteSpace(rule.CheckId));
        var structuredRule = canonical.Rules.First(rule => string.IsNullOrWhiteSpace(rule.CheckId));
        using var doc = JsonDocument.Parse(BuildImportJson(
            _ => true,
            rule => rule.Id == detectorRule.Id
                ? null
                : rule.Id == structuredRule.Id
                    ? detectorRule.CheckId
                    : rule.CheckId,
            profession: ExamProfession.Dietetics));

        var ex = Assert.Throws<ApiException>(() =>
            _validator.ValidateForImport("writing", "dietetics", doc.RootElement));

        Assert.Equal("writing_rulebook_coverage_failed", ex.ErrorCode);
        Assert.Contains(detectorRule.Id, ex.Message);
        Assert.Contains(structuredRule.Id, ex.Message);
    }

    [Fact]
    public void CoverageGate_RejectsSectionDrift()
    {
        using var doc = JsonDocument.Parse(BuildImportJson(
            _ => true,
            sectionOverride: rule => rule.Id == "OW-001" ? "99" : rule.Section));

        var ex = Assert.Throws<ApiException>(() =>
            _validator.ValidateForImport("writing", "medicine", doc.RootElement));

        Assert.Equal("writing_rulebook_coverage_failed", ex.ErrorCode);
        Assert.Contains("OW-001", ex.Message);
        Assert.Contains("section", ex.Message);
    }

    [Fact]
    public void CoverageGate_RejectsForbiddenPatternDrift()
    {
        // Same migration note: medicine carries no forbiddenPatterns, so this
        // pins the drift check on dietetics.
        var forbiddenRule = _loader.Load(RuleKind.Writing, ExamProfession.Dietetics)
            .Rules.First(rule => rule.ForbiddenPatterns is { Count: > 0 });
        using var doc = JsonDocument.Parse(BuildImportJson(
            _ => true,
            forbiddenPatternsOverride: rule => rule.Id == forbiddenRule.Id ? Array.Empty<string>() : rule.ForbiddenPatterns,
            profession: ExamProfession.Dietetics));

        var ex = Assert.Throws<ApiException>(() =>
            _validator.ValidateForImport("writing", "dietetics", doc.RootElement));

        Assert.Equal("writing_rulebook_coverage_failed", ex.ErrorCode);
        Assert.Contains(forbiddenRule.Id, ex.Message);
        Assert.Contains("forbiddenPatterns", ex.Message);
    }

    private string BuildImportJson(
        Func<OetRule, bool> includeRule,
        Func<OetRule, string?>? checkIdOverride = null,
        Func<OetRule, string>? sectionOverride = null,
        Func<OetRule, object?>? forbiddenPatternsOverride = null,
        ExamProfession profession = ExamProfession.Medicine)
    {
        var book = _loader.Load(RuleKind.Writing, profession);
        var rules = book.Rules.Where(includeRule).Select(rule => new
        {
            id = rule.Id,
            section = sectionOverride?.Invoke(rule) ?? rule.Section,
            title = rule.Title,
            body = rule.Body,
            severity = rule.Severity.ToString().ToLowerInvariant(),
            checkId = checkIdOverride is null ? rule.CheckId : checkIdOverride(rule),
            forbiddenPatterns = forbiddenPatternsOverride is null ? rule.ForbiddenPatterns : forbiddenPatternsOverride(rule),
        });

        return JsonSerializer.Serialize(new
        {
            version = $"coverage-test-{Guid.NewGuid():N}",
            kind = "writing",
            profession = "medicine",
            sections = book.Sections.Select(section => new { id = section.Id, title = section.Title, order = section.Order }),
            rules,
        });
    }
}
