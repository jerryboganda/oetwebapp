/**
 * Speaking grader calibration — typed admin client (owner spec 4 Oct 2026).
 *
 * Backed by `SpeakingGraderCalibrationEndpoints.cs` under `/v1/admin/speaking/grader-calibration`.
 * This is the expert side: Dr Hesham marks a performance BLIND — no endpoint here ever returns an AI
 * score, grade or rationale, so the number being validated cannot anchor the expert's marks.
 */
import { apiClient } from '@/lib/api';

const BASE = '/v1/admin/speaking/grader-calibration';

export type GraderCalibrationStatus = 'pending' | 'labelled' | 'excluded';

export interface GraderCalibrationCandidate {
  sessionId: string;
  professionId: string;
  cardTitle: string;
  finishedAt: string;
  elapsedSeconds: number;
  hasAudio: boolean;
}

export interface GraderCalibrationSampleRow {
  id: string;
  sessionId: string;
  professionId: string;
  cardTitle: string;
  hasAudio: boolean;
  status: GraderCalibrationStatus;
  expertOverallScaled: number | null;
  expertGrade: string | null;
  promotedAt: string;
  labelledAt: string | null;
  /** False when its audio expired or was deleted, the learner withdrew consent or the transcript was erased. */
  usable?: boolean;
}

export interface GraderCalibrationCoverage {
  total: number;
  labelled: number;
  pending: number;
  excluded: number;
  /** Labelled samples per expert grade: A, B, C+, C, D, E. */
  labelledByGrade: Record<string, number>;
  /** Labelled samples whose expert overall is 320-380 (the pass line is 350). */
  labelledNearPassLine: number;
  audioShare: number;
  requiredLabelled: number;
  requiredPerGrade: number;
  requiredNearPassLine: number;
  /** Labelled samples with an expert overall of 320-340 (just below the 350 pass line). */
  labelledBelowPassLine?: number;
  /** Labelled samples with an expert overall of 350-380. */
  labelledAtOrAbovePassLine?: number;
  /** How many marked performances each side of the pass line the report needs. */
  requiredEachSideOfPassLine?: number;
  requiredAudioShare: number;
  meetsCoverage: boolean;
  /** Plain-language list of what is still missing. */
  unmet: string[];
}

export interface GraderCalibrationOverview {
  coverage: GraderCalibrationCoverage;
  samples: GraderCalibrationSampleRow[];
}

export interface GraderCalibrationCriterion {
  code: string;
  label: string;
  family: 'linguistic' | 'clinical';
  max: number;
}

export interface GraderCalibrationCard {
  title: string;
  professionId: string;
  setting: string;
  candidateRole: string;
  interlocutorRole: string;
  background: string;
  tasks: string[];
}

export interface GraderCalibrationTranscriptLine {
  speaker: string;
  startMs: number;
  endMs: number;
  text: string;
}

export interface GraderCalibrationAudioClip {
  recordingId: string;
  durationSeconds: number;
  mimeType: string;
}

export interface GraderCalibrationLabel {
  scores: Record<string, number>;
  overallScaled: number;
  notes: string;
}

export interface GraderCalibrationSampleDetail {
  id: string;
  status: GraderCalibrationStatus;
  hasAudio: boolean;
  card: GraderCalibrationCard;
  transcript: GraderCalibrationTranscriptLine[];
  clips: GraderCalibrationAudioClip[];
  criteria: GraderCalibrationCriterion[];
  label: GraderCalibrationLabel | null;
  excludedReason: string;
}

export interface GraderCalibrationLabelBody {
  scores: Record<string, number>;
  overallScaled: number;
  notes: string;
}

export function adminGetGraderCalibration(): Promise<GraderCalibrationOverview> {
  return apiClient.get<GraderCalibrationOverview>(`${BASE}/`);
}

export function adminListGraderCalibrationCandidates(take = 50): Promise<GraderCalibrationCandidate[]> {
  return apiClient.get<GraderCalibrationCandidate[]>(`${BASE}/candidates?take=${take}`);
}

/** Promote a finished AI card. Keeps its audio for a year and writes an audit event. */
export function adminPromoteGraderCalibrationSample(sessionId: string): Promise<GraderCalibrationSampleRow> {
  return apiClient.post<GraderCalibrationSampleRow>(`${BASE}/samples`, { sessionId });
}

export function adminGetGraderCalibrationSample(id: string): Promise<GraderCalibrationSampleDetail> {
  return apiClient.get<GraderCalibrationSampleDetail>(`${BASE}/samples/${encodeURIComponent(id)}`);
}

/** Authorised path of one clip, for `fetchAuthorizedObjectUrl`. */
export function graderCalibrationAudioPath(sampleId: string, recordingId: string): string {
  return `${BASE}/samples/${encodeURIComponent(sampleId)}/audio/${encodeURIComponent(recordingId)}`;
}

export function adminLabelGraderCalibrationSample(
  id: string,
  body: GraderCalibrationLabelBody,
): Promise<GraderCalibrationSampleRow> {
  return apiClient.put<GraderCalibrationSampleRow>(`${BASE}/samples/${encodeURIComponent(id)}/label`, body);
}

export function adminExcludeGraderCalibrationSample(id: string, reason: string): Promise<GraderCalibrationSampleRow> {
  return apiClient.post<GraderCalibrationSampleRow>(`${BASE}/samples/${encodeURIComponent(id)}/exclude`, { reason });
}
