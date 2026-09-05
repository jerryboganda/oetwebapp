/**
 * Private speaking sessions (learner booking, expert availability,
 * admin management) — extracted from `lib/api.ts`. Re-exported there,
 * so `@/lib/api` imports keep working.
 */
import { ApiError, apiRequest, getHeaders, resolveApiUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

export interface LiveClassJoinToken {
  provider: 'zoom';
  sdkKey?: string | null;
  signature?: string | null;
  meetingNumber: string;
  userName: string;
  userEmail?: string | null;
  role: number;
  passWord?: string | null;
  zak?: string | null;
  joinUrl?: string | null;
  expiresAt: string;
}

export interface PrivateSpeakingBookingResult {
  bookingId: string;
  checkoutSessionId?: string | null;
  checkoutUrl?: string | null;
  entitlementUsed: boolean;
  speakingSessionsRemaining?: number | null;
}

export interface PrivateSpeakingCalendarStatus {
  connected: boolean;
  provider?: string | null;
  calendarId?: string | null;
  connectedEmail?: string | null;
  connectedAt?: string | null;
  lastCheckedAt?: string | null;
  lastSyncedAt?: string | null;
  lastError?: string | null;
}

export interface PrivateSpeakingCalendarConnectResult {
  authorizationUrl: string;
  expiresAt: string;
}

export async function fetchPrivateSpeakingConfig() {
  return apiRequest('/v1/private-speaking/config');
}

export async function fetchPrivateSpeakingTutors() {
  return apiRequest('/v1/private-speaking/tutors');
}

export async function fetchPrivateSpeakingSlots(tutorProfileId: string, from: string, to: string) {
  return apiRequest(`/v1/private-speaking/tutors/${encodeURIComponent(tutorProfileId)}/slots?from=${from}&to=${to}`);
}

export async function fetchAllPrivateSpeakingSlots(from: string, to: string) {
  return apiRequest(`/v1/private-speaking/slots?from=${from}&to=${to}`);
}

export async function createPrivateSpeakingBooking(payload: {
  tutorProfileId: string;
  sessionStartUtc: string;
  durationMinutes: number;
  learnerTimezone: string;
  learnerNotes?: string;
  /** Candidate profession track (Medicine, Nursing, Pharmacy, Dentistry, Other). */
  professionTrack?: string | null;
  idempotencyKey: string;
  /** Speaking module rebuild (2026-06-11): "practice" (default) or "exam". */
  sessionFormat?: string | null;
  /** "paypal" pays the catalog price via embedded PayPal; omit/"entitlement" uses a credit. */
  paymentMethod?: 'paypal' | 'entitlement' | null;
}): Promise<PrivateSpeakingBookingResult> {
  return apiRequest<PrivateSpeakingBookingResult>('/v1/private-speaking/bookings', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function reschedulePrivateSpeakingBooking(bookingId: string, payload: {
  sessionStartUtc: string;
  learnerTimezone: string;
  learnerNotes?: string;
  idempotencyKey: string;
}): Promise<PrivateSpeakingBookingResult> {
  return apiRequest<PrivateSpeakingBookingResult>(`/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}/reschedule`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function fetchLearnerPrivateSpeakingBookings(status?: string) {
  const qs = status ? `?status=${status}` : '';
  return apiRequest(`/v1/private-speaking/bookings${qs}`);
}

export async function fetchPrivateSpeakingBookingDetail(bookingId: string) {
  return apiRequest(`/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}`);
}

export async function cancelPrivateSpeakingBooking(bookingId: string, reason?: string) {
  return apiRequest(`/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function fetchPrivateSpeakingJoinToken(bookingId: string): Promise<LiveClassJoinToken> {
  return apiRequest<LiveClassJoinToken>(`/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}/join-token`, {
    method: 'POST',
  });
}

export async function downloadPrivateSpeakingCalendarInvite(bookingId: string): Promise<Blob> {
  const path = `/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}/calendar.ics`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    headers: await getHeaders(path, undefined, { json: false }),
  });
  if (!response.ok) {
    throw new ApiError(response.status, 'calendar_invite_download_failed', 'Could not download the calendar invite.', false);
  }
  return response.blob();
}

export async function ratePrivateSpeakingSession(bookingId: string, rating: number, feedback?: string) {
  return apiRequest(`/v1/private-speaking/bookings/${encodeURIComponent(bookingId)}/rate`, {
    method: 'POST',
    body: JSON.stringify({ rating, feedback }),
  });
}

export async function fetchExpertPrivateSpeakingProfile() {
  return apiRequest('/v1/expert/private-speaking/profile');
}

export async function fetchExpertPrivateSpeakingSessions(status?: string) {
  const qs = status ? `?status=${status}` : '';
  return apiRequest(`/v1/expert/private-speaking/sessions${qs}`);
}

export async function fetchExpertPrivateSpeakingSessionDetail(bookingId: string) {
  return apiRequest(`/v1/expert/private-speaking/sessions/${encodeURIComponent(bookingId)}`);
}

export async function fetchExpertPrivateSpeakingAvailability() {
  return apiRequest('/v1/expert/private-speaking/availability');
}

export async function updateExpertPrivateSpeakingAvailability(payload: { dayOfWeek: number; startTime: string; endTime: string; effectiveFrom?: string; effectiveTo?: string }) {
  return apiRequest('/v1/expert/private-speaking/availability', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateExpertPrivateSpeakingAvailabilityRule(ruleId: string, payload: { dayOfWeek: number; startTime: string; endTime: string; effectiveFrom?: string | null; effectiveTo?: string | null; isActive: boolean }) {
  return apiRequest(`/v1/expert/private-speaking/availability/${encodeURIComponent(ruleId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function deleteExpertPrivateSpeakingAvailability(ruleId: string) {
  return apiRequest(`/v1/expert/private-speaking/availability/${encodeURIComponent(ruleId)}`, { method: 'DELETE' });
}

export async function cancelExpertPrivateSpeakingSession(bookingId: string, reason?: string) {
  return apiRequest(`/v1/expert/private-speaking/sessions/${encodeURIComponent(bookingId)}/cancel`, {
    method: 'POST',
    body: JSON.stringify({ reason: reason || null }),
  });
}

export async function markExpertPrivateSpeakingNoShow(bookingId: string): Promise<{ noShow: boolean }> {
  return apiRequest<{ noShow: boolean }>(`/v1/expert/private-speaking/sessions/${encodeURIComponent(bookingId)}/mark-no-show`, {
    method: 'POST',
  });
}

export async function fetchExpertPrivateSpeakingJoinToken(bookingId: string): Promise<LiveClassJoinToken> {
  return apiRequest<LiveClassJoinToken>(`/v1/expert/private-speaking/sessions/${encodeURIComponent(bookingId)}/join-token`, {
    method: 'POST',
  });
}

export async function downloadExpertPrivateSpeakingCalendarInvite(bookingId: string): Promise<Blob> {
  const path = `/v1/expert/private-speaking/sessions/${encodeURIComponent(bookingId)}/calendar.ics`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    headers: await getHeaders(path, undefined, { json: false }),
  });
  if (!response.ok) {
    throw new ApiError(response.status, 'calendar_invite_download_failed', 'Could not download the calendar invite.', false);
  }
  return response.blob();
}

export async function fetchExpertPrivateSpeakingCalendarStatus(): Promise<PrivateSpeakingCalendarStatus> {
  return apiRequest<PrivateSpeakingCalendarStatus>('/v1/expert/private-speaking/calendar/status');
}

export async function connectExpertPrivateSpeakingGoogleCalendar(): Promise<PrivateSpeakingCalendarConnectResult> {
  return apiRequest<PrivateSpeakingCalendarConnectResult>('/v1/expert/private-speaking/calendar/google/connect', {
    method: 'POST',
  });
}

export async function disconnectExpertPrivateSpeakingCalendar(): Promise<{ disconnected: boolean }> {
  return apiRequest<{ disconnected: boolean }>('/v1/expert/private-speaking/calendar', {
    method: 'DELETE',
  });
}

export async function fetchAdminPrivateSpeakingConfig() {
  return apiRequest('/v1/admin/private-speaking/config');
}

export async function updateAdminPrivateSpeakingConfig(payload: Record<string, unknown>) {
  return apiRequest('/v1/admin/private-speaking/config', { method: 'PUT', body: JSON.stringify(payload) });
}

export async function fetchAdminPrivateSpeakingStats() {
  return apiRequest('/v1/admin/private-speaking/stats');
}

export async function fetchAdminPrivateSpeakingTutors(activeOnly?: boolean) {
  const qs = activeOnly !== undefined ? `?activeOnly=${activeOnly}` : '';
  return apiRequest(`/v1/admin/private-speaking/tutors${qs}`);
}

export async function fetchAdminPrivateSpeakingTutor(profileId: string) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}`);
}

export async function createAdminPrivateSpeakingTutor(payload: {
  expertUserId: string; displayName: string; timezone: string; bio?: string;
  priceOverrideMinorUnits?: number; slotDurationOverrideMinutes?: number; specialtiesJson?: string;
}) {
  return apiRequest('/v1/admin/private-speaking/tutors', { method: 'POST', body: JSON.stringify(payload) });
}

export async function updateAdminPrivateSpeakingTutor(profileId: string, payload: Record<string, unknown>) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export async function fetchAdminPrivateSpeakingAvailability(profileId: string) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}/availability`);
}

export async function createAdminPrivateSpeakingAvailabilityRule(profileId: string, payload: {
  dayOfWeek: number; startTime: string; endTime: string; effectiveFrom?: string; effectiveTo?: string;
}) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}/availability`, {
    method: 'POST', body: JSON.stringify(payload),
  });
}

export async function deleteAdminPrivateSpeakingAvailabilityRule(profileId: string, ruleId: string) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}/availability/${encodeURIComponent(ruleId)}`, {
    method: 'DELETE',
  });
}

export async function fetchAdminPrivateSpeakingBookings(params?: {
  tutorProfileId?: string; status?: string; learnerId?: string;
  from?: string; to?: string; page?: number; pageSize?: number;
}) {
  const qs = new URLSearchParams();
  if (params?.tutorProfileId) qs.set('tutorProfileId', params.tutorProfileId);
  if (params?.status) qs.set('status', params.status);
  if (params?.learnerId) qs.set('learnerId', params.learnerId);
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/private-speaking/bookings?${qs}`);
}

export async function cancelAdminPrivateSpeakingBooking(bookingId: string, reason?: string) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/cancel`, {
    method: 'POST', body: JSON.stringify({ reason }),
  });
}

export async function completeAdminPrivateSpeakingBooking(bookingId: string) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/complete`, { method: 'POST' });
}

export async function retryAdminPrivateSpeakingZoom(bookingId: string) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/retry-zoom`, { method: 'POST' });
}

export async function adminOverridePrivateSpeakingRefund(
  bookingId: string,
  payload: { amountMinorUnits?: number | null; reason?: string | null },
) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/override-refund`, {
    method: 'POST', body: JSON.stringify(payload),
  });
}

export async function adminManualReschedulePrivateSpeaking(
  bookingId: string,
  payload: { newSessionStartUtc: string; reason?: string | null },
) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/manual-reschedule`, {
    method: 'POST', body: JSON.stringify(payload),
  });
}

export async function adminEditPrivateSpeakingBooking(
  bookingId: string,
  payload: { sessionStartUtc?: string | null; durationMinutes?: number | null; professionTrack?: string | null; tutorNotes?: string | null },
) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}`, {
    method: 'PUT', body: JSON.stringify(payload),
  });
}

export async function adminMarkPrivateSpeakingNoShow(bookingId: string) {
  return apiRequest(`/v1/admin/private-speaking/bookings/${encodeURIComponent(bookingId)}/mark-no-show`, { method: 'POST' });
}

export async function adminUpdatePrivateSpeakingAvailabilityRule(
  profileId: string,
  ruleId: string,
  payload: { dayOfWeek: number; startTime: string; endTime: string; effectiveFrom?: string | null; effectiveTo?: string | null; isActive: boolean },
) {
  return apiRequest(`/v1/admin/private-speaking/tutors/${encodeURIComponent(profileId)}/availability/${encodeURIComponent(ruleId)}`, {
    method: 'PUT', body: JSON.stringify(payload),
  });
}

export async function downloadAdminPrivateSpeakingBookingsCsv(params?: {
  tutorProfileId?: string; status?: string; learnerId?: string; from?: string; to?: string;
}): Promise<Blob> {
  const qs = new URLSearchParams();
  if (params?.tutorProfileId) qs.set('tutorProfileId', params.tutorProfileId);
  if (params?.status) qs.set('status', params.status);
  if (params?.learnerId) qs.set('learnerId', params.learnerId);
  if (params?.from) qs.set('from', params.from);
  if (params?.to) qs.set('to', params.to);
  const query = qs.toString();
  const path = `/v1/admin/private-speaking/bookings/export${query ? `?${query}` : ''}`;
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    headers: await getHeaders(path, undefined, { json: false }),
  });
  if (!response.ok) {
    throw new ApiError(response.status, 'bookings_export_failed', 'Could not export bookings.', false);
  }
  return response.blob();
}

export async function fetchAdminPrivateSpeakingAuditLogs(params?: { bookingId?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.bookingId) qs.set('bookingId', params.bookingId);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 50));
  return apiRequest(`/v1/admin/private-speaking/audit-logs?${qs}`);
}
