using System.Text.Json;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Rulebooks;

namespace OetLearner.Api.Tests;

public class RulebookLoaderTests
{
    private readonly RulebookLoader _loader = new();

    [Fact]
    public void Loads_Writing_Medicine_Rulebook()
    {
        var book = _loader.Load(RuleKind.Writing, ExamProfession.Medicine);
        Assert.Equal(RuleKind.Writing, book.Kind);
        Assert.Equal(ExamProfession.Medicine, book.Profession);
        Assert.Equal("2.0.0-canonical", book.Version);
        Assert.Equal(43, book.Sections.Count);
        Assert.True(book.Rules.Count >= 90);
    }

    [Fact]
    public void Loads_Speaking_Medicine_Rulebook()
    {
        var book = _loader.Load(RuleKind.Speaking, ExamProfession.Medicine);
        Assert.Equal(RuleKind.Speaking, book.Kind);
        Assert.Equal(ExamProfession.Medicine, book.Profession);
        Assert.Equal(8, book.Sections.Count);
        Assert.Equal(62, book.Rules.Count);
    }

    [Fact]
    public void Loads_Speaking_Nursing_Rulebook_ForSeededRolePlay()
    {
        var book = _loader.Load(RuleKind.Speaking, ExamProfession.Nursing);
        Assert.Equal(RuleKind.Speaking, book.Kind);
        Assert.Equal(ExamProfession.Nursing, book.Profession);
        Assert.Contains(book.Rules, rule => rule.Id == "RULE_56" && rule.AppliesTo.HasValue);
    }

    [Fact]
    public void Throws_For_Unregistered_Profession_Combination()
    {
        var registered = _loader.All()
            .Select(book => (book.Kind, book.Profession))
            .ToHashSet();

        var missing = Enum.GetValues<RuleKind>()
            .SelectMany(kind => Enum.GetValues<ExamProfession>().Select(profession => (kind, profession)))
            .FirstOrDefault(pair => !registered.Contains(pair));

        Assert.DoesNotContain(registered, pair => pair == missing);
        Assert.Throws<OetLearner.Api.Services.Rulebook.RulebookNotFoundException>(() =>
            _loader.Load(missing.kind, missing.profession));
    }

    // Canonical successors of the retired R-id criticals (rulebook
    // 2.0.0-canonical): R03.4 -> OW-007, R07.6 -> DH-W-009,
    // R09.2 -> DH-W-016, R13.10 -> OW-017, R14.6 -> DH-W-030,
    // R14.12 -> DH-W-036.
    [Theory]
    [InlineData("OW-007")]
    [InlineData("DH-W-009")]
    [InlineData("DH-W-016")]
    [InlineData("OW-017")]
    [InlineData("DH-W-030")]
    [InlineData("DH-W-036")]
    public void Critical_Writing_Rule_Found(string id)
    {
        var rule = _loader.FindRule(RuleKind.Writing, ExamProfession.Medicine, id);
        Assert.NotNull(rule);
        Assert.Equal(RuleSeverity.Critical, rule!.Severity);
    }

    [Theory]
    [InlineData("RULE_06")]
    [InlineData("RULE_22")]
    [InlineData("RULE_27")]
    [InlineData("RULE_32")]
    [InlineData("RULE_44")]
    public void Critical_Speaking_Rule_Found(string id)
    {
        var rule = _loader.FindRule(RuleKind.Speaking, ExamProfession.Medicine, id);
        Assert.NotNull(rule);
        Assert.Equal(RuleSeverity.Critical, rule!.Severity);
    }

    [Fact]
    public void Speaking_Has_13Stage_Consultation_StateMachine()
    {
        var book = _loader.Load(RuleKind.Speaking, ExamProfession.Medicine);
        Assert.NotNull(book.StateMachines);
        var sm = book.StateMachines!.Value;
        Assert.Equal(13, sm.GetProperty("consultationStages").GetArrayLength());
        Assert.Equal(7, sm.GetProperty("breakingBadNewsProtocol").GetArrayLength());
        Assert.Equal(3, sm.GetProperty("smokingLadder").GetArrayLength());
    }

    [Fact]
    public void Writing_Has_Letter_Skeleton_Table()
    {
        var book = _loader.Load(RuleKind.Writing, ExamProfession.Medicine);
        Assert.NotNull(book.Tables);
        var skeleton = book.Tables!.Value.GetProperty("letterSkeleton");
        Assert.True(skeleton.GetArrayLength() > 10);
    }

    [Fact]
    public void Assessment_Criteria_Is_Loaded_For_Both_Kinds()
    {
        Assert.Equal(JsonValueKind.Object, _loader.GetAssessmentCriteria(RuleKind.Writing).ValueKind);
        Assert.Equal(JsonValueKind.Object, _loader.GetAssessmentCriteria(RuleKind.Speaking).ValueKind);
    }
}

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

public class WritingRuleEngineTests
{
    private readonly WritingRuleEngine _engine = new(new RulebookLoader());

    private const string LetterWithBoth = @"Dr A B
Cardiology Clinic
Main Street
City

1 January 2026

Dear Dr Smith,
Re: Mr John Jones D.O.B: 01/01/1980

I am writing to refer Mr Jones, a 45-year-old teacher, for your assessment.

Mr Jones smokes 10 cigarettes per day and drinks alcohol occasionally. He presented with chest pain today. His blood pressure was 150/90 mmHg. Examination revealed mild discomfort on palpation. He was advised lifestyle changes.

Please do not hesitate to contact me.

Yours sincerely,

Doctor";

    [Fact]
    public void R03_4_Passes_With_Smoking_And_Drinking()
    {
        var findings = _engine.Lint(new WritingLintInput(LetterWithBoth, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.content_requires_smoking_drinking");
    }

    // Was R03_4_Fires_When_Smoking_Missing, asserting the opposite of current
    // behavior. Per the FINAL MASTER Writing Rulebook v1.0 (31 Aug 2026) §8
    // provenance audit recorded directly on DetectSmokingDrinking below,
    // R03.4 is OVERRIDDEN_OR_CORRECTED to a permanent no-op — smoking/alcohol
    // relevance is judged by the AI assessor from the case notes, never by a
    // blanket deterministic detector. This test predates that governance
    // decision and was never updated; it now locks in the override instead
    // of contradicting it.
    [Fact]
    public void R03_4_Stays_A_NoOp_Per_31Aug2026_Governance_Override()
    {
        var text = LetterWithBoth.Replace("Mr Jones smokes 10 cigarettes per day and ", "");
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.content_requires_smoking_drinking");
    }

    [Fact]
    public void R03_4_Suppressed_When_Recipient_Is_OT()
    {
        var text = LetterWithBoth
            .Replace("smokes 10 cigarettes per day and drinks alcohol occasionally. ", "");
        var findings = _engine.Lint(new WritingLintInput(
            text, "non_medical_referral", RecipientSpecialty: "Occupational Therapist"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.content_requires_smoking_drinking");
    }

    // Regression: ParseLetter's date-line regex previously only matched
    // "day month" with no year (\d{1,2}[\/\s-]\w+), so a whole-line
    // "D Month YYYY" date — the Global Formatting Addendum's own lead
    // example, e.g. "5 September 2026" — never set DateIndex. That
    // false-negative cascaded into a spurious "missing Date" structure
    // finding and starved every other check gated on DateIndex (date
    // format consistency, smoking/drinking, jargon, etc. all read the
    // wrong body boundary). Covers every accepted style family from the
    // addendum: written (day-first and month-first), slash, and dot.
    [Theory]
    [InlineData("1 January 2026")]
    [InlineData("5 September 2026")]
    [InlineData("September 5, 2026")]
    [InlineData("September 5 2026")]
    [InlineData("01/09/2026")]
    [InlineData("05.09.2026")]
    public void ParseLetter_RecognisesEveryAddendumDateStyle_AsTheDateLine(string dateLine)
    {
        var text = LetterWithBoth.Replace("1 January 2026", dateLine);
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.letter_structure_order");
    }

    [Fact]
    public void R04_2_Fires_On_Blank_Between_Salutation_And_Re()
    {
        var text = "Dear Dr Smith,\n\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.salutation_re_adjacent" || f.RuleId == "BUILTIN.salutation_re_adjacent");
    }

    [Fact]
    public void R05_8_Fires_On_Date_Prefix()
    {
        var text = "Dr A\n\nDate: 1 January 2026\n\nDear Dr Smith,\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.no_date_prefix");
    }

    [Fact]
    public void R06_10_Fires_On_Minor_With_Title()
    {
        var text = "Dear Dr Smith,\nRe: Miss Sara Miller D.O.B: 01/01/2015\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral", PatientIsMinor: true));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.minor_naming_convention");
    }

    [Fact]
    public void R06_11_Fires_On_SirMadam_With_Sincerely()
    {
        var text = "Dear Sir/Madam,\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.yours_sincerely_vs_faithfully");
    }

    [Fact]
    public void R07_6_Fires_On_Urgent_Without_Urgent_Word()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for assessment.\n\nOn today's visit she presented with severe pain.\n\nAt your earliest convenience.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "urgent_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.urgent_intro_contains_urgent" || f.RuleId == "BUILTIN.urgent_intro_contains_urgent");
    }

    [Fact]
    public void R08_7_Does_Not_Flag_Next_Visit()
    {
        // Rulebook update (31 Aug 2026): "next visit" is standard English, no longer forbidden.
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nOn the next visit, she reported improvement.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.body_forbidden_phrase_next_visit" || f.RuleId == "BUILTIN.body_forbidden_phrase_next_visit");
    }

    [Fact]
    public void R08_14_Does_Not_Flag_The_Patient()
    {
        // Rulebook update (31 Aug 2026, G-W-112): "the patient" is not a forbidden phrase.
        var text = "Dear Dr Smith,\nRe: Ms Miller\n\nIntro.\n\nThe patient presented with nausea.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.body_forbidden_phrase_the_patient" || f.RuleId == "BUILTIN.body_forbidden_phrase_the_patient");
    }

    [Fact]
    public void R09_2_Fires_On_Urgent_Missing_Earliest_Convenience()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to urgently refer Ms A.\n\nOn today's visit she collapsed.\n\nPlease see her soon.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "urgent_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.urgent_closure_phrase" || f.RuleId == "BUILTIN.urgent_closure_phrase");
    }

    // Regression fixture for the live "Mr David Taylor" urgent rheumatology
    // referral (Writing Rule Enforcement Addendum Rev5, 10 Sep 2026 — task
    // 07d56634-dc0f-4afc-9b3d-ae1d527f1314). Root cause: WritingScenario.
    // LetterType stores the catalogue code ("LT-UR"), not the legacy token
    // ("urgent_referral") every urgent-specific detector compares against —
    // so the entire urgent battery silently no-op'd for any task passed
    // straight through with its raw catalogue code, exactly as
    // WritingTaskModelAnswerService did. Fixed by normalising LetterType
    // once, inside Lint() itself, via WritingLetterTypeTaxonomy.ToLegacyLetterType.
    //
    // This case (default/candidate mode, no closing urgency wording at all)
    // still proves the battery is active for the raw "LT-UR" code.
    // urgent_token_not_repeated is candidate-mode inert (see the §3
    // Model-vs-Candidate tests below) so it is intentionally not asserted here.
    [Fact]
    public void UrgentBattery_Fires_When_LetterType_Is_The_Raw_LTUR_Catalogue_Code()
    {
        var text = "Dear Dr Still,\nRe: Mr David Taylor, aged 55\nI am writing to refer Mr David Taylor for urgent rheumatological assessment of an acute gout flare.\n\nHe presented on today's visit with severe pain.\n\nI would be grateful if you could assess Mr Taylor's gout.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "LT-UR"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.urgent_closure_phrase");
    }

    // Owner clarification (Writing Rule Enforcement Addendum Rev5, 10 Sep
    // 2026, §3 "Urgent closure"): the Model Answer generator/validator MAY
    // require the exact "at your earliest convenience" phrase and MAY
    // penalise repeating "urgent" outside the introduction.
    [Fact]
    public void ModelAnswer_UrgentClosure_Still_Requires_Exact_Phrase_And_Forbids_Repeated_Urgent()
    {
        var text = "Dear Dr Still,\nRe: Mr David Taylor, aged 55\nI am writing to refer Mr David Taylor for urgent rheumatological assessment of an acute gout flare.\n\nHe presented on today's visit with severe pain.\n\nI would be grateful if you could urgently assess Mr Taylor's gout.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "urgent_referral", IsModelAnswer: true));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.urgent_closure_phrase");
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.urgent_token_not_repeated");
    }

    // Owner clarification (same addendum, §3): this SUPERSEDES the prior
    // candidate-facing hard penalty for repeating "urgent"/"urgently" — the
    // candidate grader must accept any clear, professional closure that
    // communicates the urgent requested action, without the exact phrase.
    [Fact]
    public void Candidate_UrgentClosure_Accepts_Urgently_And_Does_Not_Penalise_Repetition()
    {
        var text = "Dear Dr Still,\nRe: Mr David Taylor, aged 55\nI am writing to refer Mr David Taylor for urgent rheumatological assessment of an acute gout flare.\n\nHe presented on today's visit with severe pain.\n\nI would be grateful if you could urgently assess Mr Taylor's gout.\n\nYours sincerely,\nDoctor";
        // Default IsModelAnswer is false — this is the candidate grader path.
        var findings = _engine.Lint(new WritingLintInput(text, "urgent_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.urgent_closure_phrase");
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.urgent_token_not_repeated");
    }

    // Owner clarification (same addendum, §2 "Blank line after Re: line"):
    // "After the Re: line, there MUST be one blank line before the
    // introduction." New detector — was previously unenforced.
    [Fact]
    public void BlankLineAfterReLine_Fires_When_Intro_Immediately_Follows_Re()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\nI am writing to refer Ms A for assessment.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.blank_line_after_re_line");
    }

    [Fact]
    public void BlankLineAfterReLine_Passes_When_Blank_Line_Present()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for assessment.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.blank_line_after_re_line");
    }

    // Owner clarification (same addendum, §6 "Re: line and introduction: do
    // not duplicate age"). Distinct from re_line_age_dob (Re: line's own
    // "Age:"/"aged" formatting) — this is the cross-location duplication.
    [Fact]
    public void AgeNotDuplicatedInIntro_Fires_When_Same_Age_Repeated()
    {
        var text = "Dear Dr Smith,\nRe: Mr David Taylor, aged 55\n\nI am writing to refer Mr Taylor, aged 55, for assessment.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.age_not_duplicated_in_intro");
    }

    [Fact]
    public void AgeNotDuplicatedInIntro_Passes_When_Age_Only_In_ReLine()
    {
        var text = "Dear Dr Smith,\nRe: Mr David Taylor, aged 55\n\nI am writing to refer Mr Taylor for assessment.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.age_not_duplicated_in_intro");
    }

    // Owner clarification (same addendum, §2 "Emotional wording"). "He
    // suffered a severe attack." was replaced by a genuinely-dramatised
    // example — the bulk audit found plain "suffered [clinical event]" is
    // standard factual medical English, not banned (see the narrowed §1
    // "G-W-116" governance decision and the dedicated
    // EmotionalWording_Passes_On_Suffered_As_Factual_Clinical_Event /
    // _Fires_On_Suffered_With_Dramatising_Intensifier tests above).
    [Theory]
    [InlineData("The poor patient suffered terribly.")]
    [InlineData("Unfortunately, his symptoms worsened.")]
    [InlineData("Regrettably, treatment was delayed.")]
    public void EmotionalWording_Fires_On_Banned_Words(string bodyLine)
    {
        var text = $"Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\n{bodyLine}\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.emotional_wording");
    }

    // Owner clarification (same addendum, §2 "Judgmental labels").
    [Fact]
    public void JudgmentalLabels_Fires_On_Disease_As_Person_Label()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe is asthmatic and hypertensive.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.judgmental_labels");
    }

    [Fact]
    public void JudgmentalLabels_Passes_On_Factual_Form()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has asthma and hypertension.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.judgmental_labels");
    }

    // Owner clarification (same addendum, §3 "Linker policy") — restricted
    // to sentence-initial use, the actual connective position.
    [Fact]
    public void LinkerAvoidWords_Fires_On_Sentence_Initial_But()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe improved with treatment. But her pain returned.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.linker_avoid_words");
    }

    [Fact]
    public void LinkerAvoidWords_Passes_On_MidSentence_So()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nHer pain was not so severe today.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.linker_avoid_words");
    }

    // Owner clarification (same addendum, §2 "No duplicated request").
    [Fact]
    public void NoDuplicatedRequest_Fires_When_Closure_Repeats_Intro_Phrase()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for urgent cardiology assessment.\n\nBackground details here.\n\nI would be grateful for urgent cardiology assessment at your earliest convenience.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.no_duplicated_request");
    }

    [Fact]
    public void NoDuplicatedRequest_Passes_When_Closure_Is_Distinct()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for urgent cardiology assessment.\n\nBackground details here.\n\nI would be grateful for your review at your earliest convenience.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.no_duplicated_request");
    }

    // Owner governance decision (Writing Rule Enforcement Addendum Rev5
    // reply, 10 Sep 2026, §5 "Maximum paragraphs"): the closing/request
    // paragraph is not a body paragraph. 5 total paragraphs (intro + 3 body
    // + closure) is 4 real body paragraphs — must pass, not fail at max=4.
    [Fact]
    public void ParagraphCount_Passes_When_Closure_Paragraph_Pushes_Total_To_Five()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph.\n\nBody one.\n\nBody two.\n\nBody three.\n\nI would be grateful for your review.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.letter_paragraph_count");
    }

    [Fact]
    public void ParagraphCount_Fires_When_Real_Body_Exceeds_Four_Excluding_Closure()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph.\n\nBody one.\n\nBody two.\n\nBody three.\n\nBody four.\n\nBody five.\n\nI would be grateful for your review.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.letter_paragraph_count" && f.Message.Contains("Maximum"));
    }

    // Owner governance decision (same reply, §6 "Discharge introduction"):
    // equivalent professional wording must pass without the exact template
    // sentence.
    [Fact]
    public void DischargeIntroTemplate_Passes_On_Equivalent_Wording()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to inform you that Ms A was discharged today following treatment for pneumonia.\n\nDetails.\n\nPlease review as needed.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "discharge"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.discharge_intro_template");
    }

    [Fact]
    public void DischargeIntroTemplate_Fires_On_Routine_Referral_Wording_With_No_Discharge_Context()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for further assessment.\n\nDetails.\n\nPlease review.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "discharge"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.discharge_intro_template");
    }

    // Owner governance decision (same reply, §1 "G-W-116"): "suffered" is
    // standard factual clinical English for a diagnosed event and must not
    // be flagged — only genuinely emotional/dramatised use should be.
    [Fact]
    public void EmotionalWording_Passes_On_Suffered_As_Factual_Clinical_Event()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe suffered a myocardial infarction on 3 June 2020.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.emotional_wording");
    }

    // Owner-directed follow-up (Rev5 audit, 10 Sep 2026): dietetics/OT/
    // speech-pathology discharge plans are legitimately non-pharmacological.
    [Fact]
    public void DischargePlanPresent_Passes_For_NonPrescribing_Profession_Without_Medication()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to inform you Ms A was discharged today.\n\nShe was seen for dietetic review.\n\nShe should continue her meal plan and follow-up in clinic.\n\nYours sincerely,\nDietitian";
        var findings = _engine.Lint(new WritingLintInput(text, "discharge", Profession: ExamProfession.Dietetics));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.discharge_plan_present");
    }

    // \bmg\b never matched no-space dose notation ("100mg") since digit->
    // letter isn't a \b transition — found via a real held production letter
    // (OET test 11 / Ling Wu) whose discharge meds ("Neurontin 100mg every
    // 8 hours") were genuinely present but never detected.
    [Fact]
    public void DischargePlanPresent_Passes_On_NoSpace_Dose_Notation()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to update you.\n\nShe was discharged today.\n\nContinue Neurontin 100mg every 8 hours and she should review with her GP.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "discharge", Profession: ExamProfession.Medicine));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.discharge_plan_present");
    }

    // Real held production letter (Mr Ryan Cooper, physiotherapy discharge
    // after ACL reconstruction): an exercise-only plan with no medications
    // at all — the same non-pharmacological pattern as dietetics/OT/speech.
    [Fact]
    public void DischargePlanPresent_Passes_For_Physiotherapy_Without_Medication()
    {
        var text = "Dear Dr Smith,\nRe: Mr A\n\nI am writing to update you.\n\nHe was discharged today from physiotherapy.\n\nHe should continue strengthening exercises and review if pain increases.\n\nYours sincerely,\nPhysiotherapist";
        var findings = _engine.Lint(new WritingLintInput(text, "discharge", Profession: ExamProfession.Physiotherapy));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.discharge_plan_present");
    }

    [Fact]
    public void DischargePlanPresent_Still_Fires_For_Medicine_Without_Medication()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to inform you Ms A was discharged today.\n\nShe was seen for review.\n\nShe should follow up in clinic.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "discharge", Profession: ExamProfession.Medicine));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.discharge_plan_present");
    }

    [Fact]
    public void EmotionalWording_Fires_On_Suffered_With_Dramatising_Intensifier()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nThe poor patient suffered terribly during the admission.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.emotional_wording");
    }

    [Fact]
    public void R11_1_Flags_Latin_Abbreviation_bd()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe was prescribed amoxicillin 500 mg bd.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.latin_abbreviations_translated");
    }

    [Fact]
    public void R11_1_Latin_Abbreviation_Is_Advisory_Minor_Not_Blocking()
    {
        // Rulebook G-W-105 (FINAL MASTER 2026-08-31): translate "unless the
        // task/recipient convention clearly supports" keeping it — the engine
        // cannot evaluate that deterministically, so this stays advisory.
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe was prescribed amoxicillin 500 mg bd.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        var finding = findings.FirstOrDefault(f => f.RuleId == "BUILTIN.latin_abbreviations_translated");
        Assert.NotNull(finding);
        Assert.Equal(RuleSeverity.Minor, finding!.Severity);
    }

    [Fact]
    public void R12_1_Flags_Contractions()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe doesn't take any regular medication.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.no_contractions");
    }

    [Fact]
    public void R12_1_Contraction_Is_Advisory_Minor_Not_Blocking()
    {
        // Rulebook DH-W-044 / G-W-117 (FINAL MASTER 2026-08-31): an isolated
        // contraction is a Genre/Style issue, not a catastrophic grammar
        // failure — still flagged, but never blocking.
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe doesn't take any regular medication.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        var finding = findings.FirstOrDefault(f => f.RuleId == "BUILTIN.no_contractions");
        Assert.NotNull(finding);
        Assert.Equal(RuleSeverity.Minor, finding!.Severity);
    }

    [Fact]
    public void R13_10_Flags_ASAP()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nPlease see her ASAP.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.no_asap_in_letter");
    }

    [Fact]
    public void R14_6_Flags_Was_Presented_In_Discharge()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to update you regarding Ms A.\n\nShe was presented with chest pain on admission.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "discharge"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.discharge_admitted_with_past_simple");
    }

    [Fact]
    public void R14_12_Flags_Treated_From()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe was treated from pneumonia.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.treatment_for_not_from");
    }

    // Was R15_2_Flags_Jargon_In_Non_Medical_Referral, asserting the opposite
    // of current behavior. Per the FINAL MASTER Writing Rulebook v1.0
    // (31 Aug 2026) §8 provenance audit recorded directly on
    // DetectNonMedicalJargon below, R15.2 is OVERRIDDEN_OR_CORRECTED to a
    // permanent no-op — a fixed word list can't judge whether the actual
    // recipient will understand a term, so register is judged by the AI
    // assessor instead. This test predates that governance decision and was
    // never updated; it now locks in the override instead of contradicting it.
    [Fact]
    public void R15_2_Stays_A_NoOp_Per_31Aug2026_Governance_Override()
    {
        var text = "Dear Sir/Madam,\nRe: Ms A\n\nMs A has hypertension and diabetes.\n\nYours faithfully,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(
            text, "non_medical_referral", RecipientSpecialty: "Occupational Therapist"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.non_medical_no_jargon");
    }

    [Fact]
    public void R03_8_Body_Length_Suppressed_Below_80_Words()
    {
        // The default sample letter body is well under 80 words.
        var findings = _engine.Lint(new WritingLintInput(LetterWithBoth, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.letter_body_length");
    }

    [Fact]
    public void R03_8_Body_Length_Advisory_When_Too_Long()
    {
        // Build a body of ~260 words (above the 200 max, above the 80 noise floor).
        var word = "word ";
        var bigBody = string.Concat(Enumerable.Repeat(word, 260));
        var text =
            "Dear Dr Smith,\nRe: Ms A\n\n"
            + bigBody + "\n\n"
            + "Yours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        var advisory = findings.FirstOrDefault(f => f.RuleId == "BUILTIN.letter_body_length");
        Assert.NotNull(advisory);
        Assert.Equal(RuleSeverity.Minor, advisory!.Severity);
        Assert.Contains("Too long", advisory.Message);
        Assert.Contains("Advisory only", advisory.Message);
    }

    [Fact]
    public void R03_8_Body_Length_Advisory_When_Too_Short()
    {
        // ~120 words: above the 80 noise floor, below the 180 minimum.
        var word = "word ";
        var smallBody = string.Concat(Enumerable.Repeat(word, 120));
        var text =
            "Dear Dr Smith,\nRe: Ms A\n\n"
            + smallBody + "\n\n"
            + "Yours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        var advisory = findings.FirstOrDefault(f => f.RuleId == "BUILTIN.letter_body_length");
        Assert.NotNull(advisory);
        Assert.Equal(RuleSeverity.Minor, advisory!.Severity);
        Assert.Contains("Too short", advisory.Message);
    }

    [Fact]
    public void Findings_Are_Ordered_Critical_Then_Major_Then_Minor()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nThe patient has Hypertension.\n\nShe takes amoxicillin bd.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        for (int i = 1; i < findings.Count; i++)
        {
            Assert.True(Rank(findings[i - 1].Severity) <= Rank(findings[i].Severity));
        }
        static int Rank(RuleSeverity s) => s switch
        {
            RuleSeverity.Critical => 0, RuleSeverity.Major => 1, RuleSeverity.Minor => 2, _ => 3,
        };
    }

    // ---------------------------------------------------------------------
    // Newly ported detector coverage (mirrors lib/rulebook/writing-rules.ts)
    // ---------------------------------------------------------------------

    [Fact]
    public void R05_2_Flags_Address_Line_With_Trailing_Comma()
    {
        var text = "Dr A B,\nCardiology Clinic.\nMain Street\n\n1 January 2026\n\nDear Dr Smith,\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.address_punctuation" && f.Quote!.EndsWith(","));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.address_punctuation" && f.Quote!.EndsWith("."));
    }

    [Fact]
    public void R05_2_Passes_Address_Without_Punctuation()
    {
        var text = "Dr A B\nCardiology Clinic\nMain Street\n\n1 January 2026\n\nDear Dr Smith,\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.address_punctuation");
    }

    [Fact]
    public void R06_8_Fires_On_Capitalised_Age_Colon_In_Re_Line()
    {
        var text = "Dear Dr Smith,\nRe: Ms A Age: 40\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.re_line_age_dob");
    }

    [Fact]
    public void R06_8_Allows_Lowercase_Aged_In_Re_Line()
    {
        var text = "Dear Dr Smith,\nRe: Ms A, aged 40\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.re_line_age_dob");
    }

    [Fact]
    public void R06_12_Fires_On_Lowercase_Yours()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nBody.\n\nyours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.yours_sincerely_capitalisation");
    }

    [Fact]
    public void R06_12_Fires_On_Misspelled_Sincerely()
    {
        // The Yours-line parser requires the literal token 'sincerely' or 'faithfully',
        // so we anchor on a correct 'Yours faithfully' and append a misspelling that the
        // detector's spell pattern catches.
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours faithfully sincerly,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.yours_sincerely_capitalisation");
    }

    [Fact]
    public void R07_1_Fires_When_Intro_Has_Too_Many_Sentences()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A. She is forty. She works as a teacher. She lives alone.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.intro_sentence_count");
    }

    [Fact]
    public void R07_1_Passes_When_Intro_Has_Three_Or_Fewer_Sentences()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for assessment. She is forty. She works as a teacher.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.intro_sentence_count");
    }

    [Fact]
    public void R09_7_Fires_When_Patient_Initiated_Without_Request_Phrase()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for assessment.\n\nShe came in for a check-up.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(
            text, "routine_referral",
            CaseNotesMarkers: new WritingCaseNotesMarkers(PatientInitiatedReferral: true)));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.closure_mentions_patient_request_if_flagged");
    }

    [Fact]
    public void R09_7_Passes_When_Request_Phrase_Present()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A upon her request for assessment.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(
            text, "routine_referral",
            CaseNotesMarkers: new WritingCaseNotesMarkers(PatientInitiatedReferral: true)));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.closure_mentions_patient_request_if_flagged");
    }

    [Fact]
    public void R09_8_Fires_When_Consent_Documented_Without_Statement()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A.\n\nThank you for seeing her.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(
            text, "routine_referral",
            CaseNotesMarkers: new WritingCaseNotesMarkers(ConsentDocumented: true)));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.closure_mentions_consent_if_flagged");
    }

    [Fact]
    public void R09_8_Passes_When_Aware_Of_Diagnosis()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A.\n\nShe is aware of her diagnosis and management.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(
            text, "routine_referral",
            CaseNotesMarkers: new WritingCaseNotesMarkers(ConsentDocumented: true)));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.closure_mentions_consent_if_flagged");
    }

    [Fact]
    public void R09_9_Fires_When_No_Blank_Line_Before_Yours()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nLast body line.\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.blank_before_closing_phrase");
    }

    [Fact]
    public void R09_9_Passes_With_Blank_Line_Before_Yours()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nLast body line.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.blank_before_closing_phrase");
    }

    [Fact]
    public void R10_5_Fires_On_Past_Simple_With_Since()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe had hypertension since 2010.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.since_requires_present_perfect");
    }

    [Fact]
    public void R10_6_Fires_On_Past_Simple_With_For_Duration()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe had hypertension for 5 years.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.for_duration_requires_present_perfect");
    }

    [Fact]
    public void R10_8_Fires_On_Present_Perfect_For_Surgery_With_Finished_Time_Marker()
    {
        // Present perfect + a stated finished time (a year, here) is the genuine
        // error per rulebook G-W-021 (FINAL MASTER 2026-08-31).
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has had a cholecystectomy in 2018.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.surgery_past_simple");
    }

    [Fact]
    public void R10_8_Passes_On_Present_Perfect_For_Surgery_Without_Finished_Time_Marker()
    {
        // Rulebook G-W-021 (FINAL MASTER 2026-08-31): present perfect IS valid for
        // a completed procedure when no finished time is stated and the result has
        // current/ongoing relevance (worked example: "He has undergone cataract
        // surgery and is recovering well.").
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has had an appendectomy and is recovering well.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.surgery_past_simple");
    }

    [Fact]
    public void R10_8_Passes_On_Past_Simple_For_Surgery()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe had a cholecystectomy in 2018.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.surgery_past_simple");
    }

    [Fact]
    public void R10_10_Fires_On_Present_Perfect_With_Ago()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nShe has presented 3 weeks ago.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.ago_requires_past_simple");
    }

    [Fact]
    public void R12_9_Fires_On_However_Without_Semicolon_Or_Sentence_Boundary()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph here.\n\nThe patient was stable however she later deteriorated.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.linker_however_punctuation");
    }

    [Fact]
    public void R12_9_Passes_On_However_With_Semicolon()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph here.\n\nThe patient was stable; however, she later deteriorated.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.linker_however_punctuation");
    }

    [Fact]
    public void R12_9_Passes_On_However_Starting_New_Sentence()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph here.\n\nThe patient was stable. However, she later deteriorated.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.linker_however_punctuation");
    }

    [Fact]
    public void R12_10_Fires_On_Therefore_Without_Semicolon()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph here.\n\nShe was unwell, therefore she rested.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.linker_therefore_punctuation");
    }

    [Fact]
    public void R12_10_Passes_On_Therefore_With_Semicolon()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph here.\n\nShe was unwell; therefore, she rested.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.linker_therefore_punctuation");
    }

    [Fact]
    public void R12_10_Passes_On_Therefore_Starting_New_Sentence()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph here.\n\nShe was unwell. Therefore, she rested for the remainder of the week.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.linker_therefore_punctuation");
    }

    [Fact]
    public void R12_11_Fires_On_In_Addition_Without_Semicolon()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph here.\n\nShe takes amoxicillin, in addition she uses an inhaler.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.linker_in_addition_punctuation");
    }

    [Fact]
    public void R12_11_Passes_On_In_Addition_To()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph here.\n\nShe takes amoxicillin in addition to her usual inhaler.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.linker_in_addition_punctuation");
    }

    [Fact]
    public void R12_11_Passes_On_In_Addition_With()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph here.\n\nShe takes amoxicillin daily in addition with an inhaler as needed.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.linker_in_addition_punctuation");
    }

    [Fact]
    public void R12_11_Passes_On_In_Addition_Starting_New_Sentence()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro paragraph here.\n\nShe takes amoxicillin daily. In addition, she uses an inhaler as needed.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.linker_in_addition_punctuation");
    }

    [Fact]
    public void R05_5_Fires_On_Mixed_Date_Formats()
    {
        var text = "Dr A\n\n01/01/2026\n\nDear Dr Smith,\nRe: Ms A\n\nShe was seen on 1 January 2026 and again on 15/02/2026.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.date_format_consistent");
    }

    [Fact]
    public void R05_5_Passes_With_Single_Date_Format()
    {
        var text = "Dr A\n\n01/01/2026\n\nDear Dr Smith,\nRe: Ms A\n\nShe was seen on 15/02/2026.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.date_format_consistent");
    }

    [Fact]
    public void R05_6_Fires_On_Two_Digit_Year()
    {
        var text = "Dr A\n\n01/01/26\n\nDear Dr Smith,\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.year_not_abbreviated");
    }

    [Fact]
    public void R05_6_Passes_On_Four_Digit_Year()
    {
        var text = "Dr A\n\n01/01/2026\n\nDear Dr Smith,\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.year_not_abbreviated");
    }

    // Owner clarification (Writing Rule Enforcement Addendum Rev5, 10 Sep
    // 2026, §4 "No brackets/placeholders"): round/square/curly brackets are
    // all zero-tolerance. Curly braces were a genuine gap in the original
    // pattern (round/square only) — a template placeholder like "{Surname}"
    // slipped past undetected.
    [Theory]
    [InlineData("Yours sincerely,\n\n(Doctor)")]
    [InlineData("Re: [Surname]\n\nIntro.\n\nBody.")]
    [InlineData("Re: Ms A\n\nIntro.\n\n{Surname} was seen today.")]
    public void NoBrackets_Fires_On_Round_Square_Or_Curly_Brackets(string fragment)
    {
        var text = $"Dear Dr Smith,\n{fragment}\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.no_brackets_in_letter");
    }

    [Fact]
    public void NoBrackets_Passes_On_Letter_With_No_Bracket_Characters()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nIntro.\n\nBody.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.no_brackets_in_letter");
    }

    // Owner clarification (same addendum, §2 "General number style"):
    // descriptive numbers as words, digits only for age/dates/vitals/labs/
    // doses/measurements. Deterministic detection cannot reliably classify
    // "descriptive" vs "clinical" numeric context (see DetectNumberStyle's
    // own comment) so this stays an intentional no-op — this test locks
    // that it never fires, rather than silently regressing into false
    // positives on ordinary descriptive numbers.
    [Fact]
    public void NumberStyle_Is_A_Deliberate_NoOp_Even_With_Mixed_Descriptive_And_Clinical_Numbers()
    {
        var text = "Dear Dr Smith,\nRe: Ms A, aged 45\n\nIntro.\n\nShe has 3 children and her blood pressure was 150/90 mmHg.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "routine_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.number_style_words_vs_digits");
    }

    // Owner clarification (same addendum, §10 "Minimal-today urgent
    // override"): even if today's visit has very little information, urgent
    // referrals must STILL keep today/current presentation as the first
    // separate body paragraph — this supersedes any old "skip today if
    // minimal" exception. A one-line today paragraph must still satisfy the
    // check; it must never be dropped for being short.
    [Fact]
    public void UrgentBodyStartsToday_Passes_Even_When_Todays_Visit_Paragraph_Is_Minimal()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to urgently refer Ms A for assessment.\n\nShe presented today.\n\nHer history dates back to a visit five years ago for the same complaint.\n\nI would be grateful if you could see her as soon as possible.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "urgent_referral"));
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.urgent_body_starts_today");
    }

    [Fact]
    public void UrgentBodyStartsToday_Fires_When_Today_Paragraph_Is_Skipped_Even_Though_Minimal()
    {
        var text = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to urgently refer Ms A for assessment.\n\nHer history dates back to a visit five years ago for the same complaint.\n\nI would be grateful if you could see her as soon as possible.\n\nYours sincerely,\nDoctor";
        var findings = _engine.Lint(new WritingLintInput(text, "urgent_referral"));
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.urgent_body_starts_today");
    }
}

public class SpeakingRuleEngineTests
{
    private readonly SpeakingRuleEngine _engine = new(new RulebookLoader());

    private static SpeakingAuditInput Input(
        IEnumerable<SpeakingTurn> turns,
        string cardType = "first_visit_routine",
        int? silenceMs = null)
        => new(turns.ToList(), cardType, ExamProfession.Medicine, silenceMs);

    [Fact]
    public void Jargon_Detector_Flags_CT_Scan()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "We will arrange a CT scan of your chest next week."),
        }));
        Assert.Contains(findings, f => f.RuleId is "RULE_06" or "RULE_07");
    }

    [Fact]
    public void Jargon_Detector_Passes_Imaging_Scan()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "I'd like to arrange an imaging scan of your chest."),
        }));
        Assert.DoesNotContain(findings, f => f.RuleId == "RULE_07");
    }

    [Fact]
    public void Monologue_Detector_Flags_Long_Candidate_Turn()
    {
        var longText = string.Join(' ', Enumerable.Range(0, 160).Select(i => "word" + i));
        var findings = _engine.Audit(Input(new[] { new SpeakingTurn("candidate", longText) }));
        Assert.Contains(findings, f => f.RuleId == "RULE_22");
    }

    [Fact]
    public void Weight_Sensitivity_Flags_Direct_Question()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "What is your weight?"),
        }));
        Assert.Contains(findings, f => f.RuleId == "RULE_23");
    }

    [Fact]
    public void Smoking_Ladder_Flags_Reduction_Before_Cessation()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "I would suggest you try to reduce how many cigarettes you smoke."),
            new SpeakingTurn("patient", "I'm not sure."),
            new SpeakingTurn("candidate", "Ideally we want you to quit smoking completely."),
        }));
        Assert.Contains(findings, f => f.RuleId == "RULE_27");
    }

    [Fact]
    public void Smoking_Ladder_Passes_Cessation_First()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "I strongly recommend you quit smoking completely."),
            new SpeakingTurn("patient", "I don't think I can."),
            new SpeakingTurn("candidate", "If complete cessation is hard, we could discuss reducing your intake."),
        }));
        Assert.DoesNotContain(findings, f => f.RuleId == "RULE_27");
    }

    [Fact]
    public void Overdiagnosis_Flags_You_Have_Hypertension()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "Based on this reading, you have hypertension."),
        }));
        Assert.Contains(findings, f => f.RuleId == "RULE_32");
    }

    [Fact]
    public void Stage_Coverage_Flags_Missing_Empathy_Recap_Closure()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "Hello, I am Dr X. How can I help?"),
            new SpeakingTurn("patient", "I have a headache."),
            new SpeakingTurn("candidate", "Can you tell me more about the pain?"),
            new SpeakingTurn("patient", "It started last week."),
        }));
        Assert.Contains(findings, f => f.RuleId == "RULE_15");
        Assert.Contains(findings, f => f.RuleId == "RULE_20");
        Assert.Contains(findings, f => f.RuleId == "RULE_21");
    }

    [Fact]
    public void BBN_Protocol_Flags_Missing_Steps()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "Your results show cancer."),
            new SpeakingTurn("patient", "…"),
        }, cardType: "breaking_bad_news", silenceMs: 500));
        Assert.Contains(findings, f => f.RuleId == "RULE_41");
        Assert.Contains(findings, f => f.RuleId == "RULE_42");
        Assert.Contains(findings, f => f.RuleId == "RULE_44");
    }

    [Fact]
    public void BBN_Protocol_Passes_Fully_Sequenced_Transcript()
    {
        var findings = _engine.Audit(Input(new[]
        {
            new SpeakingTurn("candidate", "Before we discuss your results, is there anyone you'd like to have here with you?"),
            new SpeakingTurn("patient", "My partner is outside."),
            new SpeakingTurn("candidate", "I am afraid the results are not quite what we had hoped for."),
            new SpeakingTurn("candidate", "I am very sorry to tell you — the results are showing signs of cancer."),
            new SpeakingTurn("patient", "…"),
            new SpeakingTurn("candidate", "I know this is a lot to take in. Please take all the time you need."),
            new SpeakingTurn("candidate", "I want you to know that we caught this at an early stage, and there are effective treatment options available."),
            new SpeakingTurn("candidate", "I am here for you, and my number is available whenever you need anything."),
        }, cardType: "breaking_bad_news", silenceMs: 4000));
        Assert.DoesNotContain(findings, f => f.RuleId.StartsWith("RULE_4"));
    }

    [Fact]
    public void BBN_Rules_Do_Not_Fire_On_NonBBN_Card()
    {
        var findings = _engine.Audit(Input(new[] { new SpeakingTurn("candidate", "Hello.") }, cardType: "first_visit_routine"));
        Assert.DoesNotContain(findings, f => f.RuleId.StartsWith("RULE_4"));
    }
}

public class AiGatewayAndPromptTests
{
    private readonly RulebookLoader _loader = new();

    private IAiGatewayService BuildGateway()
        => new AiGatewayService(_loader, new[] { (IAiModelProvider)new MockAiProvider() });

    [Fact]
    public void Prompt_Contains_Rulebook_Header_And_Critical_Rules()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            LetterType = "routine_referral",
            CandidateCountry = "UK",
        });
        Assert.Contains("OET AI — Rulebook-Grounded System Prompt", prompt.SystemPrompt);
        Assert.Contains("CRITICAL rules", prompt.SystemPrompt);
        Assert.Contains("OW-001", prompt.SystemPrompt);
        Assert.Equal(350, prompt.Metadata.ScoringPassMark);
        Assert.Equal("B", prompt.Metadata.ScoringGrade);
    }

    [Fact]
    public void Prompt_For_USA_Writing_Uses_300_Grade_CPlus()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            CandidateCountry = "USA",
            LetterType = "routine_referral",
        });
        Assert.Equal(300, prompt.Metadata.ScoringPassMark);
        Assert.Equal("C+", prompt.Metadata.ScoringGrade);
    }

    [Fact]
    public void Prompt_For_Speaking_Is_350_Regardless_Of_Country()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Coach,
            CandidateCountry = "USA",
            CardType = "first_visit_routine",
        });
        Assert.Equal(350, prompt.Metadata.ScoringPassMark);
        Assert.Equal("B", prompt.Metadata.ScoringGrade);
        Assert.Contains("SPEAKING: Grade B at 350/500, universal", prompt.SystemPrompt);
    }

    [Fact]
    public void Prompt_For_BBN_Card_Includes_BBN_Rules()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Coach,
            CardType = "breaking_bad_news",
        });
        Assert.Contains("RULE_41", prompt.SystemPrompt);
        Assert.Contains("RULE_44", prompt.SystemPrompt);
    }

    [Fact]
    public void Prompt_For_Routine_First_Visit_Excludes_BBN_Rules()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Speaking,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Coach,
            CardType = "first_visit_routine",
        });
        Assert.DoesNotContain("RULE_44 (critical)", prompt.SystemPrompt);
    }

    [Fact]
    public async Task Gateway_Rejects_Empty_SystemPrompt()
    {
        var gateway = BuildGateway();
        var prompt = new AiGroundedPrompt { SystemPrompt = "", TaskInstruction = "do stuff" };
        await Assert.ThrowsAsync<PromptNotGroundedException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest { Prompt = prompt }));
    }

    [Fact]
    public async Task Gateway_Rejects_Ungrounded_SystemPrompt()
    {
        var gateway = BuildGateway();
        var prompt = new AiGroundedPrompt
        {
            SystemPrompt = "You are a friendly chatbot, answer however you like.",
            TaskInstruction = "Hi",
        };
        await Assert.ThrowsAsync<PromptNotGroundedException>(() =>
            gateway.CompleteAsync(new AiGatewayRequest { Prompt = prompt }));
    }

    [Fact]
    public async Task Gateway_Accepts_Grounded_Prompt_Via_Builder()
    {
        var gateway = BuildGateway();
        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Writing,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.Score,
            LetterType = "routine_referral",
        });
        var result = await gateway.CompleteAsync(new AiGatewayRequest { Prompt = prompt });
        Assert.False(string.IsNullOrWhiteSpace(result.Completion));
        Assert.Equal("2.0.0-canonical", result.RulebookVersion);
        Assert.NotEmpty(result.AppliedRuleIds);
    }

    [Fact]
    public async Task Gateway_Refuses_Writing_Prompt_Missing_LetterType()
    {
        var gateway = BuildGateway();
        var ex = Assert.Throws<PromptNotGroundedException>(() =>
            gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Score,
                // LetterType intentionally omitted
            }));
        Assert.Contains("LetterType", ex.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Gateway_Refuses_Writing_Prompt_With_Whitespace_LetterType()
    {
        var gateway = BuildGateway();
        var ex = Assert.Throws<PromptNotGroundedException>(() =>
            gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Score,
                LetterType = "   ",
            }));
        Assert.Contains("LetterType", ex.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Gateway_Refuses_Speaking_Prompt_Missing_CardType()
    {
        var gateway = BuildGateway();
        var ex = Assert.Throws<PromptNotGroundedException>(() =>
            gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Speaking,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Score,
                // CardType intentionally omitted
            }));
        Assert.Contains("CardType", ex.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Gateway_Refuses_Speaking_Prompt_With_Whitespace_CardType()
    {
        var gateway = BuildGateway();
        var ex = Assert.Throws<PromptNotGroundedException>(() =>
            gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Speaking,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.Score,
                CardType = "\t",
            }));
        Assert.Contains("CardType", ex.Message);
        await Task.CompletedTask;
    }
}
