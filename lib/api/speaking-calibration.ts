/**
 * Speaking tutor calibration (admin samples + drift, tutor rubric
 * submission) — extracted from `lib/api.ts`. Re-exported there, so
 * `@/lib/api` imports keep working.
 *
 * Wave 4 of docs/SPEAKING-MODULE-PLAN.md. Three audiences:
 *   • Admin: CRUD over calibration samples, drift report.
 *   • Expert/tutor: list samples, submit rubric, post inline comments.
 *   • Learner: read inline comments on their attempt.
 */
import { apiRequest, asRecord, type ApiRecord } from './client';

export interface SpeakingCriterionRubric {
  intelligibility: number;
  fluency: number;
  appropriateness: number;
  grammarExpression: number;
  relationshipBuilding: number;
  patientPerspective: number;
  structure: number;
  informationGathering: number;
  informationGiving: number;
}

export interface AdminSpeakingCalibrationSampleRow {
  sampleId: string;
  title: string;
  description: string;
  sourceAttemptId: string;
  professionId: string;
  difficulty: string;
  status: 'draft' | 'published' | 'archived';
  goldScores: Partial<SpeakingCriterionRubric>;
  tutorSubmissionCount: number;
  createdAt: string;
  publishedAt: string | null;
}

function mapAdminCalibrationSampleRow(rec: ApiRecord): AdminSpeakingCalibrationSampleRow {
  const status = (typeof rec.status === 'string' ? rec.status : 'draft') as 'draft' | 'published' | 'archived';
  const gold = asRecord(rec.goldScores);
  return {
    sampleId: typeof rec.sampleId === 'string' ? rec.sampleId : '',
    title: typeof rec.title === 'string' ? rec.title : '',
    description: typeof rec.description === 'string' ? rec.description : '',
    sourceAttemptId: typeof rec.sourceAttemptId === 'string' ? rec.sourceAttemptId : '',
    professionId: typeof rec.professionId === 'string' ? rec.professionId : 'nursing',
    difficulty: typeof rec.difficulty === 'string' ? rec.difficulty : 'core',
    status,
    goldScores: Object.fromEntries(
      Object.entries(gold).filter(([, v]) => typeof v === 'number'),
    ) as Partial<SpeakingCriterionRubric>,
    tutorSubmissionCount: typeof rec.tutorSubmissionCount === 'number' ? rec.tutorSubmissionCount : 0,
    createdAt: typeof rec.createdAt === 'string' ? rec.createdAt : '',
    publishedAt: typeof rec.publishedAt === 'string' ? rec.publishedAt : null,
  };
}

export async function fetchAdminSpeakingCalibrationSamples(status?: string): Promise<AdminSpeakingCalibrationSampleRow[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  const json = await apiRequest<ApiRecord>(`/v1/admin/speaking/calibration/samples${qs}`);
  const items = Array.isArray(json.samples) ? json.samples.map(asRecord) : [];
  return items.map(mapAdminCalibrationSampleRow);
}

export async function createAdminSpeakingCalibrationSample(payload: {
  title: string;
  sourceAttemptId: string;
  goldScores: SpeakingCriterionRubric;
  description?: string;
  professionId?: string;
  difficulty?: string;
  calibrationNotes?: string;
}): Promise<AdminSpeakingCalibrationSampleRow> {
  const json = await apiRequest<ApiRecord>('/v1/admin/speaking/calibration/samples', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  return mapAdminCalibrationSampleRow(json);
}

export async function publishAdminSpeakingCalibrationSample(sampleId: string): Promise<AdminSpeakingCalibrationSampleRow> {
  const json = await apiRequest<ApiRecord>(
    `/v1/admin/speaking/calibration/samples/${encodeURIComponent(sampleId)}/publish`,
    { method: 'POST' },
  );
  return mapAdminCalibrationSampleRow(json);
}

export async function archiveAdminSpeakingCalibrationSample(sampleId: string): Promise<AdminSpeakingCalibrationSampleRow> {
  const json = await apiRequest<ApiRecord>(
    `/v1/admin/speaking/calibration/samples/${encodeURIComponent(sampleId)}/archive`,
    { method: 'POST' },
  );
  return mapAdminCalibrationSampleRow(json);
}

export interface AdminSpeakingCalibrationDriftRow {
  tutorId: string;
  tutorName: string;
  submissionCount: number;
  meanAbsoluteError: number;
  totalAbsoluteError: number;
  lastSubmittedAt: string;
}

export interface AdminSpeakingCalibrationDriftReport {
  tutors: AdminSpeakingCalibrationDriftRow[];
  sampleSize: number;
  samplesPublished: number;
}

export async function fetchAdminSpeakingCalibrationDrift(minSubmissions = 1): Promise<AdminSpeakingCalibrationDriftReport> {
  const json = await apiRequest<ApiRecord>(`/v1/admin/speaking/calibration/drift?minSubmissions=${minSubmissions}`);
  const tutors = Array.isArray(json.tutors) ? json.tutors.map(asRecord) : [];
  return {
    tutors: tutors.map((t) => ({
      tutorId: typeof t.tutorId === 'string' ? t.tutorId : '',
      tutorName: typeof t.tutorName === 'string' ? t.tutorName : '',
      submissionCount: typeof t.submissionCount === 'number' ? t.submissionCount : 0,
      meanAbsoluteError: typeof t.meanAbsoluteError === 'number' ? t.meanAbsoluteError : 0,
      totalAbsoluteError: typeof t.totalAbsoluteError === 'number' ? t.totalAbsoluteError : 0,
      lastSubmittedAt: typeof t.lastSubmittedAt === 'string' ? t.lastSubmittedAt : '',
    })),
    sampleSize: typeof json.sampleSize === 'number' ? json.sampleSize : 0,
    samplesPublished: typeof json.samplesPublished === 'number' ? json.samplesPublished : 0,
  };
}

export interface TutorSpeakingCalibrationSampleRow {
  sampleId: string;
  title: string;
  description: string;
  sourceAttemptId: string;
  professionId: string;
  difficulty: string;
  publishedAt: string | null;
  submitted: boolean;
  mySubmission: { submittedAt: string; totalAbsoluteError: number } | null;
}

export async function fetchTutorSpeakingCalibrationSamples(): Promise<TutorSpeakingCalibrationSampleRow[]> {
  const json = await apiRequest<ApiRecord>('/v1/expert/calibration/speaking/samples');
  const items = Array.isArray(json.samples) ? json.samples.map(asRecord) : [];
  return items.map((rec) => {
    const mine = rec.mySubmission ? asRecord(rec.mySubmission) : null;
    return {
      sampleId: typeof rec.sampleId === 'string' ? rec.sampleId : '',
      title: typeof rec.title === 'string' ? rec.title : '',
      description: typeof rec.description === 'string' ? rec.description : '',
      sourceAttemptId: typeof rec.sourceAttemptId === 'string' ? rec.sourceAttemptId : '',
      professionId: typeof rec.professionId === 'string' ? rec.professionId : 'nursing',
      difficulty: typeof rec.difficulty === 'string' ? rec.difficulty : 'core',
      publishedAt: typeof rec.publishedAt === 'string' ? rec.publishedAt : null,
      submitted: rec.submitted === true,
      mySubmission: mine
        ? {
            submittedAt: typeof mine.submittedAt === 'string' ? mine.submittedAt : '',
            totalAbsoluteError: typeof mine.totalAbsoluteError === 'number' ? mine.totalAbsoluteError : 0,
          }
        : null,
    };
  });
}

export interface TutorCalibrationSubmissionResult {
  sampleId: string;
  tutorId: string;
  submittedAt: string;
  totalAbsoluteError: number;
  perCriterionDelta: Partial<SpeakingCriterionRubric>;
}

export async function submitTutorSpeakingCalibrationScores(
  sampleId: string,
  scores: SpeakingCriterionRubric,
  notes?: string,
): Promise<TutorCalibrationSubmissionResult> {
  const json = await apiRequest<ApiRecord>(
    `/v1/expert/calibration/speaking/samples/${encodeURIComponent(sampleId)}/scores`,
    { method: 'POST', body: JSON.stringify({ scores, notes }) },
  );
  const delta = asRecord(json.perCriterionDelta);
  return {
    sampleId: typeof json.sampleId === 'string' ? json.sampleId : sampleId,
    tutorId: typeof json.tutorId === 'string' ? json.tutorId : '',
    submittedAt: typeof json.submittedAt === 'string' ? json.submittedAt : '',
    totalAbsoluteError: typeof json.totalAbsoluteError === 'number' ? json.totalAbsoluteError : 0,
    perCriterionDelta: Object.fromEntries(
      Object.entries(delta).filter(([, v]) => typeof v === 'number'),
    ) as Partial<SpeakingCriterionRubric>,
  };
}
