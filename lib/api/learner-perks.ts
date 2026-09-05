/**
 * Learner perks: certificates, referrals, exam booking, tutoring sessions —
 * extracted from `lib/api.ts`. Re-exported there, so `@/lib/api` imports
 * keep working.
 *
 * NOTE: two endpoint generations coexist (`/v1/certificates` + referrals
 * vs `/v1/learner/*`) — both are live surfaces, kept as-is.
 */
import { apiRequest } from './client';

export async function fetchMyCertificates() {
  return apiRequest('/v1/certificates');
}

export async function verifyCertificate(code: string) {
  return apiRequest(`/v1/certificates/verify/${encodeURIComponent(code)}`);
}

export async function fetchMyReferralCode() {
  return apiRequest('/v1/referrals/my-code');
}

export async function fetchMyReferrals() {
  return apiRequest('/v1/referrals/my-referrals');
}

export async function applyReferralCode(code: string) {
  return apiRequest('/v1/referrals/apply', {
    method: 'POST',
    body: JSON.stringify({ code }),
  });
}

export async function fetchExamBookings() {
  return apiRequest('/v1/exam-bookings');
}

export async function createExamBooking(payload: { examTypeCode: string; examDate: string; bookingReference?: string; externalUrl?: string; testCenter?: string }) {
  return apiRequest('/v1/exam-bookings', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function deleteExamBooking(bookingId: string) {
  return apiRequest(`/v1/exam-bookings/${encodeURIComponent(bookingId)}`, { method: 'DELETE' });
}

export async function fetchTutoringSessions() {
  return apiRequest('/v1/tutoring/sessions');
}

export async function bookTutoringSession(payload: { expertUserId: string; examTypeCode: string; subtestFocus?: string; scheduledAt: string; durationMinutes: number; learnerNotes?: string; price: number }) {
  return apiRequest('/v1/tutoring/sessions', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function rateTutoringSession(sessionId: string, rating: number, feedback?: string) {
  return apiRequest(`/v1/tutoring/sessions/${encodeURIComponent(sessionId)}/rate`, {
    method: 'POST',
    body: JSON.stringify({ rating, feedback }),
  });
}

export async function fetchCertificates() {
  return apiRequest('/v1/learner/certificates');
}

export async function fetchReferralInfo() {
  return apiRequest('/v1/learner/referral');
}

export async function generateReferralCode() {
  return apiRequest('/v1/learner/referral/generate', {
    method: 'POST',
  });
}
