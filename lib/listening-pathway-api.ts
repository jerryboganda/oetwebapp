/**
 * Listening Pathway API client.
 *
 * Covers the Listening Module Phase 1 foundation:
 *   onboarding -> audio-check -> diagnostic -> pathway-generation -> results.
 *
 * Backend base route: /v1/listening-pathway/
 * (NOT /v1/listening/* — that namespace is reserved for the legacy V2
 * listening surface.)
 *
 * Pattern mirrors lib/reading-pathway-api.ts. Field names are normalised to
 * camelCase regardless of whether the backend returns PascalCase
 * (System.Text.Json default for ASP.NET Core records) or camelCase. Date
 * strings flow through as ISO strings — call sites parse them as needed.
 */

import { env } from './env';
import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';

// ─────────────────────────────────────────────────────────────────────────────
// Public type exports
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Flattened projection of the learner's listening profile.
 *
 * Mirrors backend `LearnerListeningProfileResponse`. Stage values:
 *   onboarding | audio_check | diagnostic | foundation | practice | mastery.
 */
export interface ListeningProfile {
  userId: string;
  targetBand: string;
  examDate: string | null;
  hoursPerWeek: number;
  profession: string;
  englishExposureSource: string;
  comfortBritish: number;
  comfortAustralian: number;
  comfortVarious: number;
  hasTakenBefore: boolean;
  previousScore: number | null;
  selfRatedSpeed: number;
  selfRatedNoteTaking: number;
  selfRatedSpelling: number;
  currentStage: string;
  currentReadinessScore: number | null;
  predictedScore: number | null;
  onboardingCompletedAt: string;
  audioCheckPassedAt: string | null;
  pathwayGeneratedAt: string | null;
  updatedAt: string;
}

/**
 * Onboarding intake captured before the audio check (§5.3).
 * Mirrors backend `StartOnboardingRequest`.
 */
export interface OnboardingPayload {
  targetBand: string;
  examDate: string | null;
  hoursPerWeek: number;
  profession: string;
  englishExposureSource: string;
  comfortBritish: number;
  comfortAustralian: number;
  comfortVarious: number;
  hasTakenBefore: boolean;
  previousScore: number | null;
  selfRatedSpeed: number;
  selfRatedNoteTaking: number;
  selfRatedSpelling: number;
}

/**
 * Self-reported playback check outcome (§5.4).
 */
export interface AudioCheckPayload {
  outcome: 'clear' | 'quiet' | 'failed';
  volumeLevel?: number;
}

/**
 * Single MCQ option projection for a learner-facing question.
 * Either tuple form `{ key, text }` or a raw record on the wire — the
 * normaliser collapses both into this shape.
 */
export interface DiagnosticQuestionOption {
  key: string;
  text: string;
}

/**
 * LEARNER-SAFE diagnostic question projection.
 * Never carries the correct answer, accepted synonyms, transcript evidence,
 * or explanation markdown.
 */
export interface DiagnosticQuestion {
  id: string;
  questionNumber: number;
  /** "A" | "B" | "C" | "accent_test" */
  part: 'A' | 'B' | 'C' | 'accent_test';
  /** gap_fill | mcq3 | mcq4 | ... */
  questionType: string;
  stem: string;
  /** Null for Part-A gap-fill items where the learner types a phrase. */
  options: DiagnosticQuestionOption[] | null;
  audioAssetId: string | null;
  /** Short-lived signed playback URL — never a raw S3 key. */
  audioPlaybackUrl: string | null;
  /** One or more of L1..L8. */
  subSkillTags: string[];
  /** "en-GB" | "en-AU" | "en-US" | "en-XX". */
  accent: string;
  /** 0 in diagnostic mode (no replays); >0 in practice modes. */
  maxReplays: number;
  /** False during diagnostic, true in review mode after submission. */
  transcriptAvailable: boolean;
}

/**
 * Single per-question answer payload inside a diagnostic submission.
 */
export interface DiagnosticAnswerInput {
  questionId: string;
  selectedOption?: string;
  learnerAnswer?: string;
  isUnknown: boolean;
  timeSpentSeconds: number;
  replaysUsed: number;
  markedForReview: boolean;
}

/**
 * Bulk submission of all 23 diagnostic answers plus optional notes (§6.3).
 */
export interface SubmitDiagnosticPayload {
  sessionId: string;
  answers: DiagnosticAnswerInput[];
  totalDurationSeconds: number;
  notesByQuestionId?: Record<string, string>;
}

/**
 * Rolling per-sub-skill mastery score (L1..L8).
 */
export interface SkillScore {
  skillCode: string;
  label: string;
  currentScore: number;
  diagnosticScore: number;
  questionsAttempted: number;
  questionsCorrect: number;
}

/**
 * Per-accent learner competence breakdown.
 */
export interface AccentProgress {
  /** british | australian | us | non_native */
  accent: string;
  label: string;
  accuracyPercentage: number;
  questionsAttempted: number;
  minutesListened: number;
  selfConfidenceRating: number;
}

/**
 * Spelling-vs-meaning example pair for the spelling-tolerance widget.
 */
export interface SpellingExample {
  wrong: string;
  right: string;
}

/**
 * Hero band of the diagnostic results page (§6.4).
 */
export interface DiagnosticHero {
  rawScore: number;
  totalQuestions: number;
  scaledScore: number;
  gradeLabel: string;
  confidenceLowerBound: number;
  confidenceUpperBound: number;
  targetBandLabel: string;
}

/**
 * Note-taking volume + dropped-detail analytics block (§6.4).
 */
export interface NoteTakingStats {
  charactersTyped: number;
  typicalRangeLow: number;
  typicalRangeHigh: number;
  droppedDetails: string[];
}

/**
 * Spelling-tolerance analytics block (§6.4).
 */
export interface SpellingStats {
  meaningCorrectSpellingWrong: number;
  examples: SpellingExample[];
}

/**
 * Time-on-task breakdown by part + hesitation flags (§6.4).
 */
export interface TimeAnalysis {
  partABreakdown: number;
  partBBreakdown: number;
  partCBreakdown: number;
  hesitationFlags: string[];
}

/**
 * One week of the generated 12-week roadmap (§6.4, §27).
 */
export interface RoadmapWeek {
  weekNumber: number;
  /** foundation | practice | mastery */
  phase: string;
  /** L1..L8 sub-skill codes targeted this week. */
  focusSkills: string[];
  /** Accent codes targeted this week. */
  focusAccents: string[];
  dailyMinutes: number;
  mockAtEndOfWeek: boolean;
  notes: string;
}

/**
 * Multi-section diagnostic results envelope rendered on §6.4.
 */
export interface DiagnosticResult {
  sessionId: string;
  submittedAt: string;
  hero: DiagnosticHero;
  skillRadar: SkillScore[];
  accentChart: AccentProgress[];
  noteTakingStats: NoteTakingStats;
  spellingStats: SpellingStats;
  timeAnalysis: TimeAnalysis;
  roadmap: RoadmapWeek[];
}

/**
 * Full deserialised pathway response for the roadmap screen.
 */
export interface Pathway {
  totalWeeks: number;
  generatedAt: string;
  weeks: RoadmapWeek[];
}

/**
 * Lightweight pathway-status probe used by the listening landing page.
 */
export interface StageInfo {
  hasProfile: boolean;
  currentStage: string;
  diagnosticCompletedAt?: string;
  pathwayGeneratedAt?: string;
  currentReadinessScore?: number;
  daysUntilExam?: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// HTTP helper — mirrors Reading's `api<T>` so tests can stub the same hooks.
// ─────────────────────────────────────────────────────────────────────────────

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl || '';
  return base ? `${base.replace(/\/$/, '')}${path}` : path;
}

const CSRF_SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

interface ApiError extends Error {
  status?: number;
  detail?: unknown;
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const token = await ensureFreshAccessToken();
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init?.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  const method = (init?.method ?? 'GET').toUpperCase();
  if (!CSRF_SAFE_METHODS.has(method) && typeof document !== 'undefined' && !headers.has('x-csrf-token')) {
    const csrfMatch = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
    if (csrfMatch) headers.set('x-csrf-token', csrfMatch[1]);
  }
  const res = await fetchWithTimeout(resolveUrl(path), { ...init, headers });
  if (!res.ok) {
    let detail: unknown = null;
    try { detail = await res.json(); } catch { /* ignore */ }
    const err = new Error(`HTTP ${res.status}`) as ApiError;
    err.status = res.status;
    err.detail = detail;
    throw err;
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Shape normalisers — backend may emit PascalCase or camelCase depending on
// the System.Text.Json policy applied by the route handler. Pick whichever is
// present.
// ─────────────────────────────────────────────────────────────────────────────

type RawRecord = Record<string, unknown>;

function asRecord(value: unknown): RawRecord {
  return value && typeof value === 'object' ? (value as RawRecord) : {};
}

/** Read a single field by either casing. */
function pick<T = unknown>(source: RawRecord, ...keys: string[]): T | undefined {
  for (const key of keys) {
    if (key in source) {
      return source[key] as T;
    }
    const alt = key.charAt(0).toUpperCase() + key.slice(1);
    if (alt in source) {
      return source[alt] as T;
    }
    const camel = key.charAt(0).toLowerCase() + key.slice(1);
    if (camel in source) {
      return source[camel] as T;
    }
  }
  return undefined;
}

function pickString(source: RawRecord, ...keys: string[]): string {
  const value = pick<unknown>(source, ...keys);
  return value === null || value === undefined ? '' : String(value);
}

function pickOptionalString(source: RawRecord, ...keys: string[]): string | null {
  const value = pick<unknown>(source, ...keys);
  if (value === null || value === undefined) return null;
  return String(value);
}

function pickNumber(source: RawRecord, fallback = 0, ...keys: string[]): number {
  const value = pick<unknown>(source, ...keys);
  if (typeof value === 'number') return value;
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number(value);
    if (!Number.isNaN(parsed)) return parsed;
  }
  return fallback;
}

function pickOptionalNumber(source: RawRecord, ...keys: string[]): number | null {
  const value = pick<unknown>(source, ...keys);
  if (value === null || value === undefined) return null;
  if (typeof value === 'number') return value;
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number(value);
    if (!Number.isNaN(parsed)) return parsed;
  }
  return null;
}

function pickBoolean(source: RawRecord, ...keys: string[]): boolean {
  const value = pick<unknown>(source, ...keys);
  return Boolean(value);
}

function pickStringArray(source: RawRecord, ...keys: string[]): string[] {
  const value = pick<unknown>(source, ...keys);
  if (Array.isArray(value)) return value.map((entry) => String(entry));
  return [];
}

function pickArray(source: RawRecord, ...keys: string[]): unknown[] {
  const value = pick<unknown>(source, ...keys);
  return Array.isArray(value) ? value : [];
}

/**
 * Parse a date-shaped value into an ISO string, or `null` when absent.
 * Accepts strings, numbers (epoch ms), and `Date` instances.
 */
function parseDateField(source: RawRecord, ...keys: string[]): string | null {
  const value = pick<unknown>(source, ...keys);
  if (value === null || value === undefined) return null;
  if (value instanceof Date) return value.toISOString();
  if (typeof value === 'number') return new Date(value).toISOString();
  if (typeof value === 'string') return value;
  return null;
}

function normalizeProfile(raw: unknown): ListeningProfile {
  const record = asRecord(raw);
  return {
    userId: pickString(record, 'userId'),
    targetBand: pickString(record, 'targetBand'),
    examDate: parseDateField(record, 'examDate'),
    hoursPerWeek: pickNumber(record, 0, 'hoursPerWeek'),
    profession: pickString(record, 'profession'),
    englishExposureSource: pickString(record, 'englishExposureSource'),
    comfortBritish: pickNumber(record, 0, 'comfortBritish'),
    comfortAustralian: pickNumber(record, 0, 'comfortAustralian'),
    comfortVarious: pickNumber(record, 0, 'comfortVarious'),
    hasTakenBefore: pickBoolean(record, 'hasTakenBefore'),
    previousScore: pickOptionalNumber(record, 'previousScore'),
    selfRatedSpeed: pickNumber(record, 0, 'selfRatedSpeed'),
    selfRatedNoteTaking: pickNumber(record, 0, 'selfRatedNoteTaking'),
    selfRatedSpelling: pickNumber(record, 0, 'selfRatedSpelling'),
    currentStage: pickString(record, 'currentStage') || 'onboarding',
    currentReadinessScore: pickOptionalNumber(record, 'currentReadinessScore'),
    predictedScore: pickOptionalNumber(record, 'predictedScore'),
    onboardingCompletedAt: parseDateField(record, 'onboardingCompletedAt') ?? '',
    audioCheckPassedAt: parseDateField(record, 'audioCheckPassedAt'),
    pathwayGeneratedAt: parseDateField(record, 'pathwayGeneratedAt'),
    updatedAt: parseDateField(record, 'updatedAt') ?? '',
  };
}

function normalizeQuestionOption(raw: unknown, fallbackKey: string): DiagnosticQuestionOption {
  if (raw && typeof raw === 'object') {
    const record = raw as RawRecord;
    const key = pick<unknown>(record, 'key', 'optionKey', 'value', 'id');
    const text = pick<unknown>(record, 'text', 'label', 'title');
    return {
      key: key === null || key === undefined ? fallbackKey : String(key),
      text: text === null || text === undefined ? '' : String(text),
    };
  }
  return { key: fallbackKey, text: raw === null || raw === undefined ? '' : String(raw) };
}

function normalizeQuestionOptions(raw: unknown): DiagnosticQuestionOption[] | null {
  if (raw === null || raw === undefined) return null;
  if (Array.isArray(raw)) {
    return raw.map((entry, index) => normalizeQuestionOption(entry, String.fromCharCode(65 + index)));
  }
  if (typeof raw === 'object') {
    return Object.entries(raw as RawRecord).map(([key, value]) => normalizeQuestionOption(value, key));
  }
  return null;
}

function normalizeDiagnosticQuestion(raw: unknown): DiagnosticQuestion {
  const record = asRecord(raw);
  const part = pickString(record, 'part') as DiagnosticQuestion['part'];
  return {
    id: pickString(record, 'id'),
    questionNumber: pickNumber(record, 0, 'questionNumber'),
    part: (['A', 'B', 'C', 'accent_test'] as const).includes(part) ? part : 'A',
    questionType: pickString(record, 'questionType'),
    stem: pickString(record, 'stem'),
    options: normalizeQuestionOptions(pick(record, 'options')),
    audioAssetId: pickOptionalString(record, 'audioAssetId'),
    audioPlaybackUrl: pickOptionalString(record, 'audioPlaybackUrl'),
    subSkillTags: pickStringArray(record, 'subSkillTags'),
    accent: pickString(record, 'accent'),
    maxReplays: pickNumber(record, 0, 'maxReplays'),
    transcriptAvailable: pickBoolean(record, 'transcriptAvailable'),
  };
}

function normalizeSkillScore(raw: unknown): SkillScore {
  const record = asRecord(raw);
  const code = pickString(record, 'skillCode');
  return {
    skillCode: code,
    label: pickString(record, 'label') || skillLabel(code),
    currentScore: pickNumber(record, 0, 'currentScore'),
    diagnosticScore: pickNumber(record, 0, 'diagnosticScore'),
    questionsAttempted: pickNumber(record, 0, 'questionsAttempted'),
    questionsCorrect: pickNumber(record, 0, 'questionsCorrect'),
  };
}

function normalizeAccentProgress(raw: unknown): AccentProgress {
  const record = asRecord(raw);
  const accent = pickString(record, 'accent');
  return {
    accent,
    label: pickString(record, 'label') || accentLabel(accent),
    accuracyPercentage: pickNumber(record, 0, 'accuracyPercentage'),
    questionsAttempted: pickNumber(record, 0, 'questionsAttempted'),
    minutesListened: pickNumber(record, 0, 'minutesListened'),
    selfConfidenceRating: pickNumber(record, 0, 'selfConfidenceRating'),
  };
}

function normalizeRoadmapWeek(raw: unknown): RoadmapWeek {
  const record = asRecord(raw);
  return {
    weekNumber: pickNumber(record, 0, 'weekNumber'),
    phase: pickString(record, 'phase'),
    focusSkills: pickStringArray(record, 'focusSkills'),
    focusAccents: pickStringArray(record, 'focusAccents'),
    dailyMinutes: pickNumber(record, 0, 'dailyMinutes'),
    mockAtEndOfWeek: pickBoolean(record, 'mockAtEndOfWeek'),
    notes: pickString(record, 'notes'),
  };
}

function normalizeHero(raw: unknown): DiagnosticHero {
  const record = asRecord(raw);
  return {
    rawScore: pickNumber(record, 0, 'rawScore'),
    totalQuestions: pickNumber(record, 0, 'totalQuestions'),
    scaledScore: pickNumber(record, 0, 'scaledScore'),
    gradeLabel: pickString(record, 'gradeLabel'),
    confidenceLowerBound: pickNumber(record, 0, 'confidenceLowerBound'),
    confidenceUpperBound: pickNumber(record, 0, 'confidenceUpperBound'),
    targetBandLabel: pickString(record, 'targetBandLabel'),
  };
}

function normalizeNoteTakingStats(raw: unknown): NoteTakingStats {
  const record = asRecord(raw);
  return {
    charactersTyped: pickNumber(record, 0, 'charactersTyped'),
    typicalRangeLow: pickNumber(record, 0, 'typicalRangeLow'),
    typicalRangeHigh: pickNumber(record, 0, 'typicalRangeHigh'),
    droppedDetails: pickStringArray(record, 'droppedDetails'),
  };
}

function normalizeSpellingStats(raw: unknown): SpellingStats {
  const record = asRecord(raw);
  return {
    meaningCorrectSpellingWrong: pickNumber(record, 0, 'meaningCorrectSpellingWrong'),
    examples: pickArray(record, 'examples').map((entry) => {
      const example = asRecord(entry);
      return {
        wrong: pickString(example, 'wrong'),
        right: pickString(example, 'right'),
      };
    }),
  };
}

function normalizeTimeAnalysis(raw: unknown): TimeAnalysis {
  const record = asRecord(raw);
  return {
    partABreakdown: pickNumber(record, 0, 'partABreakdown'),
    partBBreakdown: pickNumber(record, 0, 'partBBreakdown'),
    partCBreakdown: pickNumber(record, 0, 'partCBreakdown'),
    hesitationFlags: pickStringArray(record, 'hesitationFlags'),
  };
}

function normalizeDiagnosticResult(raw: unknown, fallbackSessionId?: string): DiagnosticResult {
  const record = asRecord(raw);
  return {
    sessionId: pickString(record, 'sessionId') || fallbackSessionId || '',
    submittedAt: parseDateField(record, 'submittedAt') ?? '',
    hero: normalizeHero(pick(record, 'hero')),
    skillRadar: pickArray(record, 'skillRadar').map(normalizeSkillScore),
    accentChart: pickArray(record, 'accentChart').map(normalizeAccentProgress),
    noteTakingStats: normalizeNoteTakingStats(pick(record, 'noteTakingStats')),
    spellingStats: normalizeSpellingStats(pick(record, 'spellingStats')),
    timeAnalysis: normalizeTimeAnalysis(pick(record, 'timeAnalysis')),
    roadmap: pickArray(record, 'roadmap').map(normalizeRoadmapWeek),
  };
}

function normalizePathway(raw: unknown): Pathway {
  const record = asRecord(raw);
  return {
    totalWeeks: pickNumber(record, 0, 'totalWeeks'),
    generatedAt: parseDateField(record, 'generatedAt') ?? '',
    weeks: pickArray(record, 'weeks').map(normalizeRoadmapWeek),
  };
}

function normalizeStageInfo(raw: unknown): StageInfo {
  const record = asRecord(raw);
  const diagnosticCompletedAt = parseDateField(record, 'diagnosticCompletedAt');
  const pathwayGeneratedAt = parseDateField(record, 'pathwayGeneratedAt');
  const readiness = pickOptionalNumber(record, 'currentReadinessScore');
  const daysUntilExam = pickOptionalNumber(record, 'daysUntilExam');
  const stage: StageInfo = {
    hasProfile: pickBoolean(record, 'hasProfile'),
    currentStage: pickString(record, 'currentStage') || 'onboarding',
  };
  if (diagnosticCompletedAt !== null) stage.diagnosticCompletedAt = diagnosticCompletedAt;
  if (pathwayGeneratedAt !== null) stage.pathwayGeneratedAt = pathwayGeneratedAt;
  if (readiness !== null) stage.currentReadinessScore = readiness;
  if (daysUntilExam !== null) stage.daysUntilExam = daysUntilExam;
  return stage;
}

// ─────────────────────────────────────────────────────────────────────────────
// Display label maps
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Map an L1..L8 sub-skill code to its human display label.
 */
export function skillLabel(code: string): string {
  switch (code) {
    case 'L1': return 'Detail capture';
    case 'L2': return 'Note-taking speed';
    case 'L3': return 'Spelling accuracy';
    case 'L4': return 'Gist comprehension';
    case 'L5': return 'Distractor recognition';
    case 'L6': return 'Inference';
    case 'L7': return 'Speaker stance';
    case 'L8': return 'Accent adaptation';
    default: return code;
  }
}

/**
 * Map an accent code to its human display label.
 * british -> "British", australian -> "Australian", us -> "North American",
 * non_native -> "Non-native".
 */
export function accentLabel(code: string): string {
  switch (code) {
    case 'british': return 'British';
    case 'australian': return 'Australian';
    case 'us': return 'North American';
    case 'non_native': return 'Non-native';
    default: return code;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// sessionStorage cache helpers — keep diagnostic results recoverable across
// soft navigations even if the API is briefly unavailable.
// ─────────────────────────────────────────────────────────────────────────────

function diagnosticCacheKey(sessionId: string): string {
  return `listening_diagnostic_result_${sessionId}`;
}

function cacheDiagnosticResult(sessionId: string, result: DiagnosticResult): void {
  if (typeof window === 'undefined') return;
  try {
    window.sessionStorage.setItem(diagnosticCacheKey(sessionId), JSON.stringify(result));
  } catch {
    /* sessionStorage may be unavailable (private mode, quota); ignore */
  }
}

function readCachedDiagnosticResult(sessionId: string): DiagnosticResult | null {
  if (typeof window === 'undefined') return null;
  try {
    const raw = window.sessionStorage.getItem(diagnosticCacheKey(sessionId));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as unknown;
    return normalizeDiagnosticResult(parsed, sessionId);
  } catch {
    return null;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Profile & Onboarding
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Fetch the learner's listening profile. Resolves to `null` when the learner
 * has not yet onboarded (HTTP 404).
 */
export async function getListeningProfile(): Promise<ListeningProfile | null> {
  try {
    const raw = await api<unknown>('/v1/listening-pathway/profile');
    return normalizeProfile(raw);
  } catch (error) {
    const status = (error as ApiError).status;
    if (status === 404) return null;
    throw error;
  }
}

/**
 * Submit onboarding intake and create / update the learner profile (§5.3).
 */
export async function submitOnboarding(payload: OnboardingPayload): Promise<ListeningProfile> {
  const raw = await api<unknown>('/v1/listening-pathway/onboarding', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  return normalizeProfile(raw);
}

/**
 * Record the outcome of the audio-playback self-check (§5.4) and advance the
 * learner to the diagnostic stage.
 */
export async function submitAudioCheck(payload: AudioCheckPayload): Promise<{
  success: boolean;
  currentStage: string;
  audioCheckPassedAt: string | null;
}> {
  const raw = await api<unknown>('/v1/listening-pathway/audio-check', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  const record = asRecord(raw);
  return {
    success: pickBoolean(record, 'success'),
    currentStage: pickString(record, 'currentStage') || 'diagnostic',
    audioCheckPassedAt: parseDateField(record, 'audioCheckPassedAt'),
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Diagnostic
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Begin a 23-question listening diagnostic (§6.1).
 */
export async function startDiagnostic(): Promise<{
  sessionId: string;
  totalQuestions: number;
  estimatedMinutes: number;
}> {
  const raw = await api<unknown>('/v1/listening-pathway/diagnostic/start', { method: 'POST' });
  const record = asRecord(raw);
  return {
    sessionId: pickString(record, 'sessionId'),
    totalQuestions: pickNumber(record, 23, 'totalQuestions'),
    estimatedMinutes: pickNumber(record, 30, 'estimatedMinutes'),
  };
}

/**
 * Load the learner-safe question projection for an in-progress diagnostic
 * session.
 */
export async function getDiagnosticQuestions(sessionId: string): Promise<DiagnosticQuestion[]> {
  const raw = await api<unknown>(
    `/v1/listening-pathway/diagnostic/sessions/${encodeURIComponent(sessionId)}/questions`,
  );
  const list = Array.isArray(raw) ? raw : pickArray(asRecord(raw), 'questions', 'items');
  return list.map(normalizeDiagnosticQuestion);
}

/**
 * Persist a single per-question attempt as the learner moves through the
 * diagnostic. Mirrors the design's auto-save semantics so the learner can
 * resume mid-flight.
 */
export async function submitDiagnosticAnswer(
  sessionId: string,
  answer: DiagnosticAnswerInput,
): Promise<{ accepted: boolean }> {
  const raw = await api<unknown>(
    `/v1/listening-pathway/diagnostic/sessions/${encodeURIComponent(sessionId)}/attempts/${encodeURIComponent(answer.questionId)}`,
    {
      method: 'POST',
      body: JSON.stringify(answer),
    },
  );
  const record = asRecord(raw);
  const accepted = pick<unknown>(record, 'accepted');
  return { accepted: accepted === undefined ? true : Boolean(accepted) };
}

/**
 * Auto-save learner scratch notes captured during practice or diagnostic
 * (§25.7). Pass `questionId` for per-question notes or omit it for session
 * notes.
 */
export async function saveSessionNotes(
  sessionId: string,
  payload: { questionId?: string; noteMarkdown: string },
): Promise<{ savedAt: string }> {
  const raw = await api<unknown>(
    `/v1/listening-pathway/practice/sessions/${encodeURIComponent(sessionId)}/notes`,
    {
      method: 'POST',
      body: JSON.stringify(payload),
    },
  );
  const record = asRecord(raw);
  return { savedAt: parseDateField(record, 'savedAt') ?? new Date().toISOString() };
}

/**
 * Submit all diagnostic answers and receive the full results envelope (§6.3,
 * §6.4). The response is cached in `window.sessionStorage` keyed by
 * `listening_diagnostic_result_{sessionId}` so the results screen can recover
 * even if the API briefly errors on the follow-up GET.
 */
export async function submitDiagnostic(payload: SubmitDiagnosticPayload): Promise<DiagnosticResult> {
  const raw = await api<unknown>('/v1/listening-pathway/diagnostic/submit', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  const result = normalizeDiagnosticResult(raw, payload.sessionId);
  cacheDiagnosticResult(payload.sessionId, result);
  return result;
}

/**
 * Fetch the persisted diagnostic results for a completed session. Falls back
 * to the `window.sessionStorage` cache populated by {@link submitDiagnostic}
 * when the API call fails.
 */
export async function getDiagnosticResults(sessionId: string): Promise<DiagnosticResult> {
  try {
    const raw = await api<unknown>(
      `/v1/listening-pathway/diagnostic-results/${encodeURIComponent(sessionId)}`,
    );
    const result = normalizeDiagnosticResult(raw, sessionId);
    cacheDiagnosticResult(sessionId, result);
    return result;
  } catch (error) {
    const cached = readCachedDiagnosticResult(sessionId);
    if (cached) return cached;
    throw error;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Pathway, Stage, and Analytics
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Fetch the learner's full 12-week listening roadmap.
 */
export async function getListeningPathway(): Promise<Pathway> {
  const raw = await api<unknown>('/v1/listening-pathway/pathway');
  return normalizePathway(raw);
}

/**
 * Lightweight stage probe — used by the landing page to decide which screen
 * to route the learner into.
 */
export async function getCurrentStage(): Promise<StageInfo> {
  const raw = await api<unknown>('/v1/listening-pathway/stage');
  return normalizeStageInfo(raw);
}

/**
 * Fetch rolling L1..L8 sub-skill scores for the skill-radar visualisation.
 * Missing labels are filled in from the local {@link skillLabel} map.
 */
export async function getSkillScores(): Promise<SkillScore[]> {
  const raw = await api<unknown>('/v1/listening-pathway/skills/scores');
  const list = Array.isArray(raw) ? raw : pickArray(asRecord(raw), 'skills', 'items');
  return list.map(normalizeSkillScore);
}

/**
 * Fetch per-accent progress for the accent-progress chart. Missing labels are
 * filled in from the local {@link accentLabel} map.
 */
export async function getAccentProgress(): Promise<AccentProgress[]> {
  const raw = await api<unknown>('/v1/listening-pathway/accents/progress');
  const list = Array.isArray(raw) ? raw : pickArray(asRecord(raw), 'accents', 'items');
  return list.map(normalizeAccentProgress);
}

// ─────────────────────────────────────────────────────────────────────────────
// Consolidated re-export — every function is already exported inline above;
// this block makes the public surface explicit at the bottom of the file for
// callers / tooling that prefer a single import-site for the module.
// ─────────────────────────────────────────────────────────────────────────────

export {
  getListeningProfile as fetchListeningProfile,
  submitOnboarding as submitListeningOnboarding,
  submitAudioCheck as submitListeningAudioCheck,
  startDiagnostic as startListeningDiagnostic,
  getDiagnosticQuestions as getListeningDiagnosticQuestions,
  submitDiagnosticAnswer as submitListeningDiagnosticAnswer,
  saveSessionNotes as saveListeningSessionNotes,
  submitDiagnostic as submitListeningDiagnostic,
  getDiagnosticResults as getListeningDiagnosticResults,
};
