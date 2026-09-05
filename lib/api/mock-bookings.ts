/**
 * Mock bookings: record mappers, live-room transitions, admin/expert
 * booking projections — extracted from `lib/api.ts`. Re-exported there,
 * so `@/lib/api` imports keep working.
 */
import { apiRequest, asArray, asRecord, toStringArray, type ApiRecord } from './client';
import type { MockBooking, MockDeliveryMode, MockSpeakingContent } from '../mock-data';

export const MOCK_DELIVERY_MODES: ReadonlySet<MockDeliveryMode> = new Set<MockDeliveryMode>([
  'computer', 'paper', 'oet_home',
]);

export function normalizeMockDeliveryMode(value: unknown): MockDeliveryMode | undefined {
  const v = typeof value === 'string' ? value.toLowerCase() : '';
  return MOCK_DELIVERY_MODES.has(v as MockDeliveryMode) ? (v as MockDeliveryMode) : undefined;
}

export function mapMockBooking(item: ApiRecord): MockBooking {
  return {
    id: String(item.id ?? item.bookingId ?? ''),
    bookingId: String(item.bookingId ?? item.id ?? ''),
    mockBundleId: String(item.mockBundleId ?? ''),
    mockAttemptId: item.mockAttemptId ? String(item.mockAttemptId) : null,
    tutorProfileId: item.tutorProfileId ? String(item.tutorProfileId) : null,
    title: item.title ? String(item.title) : item.mockBundleTitle ? String(item.mockBundleTitle) : undefined,
    scheduledStartAt: String(item.scheduledStartAt ?? ''),
    timezoneIana: String(item.timezoneIana ?? 'UTC'),
    status: String(item.status ?? 'scheduled'),
    deliveryMode: normalizeMockDeliveryMode(item.deliveryMode),
    liveRoomState: item.liveRoomState ? String(item.liveRoomState) : undefined,
    liveRoomTransitionVersion: typeof item.liveRoomTransitionVersion === 'number' ? item.liveRoomTransitionVersion : undefined,
    consentToRecording: Boolean(item.consentToRecording),
    rescheduleCount: Number(item.rescheduleCount ?? 0),
    refundDecision: item.refundDecision ? String(item.refundDecision) : null,
    refundIssued: Boolean(item.refundIssued),
    joinUrl: item.joinUrl ? String(item.joinUrl) : null,
    zoomJoinUrl: item.zoomJoinUrl ? String(item.zoomJoinUrl) : null,
    learnerNotes: item.learnerNotes ? String(item.learnerNotes) : null,
    releasePolicy: item.releasePolicy ? String(item.releasePolicy) : undefined,
    candidateCardVisible: typeof item.candidateCardVisible === 'boolean' ? item.candidateCardVisible : undefined,
    interlocutorCardVisible: typeof item.interlocutorCardVisible === 'boolean' ? item.interlocutorCardVisible : undefined,
    speakingPaperId: typeof item.speakingPaperId === 'string' ? item.speakingPaperId : undefined,
    speakingContent: mapMockSpeakingContent(item.speakingContent),
  };
}

export function mapMockSpeakingContent(value: unknown): MockSpeakingContent | null {
  if (!value || typeof value !== 'object') return null;
  const item = asRecord(value);
  const candidateCard = asRecord(item.candidateCard);
  const tasks = toStringArray(candidateCard.tasks).length > 0
    ? toStringArray(candidateCard.tasks)
    : toStringArray(item.tasks);
  return {
    role: typeof item.role === 'string' ? item.role : typeof candidateCard.role === 'string' ? candidateCard.role : undefined,
    setting: typeof item.setting === 'string' ? item.setting : typeof candidateCard.setting === 'string' ? candidateCard.setting : undefined,
    patient: typeof item.patient === 'string' ? item.patient : typeof candidateCard.patient === 'string' ? candidateCard.patient : undefined,
    task: typeof item.task === 'string' ? item.task : typeof candidateCard.task === 'string' ? candidateCard.task : undefined,
    brief: typeof item.brief === 'string' ? item.brief : typeof candidateCard.brief === 'string' ? candidateCard.brief : undefined,
    background: typeof item.background === 'string' ? item.background : typeof candidateCard.background === 'string' ? candidateCard.background : undefined,
    tasks,
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
    roleplayCount: typeof item.roleplayCount === 'number' ? item.roleplayCount : undefined,
    patientEmotion: typeof item.patientEmotion === 'string' ? item.patientEmotion : undefined,
    communicationGoal: typeof item.communicationGoal === 'string' ? item.communicationGoal : undefined,
    clinicalTopic: typeof item.clinicalTopic === 'string' ? item.clinicalTopic : undefined,
    criteriaFocus: toStringArray(item.criteriaFocus),
    disclaimer: typeof item.disclaimer === 'string' ? item.disclaimer : undefined,
  };
}

export type MockLiveRoomTargetState = 'in_progress' | 'completed' | 'tutor_no_show' | 'learner_no_show';

export interface MockLiveRoomTransitionOptions {
  reason?: string;
  clientTransitionId?: string;
}

function mockLiveRoomTransitionPayload(
  targetState: MockLiveRoomTargetState,
  options?: MockLiveRoomTransitionOptions,
) {
  return {
    targetState,
    ...(options?.reason ? { reason: options.reason } : {}),
    ...(options?.clientTransitionId ? { clientTransitionId: options.clientTransitionId } : {}),
  };
}

export async function transitionMockBookingLiveRoom(
  bookingId: string,
  targetState: MockLiveRoomTargetState,
  options?: MockLiveRoomTransitionOptions,
): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(`/v1/mock-bookings/${bookingId}/live-room/transition`, {
    method: 'POST',
    body: JSON.stringify(mockLiveRoomTransitionPayload(targetState, options)),
  });
  return mapMockBooking(response);
}

export async function transitionExpertMockBookingLiveRoom(
  bookingId: string,
  targetState: MockLiveRoomTargetState,
  options?: MockLiveRoomTransitionOptions,
): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(`/v1/expert/mocks/bookings/${encodeURIComponent(bookingId)}/live-room/transition`, {
    method: 'POST',
    body: JSON.stringify(mockLiveRoomTransitionPayload(targetState, options)),
  });
  return mapMockBooking(response);
}

export async function transitionAdminMockBookingLiveRoom(
  bookingId: string,
  targetState: MockLiveRoomTargetState,
  options?: MockLiveRoomTransitionOptions,
): Promise<MockBooking> {
  const response = await apiRequest<ApiRecord>(`/v1/admin/mock-bookings/${encodeURIComponent(bookingId)}/live-room/transition`, {
    method: 'POST',
    body: JSON.stringify(mockLiveRoomTransitionPayload(targetState, options)),
  });
  return mapMockBooking(response);
}

export interface AdminMockBookingRow extends MockBooking {
  learnerId?: string | null;
  learnerDisplayName?: string | null;
  learnerEmail?: string | null;
  assignedTutorId?: string | null;
  assignedTutorDisplayName?: string | null;
  assignedInterlocutorId?: string | null;
  assignedInterlocutorDisplayName?: string | null;
  mockBundleTitle?: string | null;
}

export async function fetchAdminMockBookings(
  params?: { from?: string; to?: string },
): Promise<{ items: AdminMockBookingRow[] }> {
  const q = new URLSearchParams();
  if (params?.from) q.set('from', params.from);
  if (params?.to) q.set('to', params.to);
  const qs = q.toString();
  const response = await apiRequest<ApiRecord>(`/v1/admin/mocks/bookings${qs ? `?${qs}` : ''}`);
  const items = asArray(response.items).map((row): AdminMockBookingRow => {
    const base = mapMockBooking(row);
    return {
      ...base,
      learnerId: typeof row.learnerId === 'string' ? row.learnerId : null,
      learnerDisplayName: typeof row.learnerDisplayName === 'string' ? row.learnerDisplayName : null,
      learnerEmail: typeof row.learnerEmail === 'string' ? row.learnerEmail : null,
      assignedTutorId: typeof row.assignedTutorId === 'string' ? row.assignedTutorId : null,
      assignedTutorDisplayName: typeof row.assignedTutorDisplayName === 'string' ? row.assignedTutorDisplayName : null,
      assignedInterlocutorId: typeof row.assignedInterlocutorId === 'string' ? row.assignedInterlocutorId : null,
      assignedInterlocutorDisplayName: typeof row.assignedInterlocutorDisplayName === 'string' ? row.assignedInterlocutorDisplayName : null,
      mockBundleTitle: typeof row.mockBundleTitle === 'string' ? row.mockBundleTitle : null,
    };
  });
  return { items };
}

export async function transitionAdminMockBookingLiveRoomState(
  bookingId: string,
  targetState: MockLiveRoomTargetState,
  options?: MockLiveRoomTransitionOptions,
): Promise<MockBooking> {
  return transitionAdminMockBookingLiveRoom(bookingId, targetState, options);
}

export async function fetchExpertMockBookings() {
  const response = await apiRequest<ApiRecord>('/v1/expert/mocks/bookings');
  return asArray(response.items).map(mapMockBooking);
}

/**
 * Mocks V2 Wave 6 — fetch a single booking projection from the tutor-side
 * endpoint. Returns the raw API record (instead of the learner-stripped
 * {@link MockBooking} shape) because the expert projection embeds the
 * `speakingContent.interlocutorCard` payload that the learner DTO must never
 * expose. The caller (expert speaking-room page) needs that raw payload to
 * render the cue prompts and patient background.
 */
export interface ExpertMockBookingDetail extends Omit<MockBooking, 'speakingContent' | 'speakingPaperId'> {
  assignedTutorId?: string | null;
  assignedInterlocutorId?: string | null;
  zoomStartUrl?: string | null;
  speakingPaperId?: string | null;
  speakingContent?: ExpertSpeakingContent | null;
}

export interface ExpertSpeakingInterlocutorCard {
  background?: string;
  patientProfile?: string;
  cuePrompts?: string[];
  prompts?: string[];
  objectives?: string[];
  hiddenInformation?: string;
  [key: string]: unknown;
}

export interface ExpertSpeakingContent {
  candidateCard?: Record<string, unknown>;
  interlocutorCard?: ExpertSpeakingInterlocutorCard;
  warmUpQuestions?: string[];
  prepTimeSeconds?: number;
  roleplayTimeSeconds?: number;
  patientEmotion?: string;
  communicationGoal?: string;
  clinicalTopic?: string;
  criteriaFocus?: string[];
  disclaimer?: string;
  background?: string;
  setting?: string;
  patient?: string;
  task?: string;
  role?: string;
  [key: string]: unknown;
}

export async function fetchExpertMockBookingDetail(bookingId: string): Promise<ExpertMockBookingDetail> {
  const response = await apiRequest<ApiRecord>(`/v1/expert/mocks/bookings/${encodeURIComponent(bookingId)}`);
  const base = mapMockBooking(response);
  return {
    ...base,
    assignedTutorId: typeof response.assignedTutorId === 'string' ? response.assignedTutorId : null,
    assignedInterlocutorId: typeof response.assignedInterlocutorId === 'string' ? response.assignedInterlocutorId : null,
    zoomStartUrl: typeof response.zoomStartUrl === 'string' ? response.zoomStartUrl : null,
    speakingPaperId: typeof response.speakingPaperId === 'string' ? response.speakingPaperId : null,
    speakingContent: (response.speakingContent as ExpertSpeakingContent | null | undefined) ?? null,
  };
}
