import { ensureAttempt, cacheRemove, cacheSet, attemptCacheKey, evaluationCacheKey } from './attempt-cache';
import { apiRequest, asRecord, toStringArray, type ApiRecord } from './client';
import { mapSpeakingTask, titleCase } from './task-mappers';
import { scoreRangeDisplay, toConfidence, toEvalStatus, toExamFamilyCode } from './result-mappers';
import { normalizeWaveformPeaks } from '../domain/format';
import { uploadBinary } from './binary';
import { type PhrasingSegment, RoleCard, SpeakingResult, SpeakingTask, SpeakingTranscriptReview } from '../mock-data';

/**
 * Speaking tasks, mock sets, compliance, role cards, result, transcript, phrasing, recording submit.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchSpeakingTasks(): Promise<SpeakingTask[]> {
  const items = await apiRequest<ApiRecord[]>('/v1/speaking/tasks');
  return items.map(mapSpeakingTask);
}

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md - Speaking mock-set helpers.
// These are intentionally typed as ApiRecord-shaped objects so the
// orchestrator UI can stay loose while the backend contract stabilises;
// strict types will land alongside the admin authoring UI in Wave 3b.
export interface SpeakingMockSetSummary {
  mockSetId: string;
  title: string;
  description: string;
  difficulty: string;
  criteriaFocus: string[];
  tags: string[];
  rolePlay1ContentId: string;
  rolePlay2ContentId: string;
  publishedAt: string | null;
}

export interface SpeakingMockSetEntitlement {
  cap: number;
  used: number;
  remaining: number;
  windowDays: number;
  windowStartsAt: string;
}

export interface SpeakingMockSessionRolePlay {
  attemptId: string;
  contentId: string;
  title: string;
  scenarioType: string | null;
  state: string;
  evaluationId: string | null;
  evaluationState: string | null;
  estimatedScaledScore: number | null;
  readinessBand: string;
  readinessBandLabel: string;
}

export interface SpeakingMockSession {
  mockSessionId: string;
  mockSetId: string;
  title: string;
  description: string;
  mode: 'exam' | 'self';
  state: 'inprogress' | 'completed' | 'abandoned';
  startedAt: string;
  completedAt: string | null;
  criteriaFocus: string[];
  tags: string[];
  rolePlay1: SpeakingMockSessionRolePlay;
  rolePlay2: SpeakingMockSessionRolePlay;
  combined: {
    bothCompleted: boolean;
    estimatedScaledScore: number | null;
    passThreshold: number;
    readinessBand: string;
    readinessBandLabel: string;
  };
}

function mapSpeakingMockSession(json: ApiRecord): SpeakingMockSession {
  const role = (key: string): SpeakingMockSessionRolePlay => {
    const rec = asRecord(json[key]);
    return {
      attemptId: typeof rec.attemptId === 'string' ? rec.attemptId : '',
      contentId: typeof rec.contentId === 'string' ? rec.contentId : '',
      title: typeof rec.title === 'string' ? rec.title : '',
      scenarioType: typeof rec.scenarioType === 'string' ? rec.scenarioType : null,
      state: typeof rec.state === 'string' ? rec.state : 'inprogress',
      evaluationId: typeof rec.evaluationId === 'string' ? rec.evaluationId : null,
      evaluationState: typeof rec.evaluationState === 'string' ? rec.evaluationState : null,
      estimatedScaledScore: typeof rec.estimatedScaledScore === 'number' ? rec.estimatedScaledScore : null,
      readinessBand: typeof rec.readinessBand === 'string' ? rec.readinessBand : 'not_ready',
      readinessBandLabel: typeof rec.readinessBandLabel === 'string' ? rec.readinessBandLabel : 'Not ready',
    };
  };
  const combined = asRecord(json.combined);
  return {
    mockSessionId: typeof json.mockSessionId === 'string' ? json.mockSessionId : '',
    mockSetId: typeof json.mockSetId === 'string' ? json.mockSetId : '',
    title: typeof json.title === 'string' ? json.title : '',
    description: typeof json.description === 'string' ? json.description : '',
    mode: json.mode === 'self' ? 'self' : 'exam',
    state: (json.state === 'completed' || json.state === 'abandoned') ? json.state : 'inprogress',
    startedAt: typeof json.startedAt === 'string' ? json.startedAt : new Date().toISOString(),
    completedAt: typeof json.completedAt === 'string' ? json.completedAt : null,
    criteriaFocus: toStringArray(json.criteriaFocus),
    tags: toStringArray(json.tags),
    rolePlay1: role('rolePlay1'),
    rolePlay2: role('rolePlay2'),
    combined: {
      bothCompleted: combined.bothCompleted === true,
      estimatedScaledScore: typeof combined.estimatedScaledScore === 'number' ? combined.estimatedScaledScore : null,
      passThreshold: typeof combined.passThreshold === 'number' ? combined.passThreshold : 350,
      readinessBand: typeof combined.readinessBand === 'string' ? combined.readinessBand : 'not_ready',
      readinessBandLabel: typeof combined.readinessBandLabel === 'string' ? combined.readinessBandLabel : 'Not ready',
    },
  };
}

export async function fetchSpeakingMockSets(): Promise<{ mockSets: SpeakingMockSetSummary[]; entitlement: SpeakingMockSetEntitlement }> {
  const json = await apiRequest<ApiRecord>('/v1/speaking/mock-sets');
  const list = Array.isArray(json.mockSets) ? json.mockSets.map(asRecord) : [];
  const ent = asRecord(json.entitlement);
  return {
    mockSets: list.map((rec): SpeakingMockSetSummary => ({
      mockSetId: typeof rec.mockSetId === 'string' ? rec.mockSetId : '',
      title: typeof rec.title === 'string' ? rec.title : '',
      description: typeof rec.description === 'string' ? rec.description : '',
      difficulty: typeof rec.difficulty === 'string' ? rec.difficulty : 'core',
      criteriaFocus: toStringArray(rec.criteriaFocus),
      tags: toStringArray(rec.tags),
      rolePlay1ContentId: typeof rec.rolePlay1ContentId === 'string' ? rec.rolePlay1ContentId : '',
      rolePlay2ContentId: typeof rec.rolePlay2ContentId === 'string' ? rec.rolePlay2ContentId : '',
      publishedAt: typeof rec.publishedAt === 'string' ? rec.publishedAt : null,
    })),
    entitlement: {
      cap: typeof ent.cap === 'number' ? ent.cap : 1,
      used: typeof ent.used === 'number' ? ent.used : 0,
      remaining: typeof ent.remaining === 'number' ? ent.remaining : 1,
      windowDays: typeof ent.windowDays === 'number' ? ent.windowDays : 7,
      windowStartsAt: typeof ent.windowStartsAt === 'string' ? ent.windowStartsAt : new Date().toISOString(),
    },
  };
}

export async function startSpeakingMockSet(mockSetId: string, mode: 'exam' | 'self' = 'exam'): Promise<SpeakingMockSession> {
  const json = await apiRequest<ApiRecord>(`/v1/speaking/mock-sets/${mockSetId}/start`, {
    method: 'POST',
    body: JSON.stringify({ mode }),
  });
  return mapSpeakingMockSession(json);
}

export async function fetchSpeakingMockSession(sessionId: string): Promise<SpeakingMockSession> {
  const json = await apiRequest<ApiRecord>(`/v1/speaking/mock-sessions/${sessionId}`);
  return mapSpeakingMockSession(json);
}

export async function startSpeakingMockBridge(sessionId: string): Promise<SpeakingMockSession> {
  const json = await apiRequest<ApiRecord>(`/v1/speaking/mock-sessions/${sessionId}/bridge/start`, {
    method: 'POST',
    body: '{}',
  });
  return mapSpeakingMockSession(json);
}

export async function finishSpeakingMockBridge(sessionId: string): Promise<SpeakingMockSession> {
  const json = await apiRequest<ApiRecord>(`/v1/speaking/mock-sessions/${sessionId}/bridge/finish`, {
    method: 'POST',
    body: '{}',
  });
  return mapSpeakingMockSession(json);
}

export interface SpeakingComplianceCopy {
  consentText: string;
  scoreDisclaimer: string;
  audioRetentionDays: number;
}

export async function fetchSpeakingCompliance(): Promise<SpeakingComplianceCopy> {
  const json = await apiRequest<ApiRecord>('/v1/speaking/compliance');
  return {
    consentText: typeof json.consentText === 'string' ? json.consentText : 'I consent to this speaking recording being stored and processed for feedback.',
    scoreDisclaimer: typeof json.scoreDisclaimer === 'string' ? json.scoreDisclaimer : 'Estimated score only. This is not an official OET score or result.',
    audioRetentionDays: typeof json.audioRetentionDays === 'number' ? json.audioRetentionDays : 365,
  };
}

function mapRoleCardPayload(item: ApiRecord): RoleCard {
  const candidateCard = asRecord(item.candidateCard);
  const tasks = toStringArray(candidateCard.tasks).length > 0
    ? toStringArray(candidateCard.tasks)
    : toStringArray(item.tasks);
  const criteriaFocus = toStringArray(item.criteriaFocus ?? item.criteriaFocusTags);
  return {
    id: String(item.contentId ?? item.id ?? ''),
    title: String(item.title ?? 'Speaking role play'),
    profession: item.profession ? titleCase(item.profession) : titleCase(item.professionId),
    setting: String(candidateCard.setting ?? item.setting ?? 'Clinical setting'),
    patient: String(candidateCard.patient ?? candidateCard.patientRole ?? item.patient ?? 'Patient'),
    brief: String(candidateCard.brief ?? candidateCard.task ?? item.brief ?? item.task ?? item.caseNotes ?? ''),
    tasks,
    background: String(candidateCard.background ?? item.background ?? item.caseNotes ?? ''),
    candidateCard: {
      role: typeof candidateCard.role === 'string' ? candidateCard.role : undefined,
      candidateRole: typeof candidateCard.candidateRole === 'string' ? candidateCard.candidateRole : undefined,
      setting: typeof candidateCard.setting === 'string' ? candidateCard.setting : undefined,
      patient: typeof candidateCard.patient === 'string' ? candidateCard.patient : undefined,
      patientRole: typeof candidateCard.patientRole === 'string' ? candidateCard.patientRole : undefined,
      brief: typeof candidateCard.brief === 'string' ? candidateCard.brief : undefined,
      task: typeof candidateCard.task === 'string' ? candidateCard.task : undefined,
      background: typeof candidateCard.background === 'string' ? candidateCard.background : undefined,
      tasks,
    },
    warmUpQuestions: toStringArray(item.warmUpQuestions),
    prepTimeSeconds: typeof item.prepTimeSeconds === 'number' ? item.prepTimeSeconds : undefined,
    roleplayTimeSeconds: typeof item.roleplayTimeSeconds === 'number' ? item.roleplayTimeSeconds : undefined,
    patientEmotion: typeof item.patientEmotion === 'string' ? item.patientEmotion : undefined,
    communicationGoal: typeof item.communicationGoal === 'string' ? item.communicationGoal : undefined,
    clinicalTopic: typeof item.clinicalTopic === 'string' ? item.clinicalTopic : undefined,
    criteriaFocus,
    disclaimer: typeof item.disclaimer === 'string' ? item.disclaimer : undefined,
    sourceAttribution: typeof item.sourceAttribution === 'string' && item.sourceAttribution.trim()
      ? item.sourceAttribution
      : undefined,
  };
}

export async function fetchRoleCard(taskId: string): Promise<RoleCard> {
  const item = await apiRequest<ApiRecord>(`/v1/speaking/tasks/${taskId}`);
  return mapRoleCardPayload(item);
}

export async function fetchSpeakingResult(resultId: string): Promise<SpeakingResult> {
  const summary = await apiRequest<ApiRecord>(`/v1/speaking/evaluations/${resultId}/summary`);
  // Wave 1 of docs/SPEAKING-MODULE-PLAN.md: pass through criterion-keyed
  // feedback + readiness band so the results page can render the new card
  // without re-deriving the projection in the client.
  const rawCriteria = Array.isArray(summary.criteria)
    ? (summary.criteria as ApiRecord[])
    : Array.isArray(summary.criterionScores)
      ? (summary.criterionScores as ApiRecord[])
      : [];
  const criteria = rawCriteria
    .map((entry) => {
      const family = entry.family === 'clinical' ? 'clinical' : 'linguistic';
      const score = typeof entry.score === 'number' ? entry.score : Number(entry.score ?? 0);
      const max = typeof entry.max === 'number' ? entry.max : Number(entry.max ?? (family === 'clinical' ? 3 : 6));
      return {
        criterionCode: String(entry.criterionCode ?? ''),
        family,
        score: Number.isFinite(score) ? score : 0,
        max: Number.isFinite(max) ? max : (family === 'clinical' ? 3 : 6),
        scoreRange: typeof entry.scoreRange === 'string' ? entry.scoreRange : undefined,
        descriptor: typeof entry.descriptor === 'string' ? entry.descriptor : undefined,
        confidenceBand: typeof entry.confidenceBand === 'string' ? entry.confidenceBand : undefined,
        source: entry.source === 'ai_grounded' || entry.source === 'rulebook_fallback' ? entry.source : undefined,
        linkedRuleIds: Array.isArray(entry.linkedRuleIds) ? entry.linkedRuleIds.map(String) : [],
        explanation: typeof entry.explanation === 'string' ? entry.explanation : undefined,
      } as SpeakingResult['criteria'] extends (infer U)[] | undefined ? U : never;
    })
    .filter((entry) => entry.criterionCode.length > 0);

  const readinessBand = (() => {
    const code = summary.readinessBand;
    if (code === 'not_ready' || code === 'developing' || code === 'borderline' || code === 'exam_ready' || code === 'strong') {
      return code;
    }
    return undefined;
  })();

  return {
    id: resultId,
    taskId: summary.taskId,
    taskTitle: summary.taskTitle,
    examFamilyCode: toExamFamilyCode(summary.examFamilyCode),
    examFamilyLabel: summary.examFamilyLabel ?? titleCase(summary.examFamilyCode ?? 'oet'),
    scoreRange: scoreRangeDisplay(summary.scoreRange),
    confidence: toConfidence(summary.confidenceBand),
    confidenceLabel: summary.confidenceLabel ?? `${toConfidence(summary.confidenceBand)} confidence practice estimate`,
    learnerDisclaimer: summary.learnerDisclaimer ?? summary.disclaimer ?? `Practice estimate only. This is not an official ${summary.examFamilyLabel ?? 'exam'} score.`,
    methodLabel: summary.methodLabel ?? 'AI-assisted speaking evaluation',
    provenanceLabel: summary.provenanceLabel ?? `${summary.examFamilyLabel ?? 'Exam'} practice estimate`,
    humanReviewRecommended: Boolean(summary.humanReviewRecommended),
    escalationRecommended: Boolean(summary.escalationRecommended),
    isOfficialScore: Boolean(summary.isOfficialScore),
    strengths: summary.strengths ?? [],
    improvements: summary.issues ?? [],
    evalStatus: toEvalStatus(summary.state),
    submittedAt: summary.generatedAt ?? new Date().toISOString(),
    nextDrill: summary.nextDrill ?? undefined,
    recommendedDrills: Array.isArray(summary.recommendedDrills)
      ? summary.recommendedDrills.map((drill: ApiRecord) => ({
        id: String(drill.id ?? drill.route ?? drill.title ?? 'drill'),
        title: String(drill.title ?? 'Speaking drill'),
        description: String(drill.description ?? ''),
        route: typeof drill.route === 'string' ? drill.route : undefined,
      }))
      : undefined,
    criteria: criteria.length > 0 ? criteria : undefined,
    criteriaSource: summary.criteriaSource === 'ai_grounded' || summary.criteriaSource === 'rulebook_fallback' ? summary.criteriaSource : undefined,
    readinessBand,
    readinessBandLabel: typeof summary.readinessBandLabel === 'string' ? summary.readinessBandLabel : undefined,
    estimatedScaledScore: typeof summary.estimatedScaledScore === 'number' ? summary.estimatedScaledScore : undefined,
    passThreshold: typeof summary.passThreshold === 'number' ? summary.passThreshold : undefined,
    rubricMax: typeof summary.rubricMax === 'number' ? summary.rubricMax : undefined,
    statusReasonCode: typeof summary.statusReasonCode === 'string' ? summary.statusReasonCode : undefined,
    statusMessage: typeof summary.statusMessage === 'string' ? summary.statusMessage : undefined,
    retryable: typeof summary.retryable === 'boolean' ? summary.retryable : undefined,
    retryAfterMs: typeof summary.retryAfterMs === 'number' ? summary.retryAfterMs : undefined,
    timing: summary.timing
      ? {
        prepTimeSeconds: typeof summary.timing.prepTimeSeconds === 'number' ? summary.timing.prepTimeSeconds : undefined,
        roleplayTimeSeconds: typeof summary.timing.roleplayTimeSeconds === 'number' ? summary.timing.roleplayTimeSeconds : undefined,
        recordedSeconds: typeof summary.timing.recordedSeconds === 'number' ? summary.timing.recordedSeconds : undefined,
      }
      : undefined,
  };
}

export async function fetchTranscript(resultId: string): Promise<SpeakingTranscriptReview> {
  const review = await apiRequest<ApiRecord>(`/v1/speaking/evaluations/${resultId}/review`);
  const transcript = (review.transcript ?? []).map((line: ApiRecord) => ({
    id: line.id,
    speaker: titleCase(line.speaker),
    text: line.text,
    startTime: line.startTime ?? 0,
    endTime: line.endTime ?? 0,
    markers: (line.markers ?? []).map((marker: ApiRecord) => ({
      id: marker.id,
      type: marker.type,
      startTime: marker.startTime,
      endTime: marker.endTime,
      text: marker.text,
      suggestion: marker.suggestion,
    })),
  }));

  return {
    title: review.summary?.taskTitle ?? 'Speaking Transcript',
    date: review.summary?.generatedAt ? new Date(review.summary.generatedAt).toISOString().slice(0, 10) : new Date().toISOString().slice(0, 10),
    duration: transcript[transcript.length - 1]?.endTime ?? 0,
    transcript,
    audioAvailable: Boolean(review.audioAvailable),
    audioUrl: review.audioUrl ?? undefined,
    waveformPeaks: normalizeWaveformPeaks(review.analysis?.waveformPeaks),
    disclaimer: typeof review.disclaimer === 'string'
      ? review.disclaimer
      : typeof review.summary?.learnerDisclaimer === 'string'
        ? review.summary.learnerDisclaimer
        : undefined,
    roleCard: review.roleCard ? mapRoleCardPayload(asRecord(review.roleCard)) : undefined,
  };
}

export async function fetchPhrasingData(resultId: string): Promise<{ title: string; segments: PhrasingSegment[]; disclaimer?: string; recommendedDrills?: SpeakingResult['recommendedDrills'] }> {
  const review = await apiRequest<ApiRecord>(`/v1/speaking/evaluations/${resultId}/review`);
  return {
    title: review.summary?.taskTitle ?? 'Speaking Review',
    segments: (review.analysis?.phrasing ?? []).map((segment: ApiRecord) => ({
      id: segment.id,
      originalPhrase: segment.originalPhrase,
      issueExplanation: segment.issueExplanation,
      strongerAlternative: segment.strongerAlternative,
      drillPrompt: segment.drillPrompt,
    })),
    disclaimer: typeof review.disclaimer === 'string'
      ? review.disclaimer
      : typeof review.summary?.learnerDisclaimer === 'string'
        ? review.summary.learnerDisclaimer
        : undefined,
    recommendedDrills: Array.isArray(review.summary?.recommendedDrills)
      ? review.summary.recommendedDrills.map((drill: ApiRecord) => ({
        id: String(drill.id ?? drill.route ?? drill.title ?? 'drill'),
        title: String(drill.title ?? 'Speaking drill'),
        description: String(drill.description ?? ''),
        route: typeof drill.route === 'string' ? drill.route : undefined,
      }))
      : undefined,
  };
}

export async function submitSpeakingRecording(
  taskId: string,
  recording: Blob,
  durationSeconds = 120,
  mode: 'self' | 'exam' | 'practice' | 'diagnostic' = 'self',
  consent?: { accepted: boolean; text?: string },
  options?: { attemptId?: string; mockSessionId?: string; fileName?: string; captureMethod?: string; contentType?: string },
): Promise<{ uploadUrl: string; submissionId: string }> {
  const boundAttemptId = options?.attemptId?.trim();
  const attempt = boundAttemptId ? { attemptId: boundAttemptId } : await ensureAttempt('speaking', taskId, mode);
  const bindingQuery = new URLSearchParams({ contentId: taskId });
  if (options?.mockSessionId) bindingQuery.set('mockSessionId', options.mockSessionId);
  const bindingSuffix = `?${bindingQuery.toString()}`;
  const upload = await apiRequest<ApiRecord>(`/v1/speaking/attempts/${encodeURIComponent(attempt.attemptId)}/audio/upload-session${bindingSuffix}`, { method: 'POST' });
  await uploadBinary(upload.uploadUrl, recording);
  await apiRequest(`/v1/speaking/attempts/${encodeURIComponent(attempt.attemptId)}/audio/complete${bindingSuffix}`, {
    method: 'POST',
    body: JSON.stringify({
      uploadSessionId: upload.uploadSessionId,
      storageKey: upload.storageKey,
      fileName: options?.fileName ?? `${taskId}.webm`,
      sizeBytes: recording.size,
      durationSeconds,
      captureMethod: options?.captureMethod ?? 'browser-recording',
      contentType: options?.contentType ?? (recording.type || 'audio/webm'),
      consentAccepted: consent?.accepted === true,
      consentText: consent?.text,
      consentAcceptedAt: new Date().toISOString(),
    }),
  });
  const submitted = await apiRequest<ApiRecord>(`/v1/speaking/attempts/${encodeURIComponent(attempt.attemptId)}/submit${bindingSuffix}`, { method: 'POST' });
  const evaluationId = typeof submitted.evaluationId === 'string' ? submitted.evaluationId : '';
  if (!evaluationId) {
    throw new Error('Speaking evaluation was not queued. Please try again.');
  }

  if (!boundAttemptId) {
    cacheRemove(attemptCacheKey('speaking', taskId, mode));
  }
  cacheSet(evaluationCacheKey('speaking', taskId), evaluationId);
  return { uploadUrl: upload.uploadUrl, submissionId: evaluationId };
}

