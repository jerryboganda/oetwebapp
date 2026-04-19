/**
 * ============================================================================
 * OET Canonical Scoring Module — SINGLE SOURCE OF TRUTH
 * ============================================================================
 *
 * MISSION CRITICAL. All OET scoring in this platform MUST route through this
 * module. Do not re-implement thresholds, raw→scaled conversion, or country
 * rules anywhere else. Any deviation is a critical system failure.
 *
 * Rules (verbatim from the project scoring spec):
 *
 *   LISTENING
 *     - Pass: 350/500 (Grade B)
 *     - Raw equivalent: 30/42 correct ≡ 350/500 EXACTLY
 *     - <30/42 = fail; ≥30/42 = pass
 *
 *   READING
 *     - Pass: 350/500 (Grade B)
 *     - Raw equivalent: 30/42 correct ≡ 350/500 EXACTLY
 *     - <30/42 = fail; ≥30/42 = pass
 *
 *   WRITING (country-dependent)
 *     - Grade B (350/500) → UK, Ireland, Australia, New Zealand, Canada
 *     - Grade C+ (300/500) → USA, Qatar
 *
 *   SPEAKING
 *     - Pass: 350/500 (Grade B), universal (no country variation)
 *
 * References:
 *   - https://edubenchmark.com/blog/oet-score-calculator-guide/
 *   - https://www.geniusclass.co.uk/oet-calculator
 *
 * ============================================================================
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** OET sub-tests. */
export type OetSubtest = 'listening' | 'reading' | 'writing' | 'speaking';

/** OET grade letters on the 0–500 scale. */
export type OetGrade = 'A' | 'B' | 'C+' | 'C' | 'D' | 'E';

/**
 * Supported destination countries for Writing threshold resolution.
 * Values are ISO-3166-1 alpha-2 codes (uppercase).
 *
 * GRADE_B countries  : GB (United Kingdom), IE (Ireland), AU (Australia),
 *                      NZ (New Zealand), CA (Canada)
 * GRADE_C_PLUS countries: US (United States of America), QA (Qatar)
 */
export type WritingGradeBCountry = 'GB' | 'IE' | 'AU' | 'NZ' | 'CA';
export type WritingGradeCPlusCountry = 'US' | 'QA';
export type SupportedWritingCountry =
  | WritingGradeBCountry
  | WritingGradeCPlusCountry;

/** Result of a pass/fail determination. */
export interface PassFailResult {
  /** True iff score meets or exceeds the required threshold. */
  passed: boolean;
  /** The scaled score (0–500) used for the determination. */
  scaledScore: number;
  /** The required pass threshold on the 0–500 scale for this case. */
  requiredScaled: number;
  /** Grade letter corresponding to the scaled score. */
  grade: OetGrade;
  /** Human label for the required grade (e.g. "Grade B" or "Grade C+"). */
  requiredGrade: Extract<OetGrade, 'B' | 'C+'>;
  /** Sub-test this result refers to. */
  subtest: OetSubtest;
}

/**
 * Returned when Writing pass/fail cannot be determined because no
 * supported target country was provided. Callers MUST surface this
 * explicitly rather than defaulting silently.
 */
export interface CountryRequiredResult {
  passed: null;
  reason: 'country_required' | 'country_unsupported';
  providedCountry: string | null | undefined;
  supportedCountries: readonly SupportedWritingCountry[];
  subtest: 'writing';
}

// ---------------------------------------------------------------------------
// Invariants / constants
// ---------------------------------------------------------------------------

/** Max raw score on OET Listening and Reading papers. */
export const OET_LR_RAW_MAX = 42 as const;

/** Raw-score pass threshold for OET Listening and Reading. */
export const OET_LR_RAW_PASS = 30 as const;

/** Scaled-score pass threshold for Grade B (universal). */
export const OET_SCALED_PASS_B = 350 as const;

/** Scaled-score pass threshold for Grade C+ (USA, Qatar Writing only). */
export const OET_SCALED_PASS_C_PLUS = 300 as const;

/** Scaled-score scale bounds. */
export const OET_SCALED_MIN = 0 as const;
export const OET_SCALED_MAX = 500 as const;

/** Countries whose Writing pass mark is Grade B (350/500). */
export const WRITING_GRADE_B_COUNTRIES: readonly WritingGradeBCountry[] = [
  'GB',
  'IE',
  'AU',
  'NZ',
  'CA',
] as const;

/** Countries whose Writing pass mark is Grade C+ (300/500). */
export const WRITING_GRADE_C_PLUS_COUNTRIES: readonly WritingGradeCPlusCountry[] = [
  'US',
  'QA',
] as const;

/** Union of all countries explicitly supported for Writing threshold. */
export const SUPPORTED_WRITING_COUNTRIES: readonly SupportedWritingCountry[] = [
  ...WRITING_GRADE_B_COUNTRIES,
  ...WRITING_GRADE_C_PLUS_COUNTRIES,
] as const;

// ---------------------------------------------------------------------------
// Country normalization
// ---------------------------------------------------------------------------

/**
 * Map of accepted aliases → canonical ISO alpha-2 code.
 * Keys are uppercased/trimmed before lookup.
 */
const COUNTRY_ALIASES: Readonly<Record<string, SupportedWritingCountry>> = {
  // United Kingdom
  GB: 'GB',
  UK: 'GB',
  'UNITED KINGDOM': 'GB',
  BRITAIN: 'GB',
  'GREAT BRITAIN': 'GB',
  ENGLAND: 'GB',
  SCOTLAND: 'GB',
  WALES: 'GB',
  'NORTHERN IRELAND': 'GB',

  // Ireland
  IE: 'IE',
  IRELAND: 'IE',
  'REPUBLIC OF IRELAND': 'IE',

  // Australia
  AU: 'AU',
  AUSTRALIA: 'AU',

  // New Zealand
  NZ: 'NZ',
  'NEW ZEALAND': 'NZ',

  // Canada
  CA: 'CA',
  CANADA: 'CA',

  // United States
  US: 'US',
  USA: 'US',
  'UNITED STATES': 'US',
  'UNITED STATES OF AMERICA': 'US',
  AMERICA: 'US',

  // Qatar
  QA: 'QA',
  QATAR: 'QA',
};

/**
 * Normalize any loosely-typed country input (ISO code, English name, etc.)
 * into the canonical alpha-2 code used by this module.
 *
 * Returns null if no mapping is found. Callers MUST treat null as
 * "country required / unsupported" and refuse to determine a Writing pass.
 */
export function normalizeWritingCountry(
  input: string | null | undefined,
): SupportedWritingCountry | null {
  if (input === null || input === undefined) return null;
  const key = String(input).trim().toUpperCase();
  if (key.length === 0) return null;
  return COUNTRY_ALIASES[key] ?? null;
}

// ---------------------------------------------------------------------------
// Raw ↔ Scaled conversion (Listening / Reading only)
// ---------------------------------------------------------------------------

function clampInt(value: number, min: number, max: number): number {
  if (!Number.isFinite(value)) {
    throw new RangeError('Raw score must be a finite number');
  }
  const rounded = Math.round(value);
  if (rounded < min) return min;
  if (rounded > max) return max;
  return rounded;
}

/**
 * Convert an OET Listening/Reading raw correct count (0–42) to the
 * scaled 0–500 score.
 *
 * INVARIANTS (never break these):
 *   oetRawToScaled(0)  === 0
 *   oetRawToScaled(30) === 350     ← the mission-critical anchor
 *   oetRawToScaled(42) === 500
 *
 * Piecewise linear with an exact pass anchor at 30:
 *   [0..30] → [0..350]   slope = 350/30
 *   [30..42] → [350..500] slope = 150/12
 *
 * Inputs outside 0..42 are clamped. Fractional raw counts are rounded.
 */
export function oetRawToScaled(rawCorrect: number): number {
  const raw = clampInt(rawCorrect, 0, OET_LR_RAW_MAX);

  if (raw === OET_LR_RAW_PASS) return OET_SCALED_PASS_B; // 350, exact
  if (raw === 0) return 0;
  if (raw === OET_LR_RAW_MAX) return OET_SCALED_MAX; // 500

  if (raw < OET_LR_RAW_PASS) {
    // 0 → 0, 30 → 350, linear in between
    const scaled = (raw * OET_SCALED_PASS_B) / OET_LR_RAW_PASS;
    return Math.round(scaled);
  }

  // 30 → 350, 42 → 500, linear in between
  const delta = raw - OET_LR_RAW_PASS;
  const span = OET_LR_RAW_MAX - OET_LR_RAW_PASS; // 12
  const scaled =
    OET_SCALED_PASS_B + (delta * (OET_SCALED_MAX - OET_SCALED_PASS_B)) / span;
  return Math.round(scaled);
}

// ---------------------------------------------------------------------------
// Grade band derivation
// ---------------------------------------------------------------------------

/**
 * Map a scaled 0–500 score to its OET grade letter.
 *
 * Bands (per OET public grade scale):
 *   A  : 450–500
 *   B  : 350–449
 *   C+ : 300–349
 *   C  : 200–299
 *   D  : 100–199
 *   E  :   0– 99
 */
export function oetGradeFromScaled(scaled: number): OetGrade {
  const s = clampInt(scaled, OET_SCALED_MIN, OET_SCALED_MAX);
  if (s >= 450) return 'A';
  if (s >= 350) return 'B';
  if (s >= 300) return 'C+';
  if (s >= 200) return 'C';
  if (s >= 100) return 'D';
  return 'E';
}

/** Human-friendly grade label, e.g. "Grade B" or "Grade C+". */
export function oetGradeLabel(grade: OetGrade): string {
  return `Grade ${grade}`;
}

// ---------------------------------------------------------------------------
// Listening / Reading pass logic
// ---------------------------------------------------------------------------

/**
 * Determine Listening/Reading pass from the raw correct count.
 * Equivalent to `rawCorrect >= 30` on a 42-item paper.
 */
export function isListeningReadingPassByRaw(rawCorrect: number): boolean {
  const raw = clampInt(rawCorrect, 0, OET_LR_RAW_MAX);
  return raw >= OET_LR_RAW_PASS;
}

/**
 * Determine Listening/Reading pass from the scaled 0–500 score.
 * Equivalent to `scaled >= 350`.
 */
export function isListeningReadingPassByScaled(scaled: number): boolean {
  const s = clampInt(scaled, OET_SCALED_MIN, OET_SCALED_MAX);
  return s >= OET_SCALED_PASS_B;
}

/**
 * Canonical Listening/Reading grading from a raw correct count.
 * Produces both raw and scaled equivalents plus grade and pass flag.
 */
export function gradeListeningReading(
  subtest: 'listening' | 'reading',
  rawCorrect: number,
): PassFailResult & { rawCorrect: number; rawMax: number } {
  const raw = clampInt(rawCorrect, 0, OET_LR_RAW_MAX);
  const scaled = oetRawToScaled(raw);
  const grade = oetGradeFromScaled(scaled);
  return {
    subtest,
    rawCorrect: raw,
    rawMax: OET_LR_RAW_MAX,
    scaledScore: scaled,
    requiredScaled: OET_SCALED_PASS_B,
    requiredGrade: 'B',
    grade,
    passed: scaled >= OET_SCALED_PASS_B,
  };
}

// ---------------------------------------------------------------------------
// Writing pass logic (country-aware)
// ---------------------------------------------------------------------------

/**
 * Return the scaled Writing pass threshold for the given destination country.
 * Returns null if the country cannot be resolved to a supported entry —
 * callers MUST NOT default silently.
 */
export function getWritingPassThreshold(
  country: string | null | undefined,
):
  | { threshold: typeof OET_SCALED_PASS_B; grade: 'B'; country: WritingGradeBCountry }
  | {
      threshold: typeof OET_SCALED_PASS_C_PLUS;
      grade: 'C+';
      country: WritingGradeCPlusCountry;
    }
  | null {
  const code = normalizeWritingCountry(country);
  if (code === null) return null;
  if ((WRITING_GRADE_B_COUNTRIES as readonly string[]).includes(code)) {
    return { threshold: OET_SCALED_PASS_B, grade: 'B', country: code as WritingGradeBCountry };
  }
  if ((WRITING_GRADE_C_PLUS_COUNTRIES as readonly string[]).includes(code)) {
    return {
      threshold: OET_SCALED_PASS_C_PLUS,
      grade: 'C+',
      country: code as WritingGradeCPlusCountry,
    };
  }
  return null;
}

/**
 * Determine Writing pass/fail for a scaled 0–500 score with a mandatory
 * destination country. Returns a discriminated result — success or an
 * explicit `CountryRequiredResult` if the country is missing/unsupported.
 */
export function gradeWriting(
  scaled: number,
  country: string | null | undefined,
): PassFailResult | CountryRequiredResult {
  const resolved = getWritingPassThreshold(country);
  if (resolved === null) {
    const reason: 'country_required' | 'country_unsupported' =
      country === null || country === undefined || String(country).trim() === ''
        ? 'country_required'
        : 'country_unsupported';
    return {
      passed: null,
      reason,
      providedCountry: country ?? null,
      supportedCountries: SUPPORTED_WRITING_COUNTRIES,
      subtest: 'writing',
    };
  }

  const s = clampInt(scaled, OET_SCALED_MIN, OET_SCALED_MAX);
  return {
    subtest: 'writing',
    scaledScore: s,
    requiredScaled: resolved.threshold,
    requiredGrade: resolved.grade,
    grade: oetGradeFromScaled(s),
    passed: s >= resolved.threshold,
  };
}

/** Type guard: narrows a `gradeWriting` result to the success case. */
export function isWritingPassFailResult(
  result: PassFailResult | CountryRequiredResult,
): result is PassFailResult {
  return result.passed !== null;
}

// ---------------------------------------------------------------------------
// Speaking pass logic (universal)
// ---------------------------------------------------------------------------

/** Determine Speaking pass from a scaled 0–500 score. */
export function isSpeakingPass(scaled: number): boolean {
  const s = clampInt(scaled, OET_SCALED_MIN, OET_SCALED_MAX);
  return s >= OET_SCALED_PASS_B;
}

/** Canonical Speaking grading result. Country is intentionally ignored. */
export function gradeSpeaking(scaled: number): PassFailResult {
  const s = clampInt(scaled, OET_SCALED_MIN, OET_SCALED_MAX);
  return {
    subtest: 'speaking',
    scaledScore: s,
    requiredScaled: OET_SCALED_PASS_B,
    requiredGrade: 'B',
    grade: oetGradeFromScaled(s),
    passed: s >= OET_SCALED_PASS_B,
  };
}

// ---------------------------------------------------------------------------
// Display formatting
// ---------------------------------------------------------------------------

/** Format a scaled score as "380/500". */
export function formatScaledScore(scaled: number): string {
  return `${clampInt(scaled, OET_SCALED_MIN, OET_SCALED_MAX)}/${OET_SCALED_MAX}`;
}

/** Format an L/R raw score as "35/42". */
export function formatRawLrScore(rawCorrect: number): string {
  return `${clampInt(rawCorrect, 0, OET_LR_RAW_MAX)}/${OET_LR_RAW_MAX}`;
}

/**
 * Format a combined L/R score line, e.g. "35/42 • 380/500 • Grade B".
 * Always shows raw, scaled, and grade so the 30/42≡350/500 mapping is
 * verifiable at a glance in every UI surface.
 */
export function formatListeningReadingDisplay(rawCorrect: number): string {
  const result = gradeListeningReading('listening', rawCorrect);
  return `${formatRawLrScore(result.rawCorrect)} \u2022 ${formatScaledScore(result.scaledScore)} \u2022 ${oetGradeLabel(result.grade)}`;
}

// ---------------------------------------------------------------------------
// Card scoring summary (Submission History, Progress lists, Dashboard tiles)
// ---------------------------------------------------------------------------

/**
 * Unified three-state pass summary used by every learner-facing list card.
 *
 * - `pass` / `fail` — scaled score available and resolved against the
 *   canonical threshold (country-aware for Writing).
 * - `pending` — no usable scaled score yet (evaluation in flight).
 * - `country_required` / `country_unsupported` — Writing-only; surfaced
 *   explicitly instead of silently defaulting to a threshold.
 */
export type SubmissionPassState =
  | 'pass'
  | 'fail'
  | 'pending'
  | 'country_required'
  | 'country_unsupported';

export interface CardScoreSummary {
  scaledScore: number | null;
  scoreLabel: string;
  passState: SubmissionPassState;
  grade: OetGrade | null;
  passLabel: string;
  requiredScaled: number | null;
}

function pendingCardSummary(
  reason: Extract<SubmissionPassState, 'pending' | 'country_required' | 'country_unsupported'>,
): CardScoreSummary {
  return {
    scaledScore: null,
    scoreLabel: 'Pending',
    passState: reason,
    grade: null,
    passLabel:
      reason === 'pending'
        ? 'Pending'
        : reason === 'country_required'
          ? 'Country required'
          : 'Unsupported country',
    requiredScaled: null,
  };
}

/**
 * Canonical helper for list-card score display. Every row in Submission
 * History, Readiness, Progress, etc. MUST route through this function.
 * Never format scores as percentages — the canonical 0–500 scale is the
 * only UI representation allowed per docs/SCORING.md.
 */
export function summarizeCardScore(
  subtest: OetSubtest | string | null | undefined,
  scaled: number | null | undefined,
  country: string | null | undefined,
): CardScoreSummary {
  const normalizedSubtest = (typeof subtest === 'string' ? subtest.trim().toLowerCase() : '') as OetSubtest;
  if (scaled === null || scaled === undefined || !Number.isFinite(scaled)) {
    return pendingCardSummary('pending');
  }
  const s = clampInt(scaled, OET_SCALED_MIN, OET_SCALED_MAX);
  const grade = oetGradeFromScaled(s);
  const scoreLabel = `${s} / ${OET_SCALED_MAX}`;

  if (normalizedSubtest === 'writing') {
    const result = gradeWriting(s, country ?? null);
    if (result.passed === null) {
      const summary = pendingCardSummary(result.reason);
      return { ...summary, scaledScore: s, scoreLabel, grade };
    }
    return {
      scaledScore: s,
      scoreLabel,
      passState: result.passed ? 'pass' : 'fail',
      grade,
      passLabel: result.passed
        ? `Pass (Grade ${result.requiredGrade})`
        : `Fail \u00b7 needs ${result.requiredScaled}`,
      requiredScaled: result.requiredScaled,
    };
  }

  if (normalizedSubtest === 'listening' || normalizedSubtest === 'reading' || normalizedSubtest === 'speaking') {
    const passed = s >= OET_SCALED_PASS_B;
    return {
      scaledScore: s,
      scoreLabel,
      passState: passed ? 'pass' : 'fail',
      grade,
      passLabel: passed ? 'Pass (Grade B)' : `Fail \u00b7 needs ${OET_SCALED_PASS_B}`,
      requiredScaled: OET_SCALED_PASS_B,
    };
  }

  // Unknown subtest — do not assert pass/fail.
  return {
    scaledScore: s,
    scoreLabel,
    passState: 'pending',
    grade,
    passLabel: 'Pending',
    requiredScaled: null,
  };
}

// ---------------------------------------------------------------------------
// Invariants (runtime self-check — runs at module load in non-production)
// ---------------------------------------------------------------------------
//
// Goal: catch a broken scoring build the instant it loads in dev/test/CI.
// Cost: a handful of integer comparisons, executed once per process.
// Safety: disabled in production so a future refactor that breaks an anchor
// cannot take down a live deployment on import — the .NET + TS test suites
// are the hard gate for production builds instead.

/* istanbul ignore next — trivial assertions, only executed at import time */
(function selfCheckInvariants() {
  const env =
    typeof process !== 'undefined' && process.env
      ? process.env.NODE_ENV
      : undefined;
  if (env === 'production') return;

  if (oetRawToScaled(OET_LR_RAW_PASS) !== OET_SCALED_PASS_B) {
    throw new Error(
      `OET scoring invariant violated: oetRawToScaled(${OET_LR_RAW_PASS}) must equal ${OET_SCALED_PASS_B}`,
    );
  }
  if (oetRawToScaled(0) !== 0) {
    throw new Error('OET scoring invariant violated: oetRawToScaled(0) must equal 0');
  }
  if (oetRawToScaled(OET_LR_RAW_MAX) !== OET_SCALED_MAX) {
    throw new Error(
      `OET scoring invariant violated: oetRawToScaled(${OET_LR_RAW_MAX}) must equal ${OET_SCALED_MAX}`,
    );
  }
  if (!isListeningReadingPassByRaw(OET_LR_RAW_PASS)) {
    throw new Error('OET scoring invariant violated: raw 30/42 must be a pass');
  }
  if (isListeningReadingPassByRaw(OET_LR_RAW_PASS - 1)) {
    throw new Error('OET scoring invariant violated: raw 29/42 must be a fail');
  }
})();
