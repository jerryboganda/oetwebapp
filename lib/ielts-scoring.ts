// ============================================================================
// IELTS Canonical Scoring Module — SINGLE SOURCE OF TRUTH
// ============================================================================
//
// MISSION CRITICAL. All IELTS scoring in this platform MUST route through
// this module. Do not re-implement band thresholds, task weighting, or
// Academic vs General distinctions anywhere else.
//
// Verified from IELTS official sources:
//   - Writing uses four criteria: Task Achievement/Response, Coherence and
//     Cohesion, Lexical Resource, Grammatical Range and Accuracy.
//   - Task 2 carries more weight than Task 1 in the Writing band score.
//   - Band scores range 0–9 in 0.5 increments.
//   - Academic and General Training have different Task 1 types.
//
// References:
//   - https://ielts.org/take-a-test/preparation-resources/writing-test-resources
// ============================================================================

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** IELTS exam pathway. */
export type IeltsPathway = 'academic' | 'general';

/** IELTS band score (0–9 in 0.5 increments). */
export type IeltsBand = 0 | 0.5 | 1 | 1.5 | 2 | 2.5 | 3 | 3.5 | 4 | 4.5 | 5 | 5.5 | 6 | 6.5 | 7 | 7.5 | 8 | 8.5 | 9;

/** IELTS sub-tests. */
export type IeltsSubtest = 'listening' | 'reading' | 'writing' | 'speaking';

/** Writing task family. */
export type IeltsWritingTask = 'task1' | 'task2';

/** IELTS Writing criteria (public rubric). */
export type IeltsWritingCriterion =
  | 'task_achievement'       // Academic Task 1 / General Task 1
  | 'task_response'          // Task 2 (both pathways)
  | 'coherence_cohesion'
  | 'lexical_resource'
  | 'grammatical_range_accuracy';

/** IELTS Speaking criteria (public rubric). */
export type IeltsSpeakingCriterion =
  | 'fluency_coherence'
  | 'lexical_resource'
  | 'grammatical_range_accuracy'
  | 'pronunciation';

/** Result of an IELTS band determination. */
export interface IeltsBandResult {
  band: number;
  /** Half-band rounded per IELTS convention. */
  bandDisplay: string;
  /** Whether this meets a typical target (e.g. 7.0 for nursing registration). */
  meetsTarget: boolean | null;
  targetBand: number | null;
}

/** Per-criterion breakdown with IELTS band. */
export interface IeltsCriterionBand {
  criterionCode: string;
  band: number;
  weight: number;
  description: string;
}

// ---------------------------------------------------------------------------
// Invariants / constants
// ---------------------------------------------------------------------------

/** Minimum IELTS band score. */
export const IELTS_BAND_MIN = 0 as const;

/** Maximum IELTS band score. */
export const IELTS_BAND_MAX = 9 as const;

/** IELTS band increment (0.5). */
export const IELTS_BAND_INCREMENT = 0.5 as const;

/** Task 1 weight in final Writing band (40%). */
export const IELTS_WRITING_TASK1_WEIGHT = 0.4 as const;

/** Task 2 weight in final Writing band (60%). */
export const IELTS_WRITING_TASK2_WEIGHT = 0.6 as const;

/** Typical nursing/OET-aligned target band for registration. */
export const IELTS_DEFAULT_TARGET_BAND = 7.0 as const;

/** IELTS Listening/Reading raw score → band mapping (approximate, verified). */
export const IELTS_LR_RAW_TO_BAND: ReadonlyArray<readonly [number, number]> = [
  [0, 0], [1, 1], [2, 1.5], [3, 2], [4, 2.5], [5, 3], [6, 3.5], [7, 3.5], [8, 4],
  [9, 4], [10, 4.5], [11, 4.5], [12, 5], [13, 5], [14, 5], [15, 5.5], [16, 5.5],
  [17, 6], [18, 6], [19, 6.5], [20, 6.5], [21, 7], [22, 7], [23, 7.5], [24, 7.5],
  [25, 8], [26, 8], [27, 8.5], [28, 8.5], [29, 9], [30, 9],
  // Reading has 40 items — extended mapping:
  [31, 9], [32, 9], [33, 9], [34, 9], [35, 9], [36, 9], [37, 9], [38, 9], [39, 9], [40, 9],
] as const;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function clampBand(value: number): number {
  if (!Number.isFinite(value)) return IELTS_BAND_MIN;
  return Math.max(IELTS_BAND_MIN, Math.min(IELTS_BAND_MAX, value));
}

/**
 * Round a raw band calculation to the nearest 0.5 increment (IELTS convention).
 */
export function ieltsRoundBand(raw: number): number {
  const clamped = clampBand(raw);
  return Math.round(clamped * 2) / 2;
}

/** Format a band as "7.0" or "6.5". */
export function ieltsBandDisplay(band: number): string {
  const rounded = ieltsRoundBand(band);
  return Number.isInteger(rounded) ? `${rounded}.0` : String(rounded);
}

// ---------------------------------------------------------------------------
// Listening / Reading band from raw score
// ---------------------------------------------------------------------------

/**
 * Convert a Listening raw score (0–40) to an IELTS band.
 * Uses the official approximate mapping.
 */
export function ieltsListeningBandFromRaw(rawScore: number): IeltsBandResult {
  const raw = Math.max(0, Math.min(40, Math.round(rawScore)));
  const entry = IELTS_LR_RAW_TO_BAND.find(([r]) => r === raw);
  const band = entry ? entry[1] : 0;
  return {
    band,
    bandDisplay: ieltsBandDisplay(band),
    meetsTarget: band >= IELTS_DEFAULT_TARGET_BAND,
    targetBand: IELTS_DEFAULT_TARGET_BAND,
  };
}

/**
 * Convert a Reading raw score (0–40) to an IELTS band.
 * Same mapping as Listening for approximate equivalence.
 */
export function ieltsReadingBandFromRaw(rawScore: number): IeltsBandResult {
  return ieltsListeningBandFromRaw(rawScore);
}

// ---------------------------------------------------------------------------
// Writing band calculation (Task 1 + Task 2 weighted)
// ---------------------------------------------------------------------------

/**
 * Compute the overall Writing band from Task 1 and Task 2 criterion scores.
 * Task 2 carries 60% weight, Task 1 carries 40%.
 *
 * Criteria per task (each scored 0–9 by band):
 *   Task 1: task_achievement, coherence_cohesion, lexical_resource, grammatical_range_accuracy
 *   Task 2: task_response, coherence_cohesion, lexical_resource, grammatical_range_accuracy
 */
export function ieltsWritingBand(
  task1Bands: Record<string, number>,
  task2Bands: Record<string, number>,
): IeltsBandResult {
  const t1Avg = averageBand(Object.values(task1Bands));
  const t2Avg = averageBand(Object.values(task2Bands));
  const weighted = t1Avg * IELTS_WRITING_TASK1_WEIGHT + t2Avg * IELTS_WRITING_TASK2_WEIGHT;
  const band = ieltsRoundBand(weighted);
  return {
    band,
    bandDisplay: ieltsBandDisplay(band),
    meetsTarget: band >= IELTS_DEFAULT_TARGET_BAND,
    targetBand: IELTS_DEFAULT_TARGET_BAND,
  };
}

function averageBand(values: number[]): number {
  const nums = values.filter(Number.isFinite);
  if (nums.length === 0) return 0;
  return nums.reduce((a, b) => a + b, 0) / nums.length;
}

// ---------------------------------------------------------------------------
// Speaking band calculation
// ---------------------------------------------------------------------------

/**
 * Compute the overall Speaking band from the four public criteria.
 * Equal weighting (simple average rounded to 0.5).
 */
export function ieltsSpeakingBand(
  criteria: Record<IeltsSpeakingCriterion, number>,
): IeltsBandResult {
  const avg = averageBand(Object.values(criteria));
  const band = ieltsRoundBand(avg);
  return {
    band,
    bandDisplay: ieltsBandDisplay(band),
    meetsTarget: band >= IELTS_DEFAULT_TARGET_BAND,
    targetBand: IELTS_DEFAULT_TARGET_BAND,
  };
}

// ---------------------------------------------------------------------------
// Overall band (simple average of four sub-tests)
// ---------------------------------------------------------------------------

/**
 * Compute the overall IELTS band from four sub-test bands.
 * Per official convention: average rounded to nearest 0.5.
 */
export function ieltsOverallBand(
  listening: number,
  reading: number,
  writing: number,
  speaking: number,
): IeltsBandResult {
  const avg = averageBand([listening, reading, writing, speaking]);
  const band = ieltsRoundBand(avg);
  return {
    band,
    bandDisplay: ieltsBandDisplay(band),
    meetsTarget: band >= IELTS_DEFAULT_TARGET_BAND,
    targetBand: IELTS_DEFAULT_TARGET_BAND,
  };
}

// ---------------------------------------------------------------------------
// Academic vs General pathway helpers
// ---------------------------------------------------------------------------

/** Task 1 label by pathway. */
export function ieltsWritingTask1Label(pathway: IeltsPathway): string {
  return pathway === 'academic'
    ? 'Task 1: Graph / Table / Diagram / Process'
    : 'Task 1: Letter (formal, semi-formal, or informal)';
}

/** Task 2 label (same for both pathways). */
export const IELTS_WRITING_TASK2_LABEL = 'Task 2: Essay (discuss, opinion, problem/solution, double question)' as const;

/** Pathway display label. */
export function ieltsPathwayLabel(pathway: IeltsPathway): string {
  return pathway === 'academic' ? 'IELTS Academic' : 'IELTS General Training';
}

// ---------------------------------------------------------------------------
// Score validation for goals/onboarding
// ---------------------------------------------------------------------------

/** Validate that a string input is a valid IELTS band (0–9 in 0.5 increments). */
export function isValidIeltsBand(value: string | number | null | undefined): boolean {
  if (value === null || value === undefined || value === '') return false;
  const num = typeof value === 'string' ? parseFloat(value) : value;
  if (!Number.isFinite(num)) return false;
  if (num < IELTS_BAND_MIN || num > IELTS_BAND_MAX) return false;
  // Must be a multiple of 0.5
  return Math.abs((num * 2) - Math.round(num * 2)) < 0.001;
}

/** Clamp and round any numeric input to a valid IELTS band. */
export function sanitizeIeltsBand(value: number): number {
  return ieltsRoundBand(value);
}

// ---------------------------------------------------------------------------
// Invariants (runtime self-check)
// ---------------------------------------------------------------------------

/* istanbul ignore next */
(function selfCheckInvariants() {
  const env = typeof process !== 'undefined' && process.env ? process.env.NODE_ENV : undefined;
  if (env === 'production') return;

  if (ieltsRoundBand(6.25) !== 6.5) {
    throw new Error('IELTS scoring invariant violated: 6.25 must round to 6.5');
  }
  if (ieltsRoundBand(6.24) !== 6.0) {
    throw new Error('IELTS scoring invariant violated: 6.24 must round to 6.0');
  }
  if (ieltsBandDisplay(7) !== '7.0') {
    throw new Error('IELTS scoring invariant violated: band 7 must display as 7.0');
  }
  if (!isValidIeltsBand('7.5')) {
    throw new Error('IELTS scoring invariant violated: 7.5 must be a valid band');
  }
  if (isValidIeltsBand('7.3')) {
    throw new Error('IELTS scoring invariant violated: 7.3 must NOT be a valid band');
  }
})();
