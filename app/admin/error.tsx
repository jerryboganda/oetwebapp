'use client';

/**
 * Admin route-level error boundary.
 * Per Next.js App Router convention this file is the runtime fallback
 * when any descendant of `/admin/*` throws during render. Spec reference:
 * docs/admin-redesign/axelit-study/19-ERROR-EMPTY-STATES.md §2.14.
 *
 * Behaviour:
 *   1. Logs the error to the console (always) and to Sentry (if available).
 *   2. Renders a full-page EmptyState in the admin design system with a
 *      Try Again action wired to Next.js's `reset()` callback and a
 *      secondary link back to the dashboard root.
 *   3. Surfaces `error.digest` as the support reference so the user can
 *      quote it when they file an incident ticket.
 */

import { useEffect } from 'react';
import { AlertTriangle } from 'lucide-react';

import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { EmptyState } from '@/components/admin/ui/empty-state';

type AdminErrorProps = {
  error: Error & { digest?: string };
  reset: () => void;
};

export default function AdminError({ error, reset }: AdminErrorProps) {
  useEffect(() => {
    // Always log to the console — keeps local dev debuggable.
    console.error('[Admin Error]', error);

    // Best-effort Sentry capture. We dynamic-import so this file does not
    // pull the SDK into the route bundle when telemetry is disabled, and so
    // the page still renders cleanly in environments without the package.
    if (typeof window !== 'undefined') {
      void import('@sentry/nextjs')
        .then((Sentry) => {
          Sentry.captureException(error, {
            tags: { surface: 'admin', boundary: 'route-error' },
          });
        })
        .catch(() => {
          // Sentry not installed or DSN not configured — silent.
        });
    }
  }, [error]);

  return (
    <AdminPageShell hideSkipLink>
      <div className="flex min-h-[60vh] items-center justify-center">
        <EmptyState
          variant="error"
          size="lg"
          headingLevel="h1"
          illustration={<AlertTriangle aria-hidden="true" />}
          title="Something went wrong in the admin"
          description="We've logged the error. Try again, or return to the dashboard while we investigate."
          primaryAction={{
            label: 'Try again',
            onClick: reset,
          }}
          secondaryAction={{
            label: 'Back to dashboard',
            href: '/admin',
          }}
          errorRef={error.digest}
        />
      </div>
    </AdminPageShell>
  );
}
