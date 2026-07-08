'use client';

import { useRouter } from 'next/navigation';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';

/**
 * Backend error codes that mean "you don't have enough credits/allowance to
 * start this" (HTTP 402 from AiPackageCreditService / MockEntitlementService
 * debit calls). Distinct from `content_locked` (subscription/plan-tier gate,
 * handled by ContentLockedNotice) — these are numeric balance shortfalls.
 */
const INSUFFICIENT_CREDIT_CODES = new Set([
  'no_reading_tests',
  'no_listening_tests',
  'no_ai_package_credits',
  'ai_credits_insufficient',
  'ai_package_expired',
  'no_mock_exams',
  'no_credits',
  'insufficient_review_credits',
  'speaking_exam_insufficient_credits',
]);

export function isInsufficientCreditsError(
  err: unknown,
): err is { message: string; status?: number; code?: string } {
  if (typeof err !== 'object' || err === null) return false;
  const e = err as { code?: unknown };
  return typeof e.code === 'string' && INSUFFICIENT_CREDIT_CODES.has(e.code);
}

export function readInsufficientCreditsMessage(
  err: unknown,
  fallback = 'You do not have enough credits to start this. Purchase a package to continue.',
): string {
  if (typeof err !== 'object' || err === null) return fallback;
  const e = err as { message?: string };
  return e.message ?? fallback;
}

/** Where the "buy more" CTA should send the learner, based on the error code. */
export function creditPurchaseHrefForError(err: unknown): string {
  if (typeof err === 'object' && err !== null) {
    const code = (err as { code?: unknown }).code;
    if (code === 'no_mock_exams' || code === 'insufficient_review_credits') {
      return '/mocks';
    }
  }
  return '/ai-packages';
}

/**
 * Shared blocking modal shown when a learner tries to start an exam,
 * practice attempt, or mock without enough credits/allowance. Every module
 * (Reading/Writing/Listening/Speaking/Mocks) should render this on a 402
 * with an insufficient-credits code, BEFORE any attempt is created or exam
 * content is shown.
 */
export function InsufficientCreditsModal({
  open,
  message,
  onClose,
  ctaHref = '/ai-packages',
  ctaLabel = 'Buy Credits',
}: {
  open: boolean;
  message: string;
  onClose: () => void;
  ctaHref?: string;
  ctaLabel?: string;
}) {
  const router = useRouter();
  return (
    <Modal open={open} onClose={onClose} title="Not enough credits">
      <div className="space-y-4">
        <p className="text-sm leading-6 text-muted">{message}</p>
        <div className="flex flex-wrap justify-end gap-2">
          <Button variant="outline" onClick={onClose}>
            Not now
          </Button>
          <Button onClick={() => router.push(ctaHref)}>{ctaLabel}</Button>
        </div>
      </div>
    </Modal>
  );
}
