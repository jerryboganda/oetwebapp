namespace OetLearner.Api.Services;

/// <summary>
/// ============================================================================
/// OET Canonical Scoring — SINGLE SOURCE OF TRUTH (.NET side)
/// ============================================================================
///
/// MISSION CRITICAL. Every .NET OET scoring computation MUST use this type.
/// Mirrors <c>lib/scoring.ts</c> on the frontend; the two implementations must
/// remain behaviourally identical.
///
/// Rules (verbatim from the project scoring spec):
///
///   LISTENING
///     - Pass: 350/500 (Grade B)
///     - Raw equivalent: 30/42 correct ≡ 350/500 EXACTLY
///     - &lt;30/42 = fail; ≥30/42 = pass
///
///   READING
///     - Pass: 350/500 (Grade B)
///     - Raw equivalent: 30/42 correct ≡ 350/500 EXACTLY
///     - &lt;30/42 = fail; ≥30/42 = pass
///
///   WRITING (country-dependent)
///     - Grade B  (350/500) → UK, Ireland, Australia, New Zealand, Canada
///     - Grade C+ (300/500) → USA, Qatar
///
///   SPEAKING
///     - Pass: 350/500 (Grade B), universal (no country variation)
///
/// References:
///   https://edubenchmark.com/blog/oet-score-calculator-guide/
///   https://www.geniusclass.co.uk/oet-calculator
/// </summary>
public static class OetScoring
{
    // -----------------------------------------------------------------------
    // Invariants / constants
    // -----------------------------------------------------------------------

    /// <summary>Max raw score on OET Listening and Reading papers.</summary>
    public const int ListeningReadingRawMax = 42;

    /// <summary>Raw-score pass threshold for OET Listening and Reading.</summary>
    public const int ListeningReadingRawPass = 30;

    /// <summary>Scaled-score pass threshold for Grade B (universal).</summary>
    public const int ScaledPassGradeB = 350;

    /// <summary>Scaled-score pass threshold for Grade C+ (USA/Qatar Writing).</summary>
    public const int ScaledPassGradeCPlus = 300;

    /// <summary>Scaled-score scale lower bound.</summary>
    public const int ScaledMin = 0;

    /// <summary>Scaled-score scale upper bound.</summary>
    public const int ScaledMax = 500;

    /// <summary>Countries whose Writing pass mark is Grade B (350/500).</summary>
    public static readonly IReadOnlyList<string> WritingGradeBCountries = new[]
    {
        "GB", "IE", "AU", "NZ", "CA",
    };

    /// <summary>Countries whose Writing pass mark is Grade C+ (300/500).</summary>
    public static readonly IReadOnlyList<string> WritingGradeCPlusCountries = new[]
    {
        "US", "QA",
    };

    /// <summary>Union of all countries explicitly supported for Writing.</summary>
    public static readonly IReadOnlyList<string> SupportedWritingCountries =
        WritingGradeBCountries.Concat(WritingGradeCPlusCountries).ToArray();

    // -----------------------------------------------------------------------
    // Country normalization
    // -----------------------------------------------------------------------

    private static readonly Dictionary<string, string> CountryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // United Kingdom
        ["GB"] = "GB",
        ["UK"] = "GB",
        ["UNITED KINGDOM"] = "GB",
        ["BRITAIN"] = "GB",
        ["GREAT BRITAIN"] = "GB",
        ["ENGLAND"] = "GB",
        ["SCOTLAND"] = "GB",
        ["WALES"] = "GB",
        ["NORTHERN IRELAND"] = "GB",

        // Ireland
        ["IE"] = "IE",
        ["IRELAND"] = "IE",
        ["REPUBLIC OF IRELAND"] = "IE",

        // Australia
        ["AU"] = "AU",
        ["AUSTRALIA"] = "AU",

        // New Zealand
        ["NZ"] = "NZ",
        ["NEW ZEALAND"] = "NZ",

        // Canada
        ["CA"] = "CA",
        ["CANADA"] = "CA",

        // United States
        ["US"] = "US",
        ["USA"] = "US",
        ["UNITED STATES"] = "US",
        ["UNITED STATES OF AMERICA"] = "US",
        ["AMERICA"] = "US",

        // Qatar
        ["QA"] = "QA",
        ["QATAR"] = "QA",
    };

    /// <summary>
    /// Normalize a loosely-typed country input (ISO code or English name)
    /// into the canonical alpha-2 code used by this module. Returns
    /// <c>null</c> if the value cannot be mapped to a supported country.
    /// </summary>
    public static string? NormalizeWritingCountry(string? input)
    {
        if (input is null) return null;
        var key = input.Trim();
        if (key.Length == 0) return null;
        return CountryAliases.TryGetValue(key, out var code) ? code : null;
    }

    // -----------------------------------------------------------------------
    // Raw ↔ Scaled conversion (Listening / Reading only)
    // -----------------------------------------------------------------------

    private static int ClampInt(double value, int min, int max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Score must be a finite number");
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded < min) return min;
        if (rounded > max) return max;
        return rounded;
    }

    /// <summary>
    /// Convert an OET Listening/Reading raw correct count (0–42) to the
    /// scaled 0–500 score.
    ///
    /// INVARIANTS (never break these):
    ///   OetRawToScaled(0)  == 0
    ///   OetRawToScaled(30) == 350   ← mission-critical anchor
    ///   OetRawToScaled(42) == 500
    /// </summary>
    public static int OetRawToScaled(int rawCorrect)
    {
        var raw = ClampInt(rawCorrect, 0, ListeningReadingRawMax);

        if (raw == ListeningReadingRawPass) return ScaledPassGradeB; // 350, exact
        if (raw == 0) return 0;
        if (raw == ListeningReadingRawMax) return ScaledMax; // 500

        if (raw < ListeningReadingRawPass)
        {
            // 0 → 0, 30 → 350, linear
            var scaled = (double)raw * ScaledPassGradeB / ListeningReadingRawPass;
            return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        // 30 → 350, 42 → 500, linear
        var delta = raw - ListeningReadingRawPass;
        var span = ListeningReadingRawMax - ListeningReadingRawPass; // 12
        var scaled2 = ScaledPassGradeB + (double)delta * (ScaledMax - ScaledPassGradeB) / span;
        return (int)Math.Round(scaled2, MidpointRounding.AwayFromZero);
    }

    // -----------------------------------------------------------------------
    // Grade band derivation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Map a scaled 0–500 score to its OET grade letter.
    ///
    /// Bands:
    ///   A  : 450–500
    ///   B  : 350–449
    ///   C+ : 300–349
    ///   C  : 200–299
    ///   D  : 100–199
    ///   E  :   0– 99
    /// </summary>
    public static string OetGradeLetterFromScaled(int scaled)
    {
        var s = ClampInt(scaled, ScaledMin, ScaledMax);
        return s switch
        {
            >= 450 => "A",
            >= 350 => "B",
            >= 300 => "C+",
            >= 200 => "C",
            >= 100 => "D",
            _ => "E",
        };
    }

    /// <summary>Human-friendly grade label, e.g. "Grade B" or "Grade C+".</summary>
    public static string OetGradeLabel(string letter) => $"Grade {letter}";

    // -----------------------------------------------------------------------
    // Listening / Reading pass logic
    // -----------------------------------------------------------------------

    /// <summary>Determine Listening/Reading pass from raw correct count.</summary>
    public static bool IsListeningReadingPassByRaw(int rawCorrect)
    {
        var raw = ClampInt(rawCorrect, 0, ListeningReadingRawMax);
        return raw >= ListeningReadingRawPass;
    }

    /// <summary>Determine Listening/Reading pass from scaled 0–500 score.</summary>
    public static bool IsListeningReadingPassByScaled(int scaled)
    {
        var s = ClampInt(scaled, ScaledMin, ScaledMax);
        return s >= ScaledPassGradeB;
    }

    /// <summary>
    /// Canonical Listening/Reading grading from a raw correct count.
    /// </summary>
    public static ListeningReadingResult GradeListeningReading(string subtest, int rawCorrect)
    {
        var raw = ClampInt(rawCorrect, 0, ListeningReadingRawMax);
        var scaled = OetRawToScaled(raw);
        var grade = OetGradeLetterFromScaled(scaled);
        return new ListeningReadingResult(
            subtest,
            raw,
            ListeningReadingRawMax,
            scaled,
            ScaledPassGradeB,
            "B",
            grade,
            scaled >= ScaledPassGradeB);
    }

    // -----------------------------------------------------------------------
    // Writing pass logic (country-aware)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Return the scaled Writing pass threshold for the given destination
    /// country. Returns <c>null</c> if the country cannot be resolved —
    /// callers MUST NOT default silently.
    /// </summary>
    public static WritingThreshold? GetWritingPassThreshold(string? country)
    {
        var code = NormalizeWritingCountry(country);
        if (code is null) return null;
        if (WritingGradeBCountries.Contains(code))
            return new WritingThreshold(ScaledPassGradeB, "B", code);
        if (WritingGradeCPlusCountries.Contains(code))
            return new WritingThreshold(ScaledPassGradeCPlus, "C+", code);
        return null;
    }

    /// <summary>
    /// Determine Writing pass/fail for a scaled 0–500 score with a
    /// mandatory destination country. If the country is missing or
    /// unsupported, <see cref="WritingResult.Passed"/> is <c>null</c>
    /// and <see cref="WritingResult.Reason"/> describes why.
    /// </summary>
    public static WritingResult GradeWriting(int scaled, string? country)
    {
        var resolved = GetWritingPassThreshold(country);
        if (resolved is null)
        {
            var reason = string.IsNullOrWhiteSpace(country)
                ? "country_required"
                : "country_unsupported";
            return new WritingResult(
                Passed: null,
                ScaledScore: ClampInt(scaled, ScaledMin, ScaledMax),
                RequiredScaled: null,
                RequiredGrade: null,
                Grade: OetGradeLetterFromScaled(scaled),
                Reason: reason,
                ProvidedCountry: country,
                SupportedCountries: SupportedWritingCountries);
        }

        var s = ClampInt(scaled, ScaledMin, ScaledMax);
        return new WritingResult(
            Passed: s >= resolved.Threshold,
            ScaledScore: s,
            RequiredScaled: resolved.Threshold,
            RequiredGrade: resolved.Grade,
            Grade: OetGradeLetterFromScaled(s),
            Reason: null,
            ProvidedCountry: resolved.Country,
            SupportedCountries: SupportedWritingCountries);
    }

    // -----------------------------------------------------------------------
    // Speaking pass logic (universal)
    // -----------------------------------------------------------------------

    /// <summary>Determine Speaking pass from a scaled 0–500 score.</summary>
    public static bool IsSpeakingPass(int scaled)
    {
        var s = ClampInt(scaled, ScaledMin, ScaledMax);
        return s >= ScaledPassGradeB;
    }

    /// <summary>Canonical Speaking grading result.</summary>
    public static SpeakingResult GradeSpeaking(int scaled)
    {
        var s = ClampInt(scaled, ScaledMin, ScaledMax);
        return new SpeakingResult(
            s,
            ScaledPassGradeB,
            "B",
            OetGradeLetterFromScaled(s),
            s >= ScaledPassGradeB);
    }

    // -----------------------------------------------------------------------
    // Pronunciation projection (authority: /rulebooks/pronunciation/common/assessment-criteria.json)
    // -----------------------------------------------------------------------
    //
    // Pronunciation accuracy/fluency/completeness/prosody are scored 0-100 by
    // the pronunciation ASR pipeline. The composite "overall" is projected
    // into the OET Speaking 0-500 scale via the canonical anchor table. The
    // projection is ADVISORY only — the authoritative Speaking scaled score
    // still comes from expert-reviewed speaking attempts.
    //
    // Anchors (overall → scaled):
    //   0 → 0
    //   60 → 300 (C+ US/QA pass threshold)
    //   70 → 350 (B pass — universal Speaking)
    //   80 → 400
    //   90 → 450
    //   100 → 500
    //
    // Between anchors, piecewise-linear interpolation. This KEEPS the 70↔350
    // anchor sacrosanct so the pronunciation engine never tells a learner
    // they are passing when the underlying overall < 70.

    /// <summary>
    /// Project a pronunciation overall score (0-100 composite) onto the
    /// 0-500 Speaking scaled scale. Advisory only.
    /// </summary>
    public static int PronunciationProjectedScaled(double overall0To100)
    {
        if (double.IsNaN(overall0To100)) return 0;
        var o = Math.Clamp(overall0To100, 0.0, 100.0);
        (double From, double To, int ScaledFrom, int ScaledTo)[] anchors =
        {
            (0,   60,  0,   300),
            (60,  70,  300, 350),
            (70,  80,  350, 400),
            (80,  90,  400, 450),
            (90,  100, 450, 500),
        };
        foreach (var (f, t, sf, st) in anchors)
        {
            if (o >= f && o <= t)
            {
                var ratio = (o - f) / (t - f == 0 ? 1 : (t - f));
                return (int)Math.Round(sf + ratio * (st - sf));
            }
        }
        return ScaledMax;
    }

    /// <summary>
    /// Project a pronunciation overall score into a full Speaking pass/fail result.
    /// </summary>
    public static SpeakingResult PronunciationProjectedBand(double overall0To100)
    {
        var scaled = PronunciationProjectedScaled(overall0To100);
        return GradeSpeaking(scaled);
    }

    // -----------------------------------------------------------------------
    // Display formatting
    // -----------------------------------------------------------------------

    /// <summary>Format a scaled score as "380/500".</summary>
    public static string FormatScaledScore(int scaled)
        => $"{ClampInt(scaled, ScaledMin, ScaledMax)}/{ScaledMax}";

    /// <summary>Format an L/R raw score as "35/42".</summary>
    public static string FormatRawLrScore(int rawCorrect)
        => $"{ClampInt(rawCorrect, 0, ListeningReadingRawMax)}/{ListeningReadingRawMax}";

    /// <summary>
    /// Format a combined L/R score line, e.g. "35/42 • 380/500 • Grade B".
    /// </summary>
    public static string FormatListeningReadingDisplay(int rawCorrect)
    {
        var r = GradeListeningReading("listening", rawCorrect);
        return $"{FormatRawLrScore(r.RawCorrect)} \u2022 {FormatScaledScore(r.ScaledScore)} \u2022 {OetGradeLabel(r.Grade)}";
    }
}

/// <summary>Writing threshold resolution result for a target country.</summary>
public sealed record WritingThreshold(int Threshold, string Grade, string Country);

/// <summary>Listening/Reading grading result.</summary>
public sealed record ListeningReadingResult(
    string Subtest,
    int RawCorrect,
    int RawMax,
    int ScaledScore,
    int RequiredScaled,
    string RequiredGrade,
    string Grade,
    bool Passed);

/// <summary>
/// Writing grading result. When <see cref="Passed"/> is <c>null</c>, the
/// caller must resolve the destination country before treating this as a
/// determination. <see cref="Reason"/> is populated in that case.
/// </summary>
public sealed record WritingResult(
    bool? Passed,
    int ScaledScore,
    int? RequiredScaled,
    string? RequiredGrade,
    string Grade,
    string? Reason,
    string? ProvidedCountry,
    IReadOnlyList<string> SupportedCountries)
{
    /// <summary>Subtest identifier, always "writing".</summary>
    public string Subtest => "writing";
}

/// <summary>Speaking grading result (country-independent).</summary>
public sealed record SpeakingResult(
    int ScaledScore,
    int RequiredScaled,
    string RequiredGrade,
    string Grade,
    bool Passed)
{
    /// <summary>Subtest identifier, always "speaking".</summary>
    public string Subtest => "speaking";
}
