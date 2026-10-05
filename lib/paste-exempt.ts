'use client';

import { useContext } from 'react';
import { AuthContext } from '@/contexts/auth-context';

/**
 * Accounts exempt from the learner copy/paste lock (owner directive 2026-10-05):
 * used to test Writing with pasted letters. Exact email match, nothing else.
 */
const PASTE_EXEMPT_EMAILS: readonly string[] = ['drahmedheshamuk2025@gmail.com'];

export function isPasteExempt(email?: string | null): boolean {
  if (!email) return false;
  return PASTE_EXEMPT_EMAILS.includes(email.trim().toLowerCase());
}

export function usePasteExempt(): boolean {
  // Raw context (not useAuth) so surfaces rendered outside AuthProvider don't throw.
  return isPasteExempt(useContext(AuthContext)?.user?.email);
}
