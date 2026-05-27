/**
 * Writing Module V2 — Buddy System (spec §23.5).
 *
 * Anonymous peer-to-peer pairing at ±1 band, same profession. Seven
 * endpoints under `/v1/writing/buddy`:
 *
 *   POST   /v1/writing/buddy/opt-in
 *   POST   /v1/writing/buddy/match
 *   GET    /v1/writing/buddy/pair
 *   POST   /v1/writing/buddy/pair/{pairId}/messages
 *   GET    /v1/writing/buddy/pair/{pairId}/messages?take=50
 *   POST   /v1/writing/buddy/pair/{pairId}/check-in
 *   POST   /v1/writing/buddy/pair/{pairId}/end
 */

import { apiClient } from '../api';

// ── DTOs (mirrors backend Writing.Services WritingBuddyService records) ─────

export interface WritingBuddyPairDto {
  id: string;
  profession: string;
  matchedAtBand: string;
  status: 'active' | 'paused' | 'ended';
  createdAt: string;
  endedAt: string | null;
  endedReason: string | null;
  partnerDisplayName: string;
  isUserA: boolean;
}

export interface WritingBuddyMessageDto {
  id: string;
  pairId: string;
  fromUserId: string;
  mineMessage: boolean;
  bodyMarkdown: string;
  sentAt: string;
  readAt: string | null;
}

export interface WritingBuddyCheckInDto {
  id: string;
  pairId: string;
  weekStartDate: string;
  myReportJson: string | null;
  partnerReportJson: string | null;
  completedAt: string | null;
}

export interface WritingBuddyOptInResultDto {
  optedIn: boolean;
  activePairId: string | null;
}

export interface WritingBuddyMatchResultDto {
  status: 'matched' | 'queued';
  pairId: string | null;
  partnerDisplayName: string | null;
}

// ── API ─────────────────────────────────────────────────────────────────────

export const optInWritingBuddy = () =>
  apiClient.post<WritingBuddyOptInResultDto>('/v1/writing/buddy/opt-in', {});

export const requestWritingBuddyMatch = () =>
  apiClient.post<WritingBuddyMatchResultDto>('/v1/writing/buddy/match', {});

/**
 * Active pair for the current learner, or null when none.
 * Backend returns 204 No Content when unpaired — already mapped to
 * `undefined` by apiRequest, which we surface as null.
 */
export const getWritingBuddyPair = async (): Promise<WritingBuddyPairDto | null> => {
  const r = await apiClient.get<WritingBuddyPairDto | undefined>('/v1/writing/buddy/pair');
  return r ?? null;
};

export const sendWritingBuddyMessage = (pairId: string, body: string) =>
  apiClient.post<WritingBuddyMessageDto>(
    `/v1/writing/buddy/pair/${encodeURIComponent(pairId)}/messages`,
    { body },
  );

export const listWritingBuddyMessages = (pairId: string, take = 50) =>
  apiClient.get<WritingBuddyMessageDto[]>(
    `/v1/writing/buddy/pair/${encodeURIComponent(pairId)}/messages?take=${take}`,
  );

export const submitWritingBuddyCheckIn = (pairId: string, report: object | string) =>
  apiClient.post<WritingBuddyCheckInDto>(
    `/v1/writing/buddy/pair/${encodeURIComponent(pairId)}/check-in`,
    { report: typeof report === 'string' ? report : JSON.stringify(report) },
  );

export const endWritingBuddyPair = (pairId: string, reason: string) =>
  apiClient.post<{ ended: boolean }>(
    `/v1/writing/buddy/pair/${encodeURIComponent(pairId)}/end`,
    { reason },
  );
