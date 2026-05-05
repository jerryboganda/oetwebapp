import Link from 'next/link';
import { Lock } from 'lucide-react';
import { Button } from '@/components/ui/button';

/**
 * Detects whether a thrown attempt-start error is the backend's
 * `content_locked` (HTTP 402) response from `ContentEntitlementService`.
 *
 * Both `lib/api.ts` (typed `ApiError`) and per-module clients like
 * `lib/listening-api.ts` / `lib/reading-attempt-api.ts` expose the parsed
 * payload; this helper looks at every shape we currently produce so callers
 * don't need to know which client threw.
 */
export function isContentLockedError(err: unknown): err is { message: string; status?: number; code?: string; detail?: { code?: string; message?: string } } {
  if (typeof err !== 'object' || err === null) return false;
  const e = err as { code?: unknown; status?: unknown; detail?: { code?: unknown } };
  if (e.code === 'content_locked') return true;
  if (e.status === 402) return true;
  if (typeof e.detail === 'object' && e.detail && (e.detail as { code?: unknown }).code === 'content_locked') return true;
  return false;
}

export function readContentLockedMessage(err: unknown, fallback = 'This paper requires an active subscription.'): string {
  if (typeof err !== 'object' || err === null) return fallback;
  const e = err as { message?: string; detail?: { message?: string } };
  return e.detail?.message ?? e.message ?? fallback;
}

/**
 * Shared inline upsell card shown when a learner hits HTTP 402 from the
 * subscription gate. Renders the backend message verbatim (it is the
 * authoritative explanation) and a CTA to /billing.
 */
export function ContentLockedNotice({
  message,
  previewHint,
  className,
}: {
  message: string;
  /**
   * Optional small tease line shown beneath the CTA — e.g. for the
   * "first-extract-free" preview hook on premium-locked papers.
   * When omitted, the notice renders unchanged from its previous form.
   */
  previewHint?: string;
  className?: string;
}) {
  return (
    <div
      role="alert"
      className={
        'mx-auto flex max-w-md flex-col items-center gap-3 rounded-xl border border-amber-300 bg-amber-50 p-5 text-center text-sm text-amber-900 ' +
        (className ?? '')
      }
    >
      <Lock className="h-8 w-8 text-amber-600" aria-hidden />
      <h3 className="text-base font-semibold">Subscription required</h3>
      <p className="text-amber-900/90">{message}</p>
      <Link href="/billing" prefetch={false}>
        <Button variant="primary">View plans</Button>
      </Link>
      {previewHint ? (
        <p className="mt-3 text-xs text-amber-800/80">{previewHint}</p>
      ) : null}
    </div>
  );
}
