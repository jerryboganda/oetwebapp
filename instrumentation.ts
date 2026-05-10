// Next.js instrumentation entry point. Called once per runtime at boot.
// Delegates to sentry.server.config.ts / sentry.edge.config.ts so the two
// runtimes stay isolated (no Node APIs at the edge).
//
// When NEXT_PUBLIC_SENTRY_DSN / SENTRY_DSN are unset, Sentry is not imported.
// This keeps local dev, CI, and DSN-less previews noise-free.

import { readSentryDsn } from '@/lib/observability/sentry-shared';

export async function register() {
  if (!readSentryDsn()) {
    return;
  }

  if (process.env.NEXT_RUNTIME === 'nodejs') {
    await import('./sentry.server.config');
  } else if (process.env.NEXT_RUNTIME === 'edge') {
    await import('./sentry.edge.config');
  }

  // Billing-feature tagging. Adds `feature: 'billing'` to any Sentry event
  // whose request URL or transaction name matches a billing-related route
  // (learner billing, admin billing, wallet, payment webhooks). This is the
  // canonical filter for billing dashboards and alerts; see docs/BILLING.md
  // section 9 (Observability) and docs/runbooks/billing-incident.md.
  //
  // Guarded behind feature detection: if Sentry is absent or `addEventProcessor`
  // is not exposed by the installed SDK version, the augmentation is silently
  // skipped so this file remains safe in DSN-less / minimal-SDK environments.
  try {
    const Sentry: unknown = await import('@sentry/nextjs');
    const addEventProcessor = (
      Sentry as {
        addEventProcessor?: (
          processor: (event: Record<string, unknown>) => Record<string, unknown> | null,
        ) => void;
      }
    ).addEventProcessor;

    if (typeof addEventProcessor === 'function') {
      const billingPattern =
        /\/(?:admin\/)?billing(?:\/|$)|\/wallet(?:\/|$)|\/payments?(?:\/|$)|\/webhooks?\/(?:stripe|paypal)/i;

      addEventProcessor((event) => {
        const request = event.request as { url?: unknown } | undefined;
        const url =
          (request && typeof request.url === 'string' && request.url) ||
          (typeof event.transaction === 'string' && event.transaction) ||
          '';

        if (url && billingPattern.test(url)) {
          const existingTags = (event.tags as Record<string, string> | undefined) ?? {};
          if (!existingTags.feature) {
            event.tags = { ...existingTags, feature: 'billing' };
          }
        }
        return event;
      });
    }
  } catch {
    // Sentry not installed, DSN absent, or SDK API surface differs - billing
    // tagging is best-effort and must never break boot.
  }
}
