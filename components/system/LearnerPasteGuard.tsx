'use client';

import { useEffect } from 'react';
import { usePathname } from 'next/navigation';

/**
 * App-wide clipboard guard for the learner experience.
 *
 * Product requirement: copy, paste, cut, and text drag-drop must be disabled
 * across the ENTIRE learner-facing app (not just the exam surfaces) so learners
 * cannot lift prompts/answers in or out of any page. This mirrors the
 * capture-phase `preventDefault()` technique already used by the writing editor
 * (`components/domain/writing/WritingEditorV2.tsx`), but applied globally at the
 * document level instead of per-input.
 *
 * Two route families are intentionally EXCLUDED so they keep native paste:
 *   - `/admin/**` — admins paste exam content into data-entry forms. Blocking
 *     paste there would make content authoring unworkable.
 *   - auth / OTP routes (`/sign-in`, `/register`, `/forgot-password`,
 *     `/reset-password`, `/mfa`, `/auth`, `/privacy`) — the OTP input manages
 *     its own paste (see `components/auth/otp-code-input.tsx`) and learners
 *     legitimately paste codes/passwords during sign-in.
 *
 * Accepted UX consequence: on guarded (learner) routes, EVERY input loses
 * paste — not only the exam editors. This is the deliberate product trade-off;
 * the value of a uniform, un-bypassable block outweighs the minor friction of
 * not being able to paste into incidental fields (e.g. community posts).
 *
 * On excluded routes the component renders `null` and attaches NO listeners,
 * so paste behaves natively there. It re-evaluates whenever the pathname
 * changes (client-side navigation), so crossing into/out of `/admin` flips the
 * guard on/off without a full reload.
 */

/** URL path prefixes that must keep native copy/paste/cut behaviour. */
const EXCLUDED_PREFIXES = [
  '/admin',
  // Auth + OTP routes (these live under the `(auth)` route group, which is
  // stripped from the URL, so they appear at the top level).
  '/sign-in',
  '/register',
  '/forgot-password',
  '/reset-password',
  '/mfa',
  '/auth',
  '/privacy',
] as const;

function isExcludedPath(pathname: string | null): boolean {
  if (!pathname) return false;
  return EXCLUDED_PREFIXES.some(
    (prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`),
  );
}

/** Clipboard / drag events we block on guarded learner routes. */
const GUARDED_EVENTS = ['paste', 'copy', 'cut', 'dragstart', 'drop'] as const;

export function LearnerPasteGuard(): null {
  const pathname = usePathname();
  const excluded = isExcludedPath(pathname);

  useEffect(() => {
    // On admin/auth routes we attach nothing so native paste keeps working.
    if (excluded) return;

    const prevent = (event: Event) => {
      event.preventDefault();
    };

    // Capture phase so we stop the event before any input/editor (e.g.
    // ProseMirror, textarea) handles it.
    const options: AddEventListenerOptions = { capture: true };
    for (const type of GUARDED_EVENTS) {
      document.addEventListener(type, prevent, options);
    }

    return () => {
      for (const type of GUARDED_EVENTS) {
        document.removeEventListener(type, prevent, options);
      }
    };
  }, [excluded]);

  return null;
}

export default LearnerPasteGuard;
