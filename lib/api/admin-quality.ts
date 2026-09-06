/**
 * Admin quality surfaces: mocks analytics, speaking calibration drift,
 * interlocutor onboarding — extracted from `lib/api.ts`. Re-exported
 * there, so `@/lib/api` imports keep working.
 */
import { ApiError, apiRequest } from './client';

export interface AdminMocksAnalyticsRevenueRow {
  packageCode: string;
  packageName: string;
  totalRevenue: number;
  currency: string;
}

export interface AdminMocksAnalyticsTutorWorkloadRow {
  tutorId: string;
  tutorName: string;
  pendingBookings: number;
  completedThisWeek: number;
}

export interface AdminMocksAnalyticsLowQualityRow {
  bundleId: string;
  bundleTitle: string;
  itemCount: number;
  flags: string[];
}

export interface AdminMocksAnalyticsWindow {
  start: string;
  end: string;
}

export interface AdminMocksAnalyticsAttemptsCompletion {
  started: number;
  completed: number;
  completionRate: number;
  window: AdminMocksAnalyticsWindow;
}

export interface AdminMocksAnalyticsReadinessDistribution {
  red: number;
  amber: number;
  green: number;
  darkGreen: number;
}

export interface AdminMocksAnalyticsAverageReadiness {
  sampleSize: number;
  averageScore: number | null;
  distribution: AdminMocksAnalyticsReadinessDistribution;
  window: AdminMocksAnalyticsWindow;
}

export interface AdminMocksAnalyticsPassPredictionProfessionRow {
  profession: string;
  sampleSize: number;
  predictedPassRate: number;
}

export interface AdminMocksAnalyticsPassPrediction {
  sampleSize: number;
  predictedPassRate: number | null;
  byProfession: AdminMocksAnalyticsPassPredictionProfessionRow[];
  window: AdminMocksAnalyticsWindow;
}

export interface AdminMocksAnalyticsMarkingDelayRow {
  subtest: 'writing' | 'speaking';
  sampleSize: number;
  avgDelayHours: number;
  p95DelayHours: number;
}

export interface AdminMocksAnalyticsMarkingDelay {
  perSubtest: AdminMocksAnalyticsMarkingDelayRow[];
  window: AdminMocksAnalyticsWindow;
}

/**
 * Phase 2 closure — Reading subtest aggregate across mock sessions.
 * Backend computes this from `MockSectionAttempt` rows with
 * `SubtestCode == "reading"`. Surfaced on the mocks analytics dashboard
 * so operators see Reading-in-mocks performance without drilling into
 * `/admin/analytics/reading`. Every field is nullable because the mock
 * pipeline may not yet have written any Reading sections.
 */
export interface AdminMocksAnalyticsReadingSection {
  started: number;
  submitted: number;
  completionRatePercent: number | null;
  averageRawScore: number | null;
  averageScaledScore: number | null;
  averageCompletionSeconds: number | null;
}

export interface AdminMocksAnalyticsResponse {
  revenueByPackage: AdminMocksAnalyticsRevenueRow[];
  tutorWorkload: AdminMocksAnalyticsTutorWorkloadRow[];
  lowQualityFlags: AdminMocksAnalyticsLowQualityRow[];
  readingSection: AdminMocksAnalyticsReadingSection;
  attemptsCompletion: AdminMocksAnalyticsAttemptsCompletion;
  averageReadiness: AdminMocksAnalyticsAverageReadiness;
  passPrediction: AdminMocksAnalyticsPassPrediction;
  markingDelay: AdminMocksAnalyticsMarkingDelay;
}

interface AdminMocksAnalyticsRootPayload {
  revenueByPackage?: AdminMocksAnalyticsRevenueRow[];
  tutorWorkload?: AdminMocksAnalyticsTutorWorkloadRow[];
  lowQualityFlags?: AdminMocksAnalyticsLowQualityRow[];
  readingSection?: AdminMocksAnalyticsReadingSection;
}

function emptyReadingSection(): AdminMocksAnalyticsReadingSection {
  return {
    started: 0,
    submitted: 0,
    completionRatePercent: null,
    averageRawScore: null,
    averageScaledScore: null,
    averageCompletionSeconds: null,
  };
}

function emptyAdminMocksAnalyticsWindow(): AdminMocksAnalyticsWindow {
  const now = new Date().toISOString();
  return { start: now, end: now };
}

function emptyAttemptsCompletion(): AdminMocksAnalyticsAttemptsCompletion {
  return { started: 0, completed: 0, completionRate: 0, window: emptyAdminMocksAnalyticsWindow() };
}

function emptyAverageReadiness(): AdminMocksAnalyticsAverageReadiness {
  return {
    sampleSize: 0,
    averageScore: null,
    distribution: { red: 0, amber: 0, green: 0, darkGreen: 0 },
    window: emptyAdminMocksAnalyticsWindow(),
  };
}

function emptyPassPrediction(): AdminMocksAnalyticsPassPrediction {
  return {
    sampleSize: 0,
    predictedPassRate: null,
    byProfession: [],
    window: emptyAdminMocksAnalyticsWindow(),
  };
}

function emptyMarkingDelay(): AdminMocksAnalyticsMarkingDelay {
  return { perSubtest: [], window: emptyAdminMocksAnalyticsWindow() };
}

async function fetchAdminMocksAnalyticsSubroute<T>(path: string, fallback: T): Promise<T> {
  try {
    return await apiRequest<T>(path);
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return fallback;
    }
    throw err;
  }
}

/**
 * GET /v1/admin/analytics/mocks (root) + 4 Phase 8a sub-routes fetched in parallel.
 *
 * Root returns revenue-by-package, tutor workload, and low-quality flagged
 * bundles. The four Phase 8a sub-routes layer on attempts-completion, average-
 * readiness, pass-prediction, and marking-delay aggregations. Tolerates 404 on
 * any individual route (endpoint not yet deployed) by substituting an empty
 * skeleton so the page can render its empty states.
 */
export async function fetchAdminMocksAnalytics(): Promise<AdminMocksAnalyticsResponse> {
  const rootPromise: Promise<AdminMocksAnalyticsRootPayload> = (async () => {
    try {
      return await apiRequest<AdminMocksAnalyticsRootPayload>('/v1/admin/analytics/mocks');
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        return {};
      }
      throw err;
    }
  })();

  const [root, attemptsCompletion, averageReadiness, passPrediction, markingDelay] = await Promise.all([
    rootPromise,
    fetchAdminMocksAnalyticsSubroute<AdminMocksAnalyticsAttemptsCompletion>(
      '/v1/admin/analytics/mocks/attempts-completion',
      emptyAttemptsCompletion(),
    ),
    fetchAdminMocksAnalyticsSubroute<AdminMocksAnalyticsAverageReadiness>(
      '/v1/admin/analytics/mocks/average-readiness',
      emptyAverageReadiness(),
    ),
    fetchAdminMocksAnalyticsSubroute<AdminMocksAnalyticsPassPrediction>(
      '/v1/admin/analytics/mocks/pass-prediction',
      emptyPassPrediction(),
    ),
    fetchAdminMocksAnalyticsSubroute<AdminMocksAnalyticsMarkingDelay>(
      '/v1/admin/analytics/mocks/marking-delay',
      emptyMarkingDelay(),
    ),
  ]);

  return {
    revenueByPackage: root.revenueByPackage ?? [],
    tutorWorkload: root.tutorWorkload ?? [],
    lowQualityFlags: root.lowQualityFlags ?? [],
    readingSection: root.readingSection ?? emptyReadingSection(),
    attemptsCompletion,
    averageReadiness,
    passPrediction,
    markingDelay,
  };
}

export interface SpeakingCalibrationDriftTutorRow {
  tutorId: string;
  tutorName: string;
  submissionCount: number;
  meanAbsoluteError: number;
  totalAbsoluteError: number;
  lastSubmittedAt: string;
}

export interface SpeakingCalibrationDriftSummary {
  tutors: SpeakingCalibrationDriftTutorRow[];
  sampleSize: number;
  samplesPublished: number;
}

export interface SpeakingCalibrationSampleSummaryRow {
  sampleId: string;
  title: string;
  description: string;
  sourceAttemptId: string;
  professionId: string;
  difficulty: string;
  status: string;
  goldScores: Record<string, number>;
  tutorSubmissionCount: number;
  createdAt: string;
  publishedAt: string | null;
}

export interface SpeakingCalibrationSamplesResponse {
  samples: SpeakingCalibrationSampleSummaryRow[];
}

/**
 * GET /v1/admin/speaking/calibration/drift?minSubmissions=1
 *
 * Drift report — for each tutor that has submitted ≥1 calibration rubric,
 * returns the mean absolute error vs the gold rubric across all 9 criteria.
 * Tolerates 404 (endpoint not yet wired) by returning empty arrays so the
 * page renders its empty state cleanly.
 */
export async function fetchSpeakingCalibrationSummary(
  minSubmissions: number = 1,
): Promise<SpeakingCalibrationDriftSummary> {
  const qs = `?minSubmissions=${encodeURIComponent(String(Math.max(1, minSubmissions)))}`;
  try {
    return await apiRequest<SpeakingCalibrationDriftSummary>(
      `/v1/admin/speaking/calibration/drift${qs}`,
    );
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return { tutors: [], sampleSize: 0, samplesPublished: 0 };
    }
    throw err;
  }
}

/**
 * GET /v1/admin/speaking/calibration/samples
 *
 * Returns the curated calibration sample set ("sets" in the calibration UI).
 * Each row carries the profession, status, and how many tutors have already
 * submitted rubric scores for that sample.
 */
export async function fetchSpeakingCalibrationSets(
  status?: string,
): Promise<SpeakingCalibrationSampleSummaryRow[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  try {
    const response = await apiRequest<SpeakingCalibrationSamplesResponse>(
      `/v1/admin/speaking/calibration/samples${qs}`,
    );
    return Array.isArray(response?.samples) ? response.samples : [];
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return [];
    }
    throw err;
  }
}

export type InterlocutorTrainingStatusLabel = 'In Progress' | 'Trained' | 'Failed';

export interface InterlocutorTraineeRow {
  traineeId: string;
  traineeName: string;
  startedAt: string | null;
  rolePlaysCompleted: number;
  calibrationSigma: number | null;
  status: InterlocutorTrainingStatusLabel;
  lastActivityAt: string | null;
}

export interface InterlocutorTraineesResponse {
  trainees: InterlocutorTraineeRow[];
  totalInOnboarding: number;
  totalTrained: number;
  totalDroppedOff: number;
}

interface AdminInterlocutorModuleRaw {
  id: string;
  title: string;
  orderIndex: number;
  contentMarkdown: string;
  requiredForCalibration: boolean;
  stage: string;
  status: string;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
}

function classifyTrainingStatus(
  sigma: number | null,
  rolePlays: number,
): InterlocutorTrainingStatusLabel {
  if (sigma === null) return 'In Progress';
  if (sigma <= 0.5 && rolePlays >= 1) return 'Trained';
  if (sigma > 1.0 && rolePlays >= 3) return 'Failed';
  return 'In Progress';
}

/**
 * GET /v1/admin/speaking/interlocutor-training/modules (+ drift report)
 *
 * Synthesises the admin-side trainee queue from the two signals the
 * backend currently exposes: published training modules and the per-tutor
 * calibration drift report. Each tutor with at least one calibration
 * submission is treated as a trainee in the onboarding pipeline.
 */
export async function fetchInterlocutorTrainees(): Promise<InterlocutorTraineesResponse> {
  // Modules are fetched purely to confirm onboarding pipeline state; the
  // synthesised trainee rows are derived from the drift report below.
  try {
    await apiRequest<AdminInterlocutorModuleRaw[]>(
      '/v1/admin/speaking/interlocutor-training/modules',
    );
  } catch (err) {
    if (!(err instanceof ApiError) || err.status !== 404) throw err;
  }

  const drift = await fetchSpeakingCalibrationSummary(1);

  const trainees: InterlocutorTraineeRow[] = drift.tutors.map((tutor) => {
    const sigma = Number.isFinite(tutor.meanAbsoluteError) ? tutor.meanAbsoluteError : null;
    const status = classifyTrainingStatus(sigma, tutor.submissionCount);
    return {
      traineeId: tutor.tutorId,
      traineeName: tutor.tutorName,
      startedAt: tutor.lastSubmittedAt ?? null,
      rolePlaysCompleted: tutor.submissionCount,
      calibrationSigma: sigma,
      status,
      lastActivityAt: tutor.lastSubmittedAt ?? null,
    };
  });

  const totalTrained = trainees.filter((t) => t.status === 'Trained').length;
  const totalDroppedOff = trainees.filter((t) => t.status === 'Failed').length;
  const totalInOnboarding = trainees.length - totalTrained - totalDroppedOff;

  return {
    trainees,
    totalInOnboarding: Math.max(0, totalInOnboarding),
    totalTrained,
    totalDroppedOff,
  };
}

export interface MarkInterlocutorTrainedResult {
  traineeId: string;
  status: InterlocutorTrainingStatusLabel;
  acknowledgedAt: string;
}

/**
 * POST /v1/admin/speaking/interlocutor-training/trainees/{id}/mark-trained
 *
 * Admin-side shortcut to mark a trainee as Trained. Tolerates 404 so the
 * UI can still surface the action even before the backend wires the
 * dedicated route — in that case the helper returns a synthetic
 * acknowledgement that the caller can use to optimistically update local
 * state.
 */
export async function markInterlocutorTrained(
  traineeId: string,
): Promise<MarkInterlocutorTrainedResult> {
  try {
    return await apiRequest<MarkInterlocutorTrainedResult>(
      `/v1/admin/speaking/interlocutor-training/trainees/${encodeURIComponent(traineeId)}/mark-trained`,
      { method: 'POST', body: JSON.stringify({}) },
      { json: true },
    );
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return {
        traineeId,
        status: 'Trained',
        acknowledgedAt: new Date().toISOString(),
      };
    }
    throw err;
  }
}

export interface InterlocutorPracticeSessionStart {
  sessionId: string;
  prepHref: string;
}

/**
 * POST /v1/admin/speaking/interlocutor-training/trainees/{id}/practice-session
 *
 * Creates (or resumes) a practice role-play session for an interlocutor
 * trainee and returns the session id + the prep-page URL the admin should
 * route to. Tolerates 404 by returning a placeholder route the page can
 * surface as a disabled state.
 */
export async function startInterlocutorPracticeSession(
  traineeId: string,
): Promise<InterlocutorPracticeSessionStart> {
  try {
    return await apiRequest<InterlocutorPracticeSessionStart>(
      `/v1/admin/speaking/interlocutor-training/trainees/${encodeURIComponent(traineeId)}/practice-session`,
      { method: 'POST', body: JSON.stringify({}) },
      { json: true },
    );
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return {
        sessionId: '',
        prepHref: '/speaking/select-profession',
      };
    }
    throw err;
  }
}

/**
 * Phase 7a alias for {@link fetchSpeakingCalibrationSummary}.
 *
 * Returns the per-tutor calibration drift overview surfaced on the admin
 * speaking calibration page.
 */
export async function fetchSpeakingCalibrationOverview(
  minSubmissions: number = 1,
): Promise<SpeakingCalibrationDriftSummary> {
  return fetchSpeakingCalibrationSummary(minSubmissions);
}

/**
 * Phase 7a alias for {@link fetchInterlocutorTrainees}.
 *
 * Returns the synthesised interlocutor trainee table for the admin
 * onboarding page.
 */
export async function fetchInterlocutorTraineeList(): Promise<InterlocutorTraineesResponse> {
  return fetchInterlocutorTrainees();
}

/**
 * Practice queue row — pending interlocutor practice recording under
 * review by the calibration team.
 */
export interface InterlocutorPracticeQueueRow {
  recordingId: string;
  traineeId: string;
  traineeName: string;
  submittedAt: string;
  durationSeconds: number;
  status: 'Pending' | 'UnderReview' | 'Returned';
}

export interface InterlocutorPracticeQueueResponse {
  recordings: InterlocutorPracticeQueueRow[];
  totalPending: number;
}

/**
 * GET /v1/admin/speaking/interlocutor-training/practice-queue
 *
 * Backend gap (Phase 7a): the dedicated practice queue endpoint is not
 * wired yet. The helper tolerates 404 by returning an empty queue so the
 * admin onboarding page renders the empty state instead of erroring.
 *
 * When the backend ships the endpoint this helper will start surfacing
 * real rows without UI changes.
 */
export async function fetchInterlocutorPracticeQueue(): Promise<InterlocutorPracticeQueueResponse> {
  try {
    return await apiRequest<InterlocutorPracticeQueueResponse>(
      '/v1/admin/speaking/interlocutor-training/practice-queue',
    );
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return { recordings: [], totalPending: 0 };
    }
    throw err;
  }
}
