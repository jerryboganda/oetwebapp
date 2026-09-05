/**
 * Zoom live classes + tutor (wave B1) surface — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest, isApiError } from './client';
import type { LiveClassJoinToken } from './private-speaking';

export interface LiveClassSessionSummary {
  id: string;
  scheduledStartAt: string;
  scheduledEndAt: string;
  capacity: number;
  enrolledCount: number;
  status: string;
  isEnrolled: boolean;
  isJoinAvailable: boolean;
  creditCost: number;
  /** Only present on admin endpoints */
  zoomMeetingId?: number | null;
  /** Only present on admin endpoints */
  zoomError?: string | null;
}

export interface LiveClassListItem {
  id: string;
  slug: string;
  title: string;
  titleAr?: string | null;
  description: string;
  descriptionAr?: string | null;
  type: string;
  professionTrack: string;
  level: string;
  tutorProfileId?: string | null;
  tutorDisplayName?: string | null;
  creditCost: number;
  status: string;
  coverImageUrl?: string | null;
  sessions: LiveClassSessionSummary[];
}

export interface LiveClassDetail extends LiveClassListItem {
  defaultDurationMinutes: number;
  defaultCapacity: number;
  tags: string[];
}

export interface LiveClassEnrollment {
  id: string;
  classSessionId: string;
  userId: string;
  enrolledAt: string;
  creditsCharged: number;
  status: string;
  cancelledAt?: string | null;
  cancellationReason?: string | null;
}

export interface LiveClassRecording {
  id: string;
  classSessionId: string;
  status: string;
  videoUrl?: string | null;
  transcriptUrl?: string | null;
  transcriptText?: string | null;
  aiSummary?: string | null;
  aiSummaryAr?: string | null;
  chapters: Array<{ startSeconds: number; title: string; summary: string }>;
  actionItems: string[];
  expiresAt?: string | null;
}

export interface AdminLiveClassUpsertPayload {
  title: string;
  titleAr?: string | null;
  description: string;
  descriptionAr?: string | null;
  type: string;
  professionTrack: string;
  level: string;
  tutorProfileId?: string | null;
  scheduledStartAt: string;
  durationMinutes: number;
  capacity: number;
  creditCost: number;
  coverImageUrl?: string | null;
  tags?: string[];
  autoPublish?: boolean;
}

export interface LiveClassQueryParams {
  professionTrack?: string;
  type?: string;
  tutorProfileId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

function liveClassQuery(params?: LiveClassQueryParams) {
  const qs = new URLSearchParams();
  if (params?.professionTrack) qs.set('professionTrack', params.professionTrack);
  if (params?.type) qs.set('type', params.type);
  if (params?.tutorProfileId) qs.set('tutorProfileId', params.tutorProfileId);
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return qs.toString();
}

export async function fetchLiveClasses(params?: LiveClassQueryParams): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>(`/v1/classes?${liveClassQuery(params)}`);
}

export async function fetchLiveClassDetail(idOrSlug: string): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(`/v1/classes/${encodeURIComponent(idOrSlug)}`);
}

export async function enrollLiveClassSession(sessionId: string, idempotencyKey?: string): Promise<LiveClassEnrollment> {
  return apiRequest<LiveClassEnrollment>(`/v1/classes/sessions/${encodeURIComponent(sessionId)}/enroll`, {
    method: 'POST',
    body: JSON.stringify({ idempotencyKey }),
  });
}

export async function cancelLiveClassEnrollment(sessionId: string, reason?: string): Promise<LiveClassEnrollment> {
  return apiRequest<LiveClassEnrollment>(`/v1/classes/sessions/${encodeURIComponent(sessionId)}/cancel-enrollment`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function fetchLiveClassJoinToken(sessionId: string): Promise<LiveClassJoinToken> {
  return apiRequest<LiveClassJoinToken>(`/v1/classes/sessions/${encodeURIComponent(sessionId)}/join-token`, {
    method: 'POST',
  });
}

export async function fetchMyUpcomingLiveClasses(): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>('/v1/classes/me/upcoming');
}

export async function fetchMyPastLiveClasses(): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>('/v1/classes/me/past');
}

export async function fetchLiveClassRecording(sessionId: string): Promise<LiveClassRecording> {
  return apiRequest<LiveClassRecording>(`/v1/classes/sessions/${encodeURIComponent(sessionId)}/recording`);
}

export async function fetchExpertLiveClasses(): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>('/v1/expert/live-classes');
}

export async function fetchExpertLiveClassJoinToken(sessionId: string): Promise<LiveClassJoinToken> {
  return apiRequest<LiveClassJoinToken>(`/v1/expert/live-classes/sessions/${encodeURIComponent(sessionId)}/join-token`, {
    method: 'POST',
  });
}

export async function fetchAdminLiveClasses(params?: LiveClassQueryParams): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>(`/v1/admin/live-classes?${liveClassQuery({ ...params, pageSize: params?.pageSize ?? 50 })}`);
}

export async function createAdminLiveClass(payload: AdminLiveClassUpsertPayload): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>('/v1/admin/live-classes', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function publishAdminLiveClass(liveClassId: string): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(`/v1/admin/live-classes/${encodeURIComponent(liveClassId)}/publish`, {
    method: 'POST',
  });
}

export async function updateAdminLiveClassSession(sessionId: string, payload: { scheduledStartAt?: string; durationMinutes?: number; capacity?: number; cancellationReason?: string }): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(`/v1/admin/live-classes/sessions/${encodeURIComponent(sessionId)}`, {
    method: 'PATCH',
    body: JSON.stringify(payload),
  });
}

export async function cancelAdminLiveClassSession(sessionId: string, reason?: string): Promise<void> {
  await apiRequest(`/v1/admin/live-classes/sessions/${encodeURIComponent(sessionId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function fetchAdminLiveClassAnalytics() {
  return apiRequest('/v1/admin/live-classes/analytics');
}

export async function fetchAdminLiveClassDetail(idOrSlug: string): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(`/v1/admin/live-classes/${encodeURIComponent(idOrSlug)}`);
}

export async function addAdminLiveClassSession(
  classId: string,
  payload: { scheduledStartAt: string; durationMinutes?: number; capacity?: number },
): Promise<LiveClassSessionSummary> {
  return apiRequest<LiveClassSessionSummary>(`/v1/admin/live-classes/${encodeURIComponent(classId)}/sessions`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function retryAdminLiveClassSessionZoom(sessionId: string): Promise<void> {
  await apiRequest(`/v1/admin/live-classes/sessions/${encodeURIComponent(sessionId)}/retry-zoom`, {
    method: 'POST',
  });
}

export type DayOfWeekString =
  | 'Sunday'
  | 'Monday'
  | 'Tuesday'
  | 'Wednesday'
  | 'Thursday'
  | 'Friday'
  | 'Saturday';

export interface TutorProfile {
  id: string;
  userId: string;
  displayName: string;
  displayNameAr?: string | null;
  bio: string;
  bioAr?: string | null;
  avatarUrl?: string | null;
  specialties: string[];
  languages: string[];
  hourlyRateUsd?: number | null;
  timeZone: string;
  zoomUserId?: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface TutorUpsertPayload {
  displayName: string;
  displayNameAr?: string | null;
  bio?: string | null;
  bioAr?: string | null;
  avatarUrl?: string | null;
  specialties?: string[];
  languages?: string[];
  hourlyRateUsd?: number | null;
  timeZone?: string | null;
  isActive?: boolean | null;
}

export interface TutorAvailabilitySlot {
  id: string;
  /** Server emits .NET DayOfWeek either as enum number (0–6) or name. We accept both. */
  dayOfWeek: number | DayOfWeekString;
  /** TimeOnly serializes as `HH:mm:ss`. */
  startTime: string;
  /** TimeOnly serializes as `HH:mm:ss`. */
  endTime: string;
  isActive: boolean;
}

export interface TutorAvailabilityUpsertPayload {
  /** Accepts the .NET DayOfWeek enum index or name. */
  dayOfWeek: number | DayOfWeekString;
  startTime: string;
  endTime: string;
  isActive: boolean;
}

export interface TutorEarningsLine {
  classSessionId: string;
  liveClassId: string;
  classTitle: string;
  scheduledStartAt: string;
  attendedCount: number;
  creditCost: number;
  creditUsdValue: number;
  revenueSharePercent: number;
  grossUsd: number;
  netUsd: number;
}

export interface TutorEarnings {
  from?: string | null;
  to?: string | null;
  grossUsd: number;
  netUsd: number;
  revenueSharePercent: number;
  lines: TutorEarningsLine[];
}

export interface TutorClassCreatePayload {
  title: string;
  titleAr?: string | null;
  description: string;
  descriptionAr?: string | null;
  type: string;
  professionTrack: string;
  level: string;
  scheduledStartAt: string;
  durationMinutes: number;
  capacity: number;
  creditCost: number;
  coverImageUrl?: string | null;
  tags?: string[];
  autoPublish?: boolean;
}

export interface TutorClassUpdatePayload {
  title?: string | null;
  titleAr?: string | null;
  description?: string | null;
  descriptionAr?: string | null;
  coverImageUrl?: string | null;
  creditCost?: number | null;
  defaultCapacity?: number | null;
  defaultDurationMinutes?: number | null;
  tags?: string[] | null;
}

export interface TutorClassSessionCreatePayload {
  scheduledStartAt: string;
  durationMinutes?: number | null;
  capacity?: number | null;
}

export interface TutorClassSessionUpdatePayload {
  scheduledStartAt?: string;
  durationMinutes?: number;
  capacity?: number;
  cancellationReason?: string;
}

export interface TutorAttendanceLine {
  userId: string;
  displayName?: string | null;
  joinedAt: string;
  leftAt?: string | null;
  durationSeconds: number;
}

export interface ClassFeedbackSubmitPayload {
  rating: number;
  comment?: string | null;
  recommendToFriend?: boolean | null;
}

export interface ClassFeedbackEntry {
  id: string;
  classSessionId: string;
  userId: string;
  rating: number;
  comment?: string | null;
  recommendToFriend: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ClassWaitlistEntry {
  id: string;
  classSessionId: string;
  userId: string;
  position: number;
  joinedAt: string;
}

export interface LiveClassTranscript {
  classSessionId: string;
  transcriptText: string;
  processedAt?: string | null;
}

export async function fetchTutorProfile(): Promise<TutorProfile | null> {
  try {
    return await apiRequest<TutorProfile>('/v1/tutor/me');
  } catch (err) {
    if (isApiError(err) && err.status === 404) return null;
    throw err;
  }
}

export async function createTutorProfile(payload: TutorUpsertPayload): Promise<TutorProfile> {
  return apiRequest<TutorProfile>('/v1/tutor/me', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateTutorProfile(payload: TutorUpsertPayload): Promise<TutorProfile> {
  return apiRequest<TutorProfile>('/v1/tutor/me', {
    method: 'PATCH',
    body: JSON.stringify(payload),
  });
}

export async function fetchTutorAvailability(): Promise<TutorAvailabilitySlot[]> {
  return apiRequest<TutorAvailabilitySlot[]>('/v1/tutor/me/availability');
}

export async function replaceTutorAvailability(
  slots: TutorAvailabilityUpsertPayload[],
): Promise<TutorAvailabilitySlot[]> {
  return apiRequest<TutorAvailabilitySlot[]>('/v1/tutor/me/availability', {
    method: 'PUT',
    body: JSON.stringify(slots),
  });
}

export async function fetchTutorEarnings(from?: string, to?: string): Promise<TutorEarnings> {
  const qs = new URLSearchParams();
  if (from) qs.set('from', from);
  if (to) qs.set('to', to);
  const tail = qs.toString();
  return apiRequest<TutorEarnings>(`/v1/tutor/me/earnings${tail ? `?${tail}` : ''}`);
}

export async function provisionTutorZoomUser(): Promise<{ zoomUserId: string | null }> {
  return apiRequest<{ zoomUserId: string | null }>('/v1/tutor/me/zoom-user', {
    method: 'POST',
  });
}

export async function fetchTutorClasses(): Promise<LiveClassListItem[]> {
  return apiRequest<LiveClassListItem[]>('/v1/tutor/me/classes');
}

export async function createTutorClass(payload: TutorClassCreatePayload): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>('/v1/tutor/me/classes', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function updateTutorClass(
  classId: string,
  payload: TutorClassUpdatePayload,
): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(`/v1/tutor/me/classes/${encodeURIComponent(classId)}`, {
    method: 'PATCH',
    body: JSON.stringify(payload),
  });
}

export async function addTutorClassSession(
  classId: string,
  payload: TutorClassSessionCreatePayload,
): Promise<LiveClassSessionSummary> {
  return apiRequest<LiveClassSessionSummary>(
    `/v1/tutor/me/classes/${encodeURIComponent(classId)}/sessions`,
    {
      method: 'POST',
      body: JSON.stringify(payload),
    },
  );
}

export async function updateTutorClassSession(
  sessionId: string,
  payload: TutorClassSessionUpdatePayload,
): Promise<LiveClassDetail> {
  return apiRequest<LiveClassDetail>(
    `/v1/tutor/me/classes/sessions/${encodeURIComponent(sessionId)}`,
    {
      method: 'PATCH',
      body: JSON.stringify(payload),
    },
  );
}

export async function cancelTutorClassSession(sessionId: string): Promise<void> {
  await apiRequest(`/v1/tutor/me/classes/sessions/${encodeURIComponent(sessionId)}`, {
    method: 'DELETE',
  });
}

export async function fetchTutorSessionAttendance(sessionId: string): Promise<TutorAttendanceLine[]> {
  return apiRequest<TutorAttendanceLine[]>(
    `/v1/tutor/me/classes/sessions/${encodeURIComponent(sessionId)}/attendance`,
  );
}

export async function submitClassFeedback(
  sessionId: string,
  payload: ClassFeedbackSubmitPayload,
): Promise<ClassFeedbackEntry> {
  return apiRequest<ClassFeedbackEntry>(
    `/v1/classes/sessions/${encodeURIComponent(sessionId)}/feedback`,
    {
      method: 'POST',
      body: JSON.stringify(payload),
    },
  );
}

export async function joinClassWaitlist(sessionId: string): Promise<ClassWaitlistEntry> {
  return apiRequest<ClassWaitlistEntry>(
    `/v1/classes/sessions/${encodeURIComponent(sessionId)}/waitlist`,
    { method: 'POST' },
  );
}

export async function leaveClassWaitlist(sessionId: string): Promise<void> {
  await apiRequest(`/v1/classes/sessions/${encodeURIComponent(sessionId)}/waitlist`, {
    method: 'DELETE',
  });
}

export async function fetchClassTranscript(sessionId: string): Promise<LiveClassTranscript> {
  return apiRequest<LiveClassTranscript>(
    `/v1/me/classes/sessions/${encodeURIComponent(sessionId)}/transcript`,
  );
}
