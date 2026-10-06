'use client';

import { useContext } from 'react';
import { AuthContext } from '@/contexts/auth-context';

/**
 * Accounts exempt from the learner copy/paste lock (owner directive 2026-10-06):
 * the five owner/testing accounts. The server flag `writingUnrestricted` is
 * authoritative; this exact-match list (no dot or plus folding) is only the
 * fallback for sessions stored before that field existed. Keep it identical to
 * the backend WritingUnrestrictedAccounts list.
 */
const PASTE_EXEMPT_EMAILS: readonly string[] = [
  'drahmedheshamuk2025@gmail.com',
  'drahmedhesham9595@gmail.com',
  'ahmedibrahimabdrabuibrahim@gmail.com',
  'drahmedhesham.work@gmail.com',
  'tutorcommerceacademy2026@gmail.com',
];

export function isPasteExempt(
  user?: { email?: string | null; writingUnrestricted?: boolean | null } | null,
): boolean {
  if (user?.writingUnrestricted === true) return true;
  const email = user?.email;
  if (!email) return false;
  return PASTE_EXEMPT_EMAILS.includes(email.trim().toLowerCase());
}

export function usePasteExempt(): boolean {
  // Raw context (not useAuth) so surfaces rendered outside AuthProvider don't throw.
  return isPasteExempt(useContext(AuthContext)?.user);
}
