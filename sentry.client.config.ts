// Browser-side Sentry init. Auto-loaded by @sentry/nextjs when present at the
// repo root. Env-gated on NEXT_PUBLIC_SENTRY_DSN so local dev, tests, and
// preview envs without a DSN never ship events.

import * as Sentry from '@sentry/nextjs';
import {
  readSampleRate,
  readSentryDsn,
  readSentryEnvironment,
  readSentryRelease,
  scrubPii,
} from '@/lib/observability/sentry-shared';

const dsn = readSentryDsn();

if (dsn) {
  Sentry.init({
    dsn,
    environment: readSentryEnvironment(),
    release: readSentryRelease(),

    // Privacy pins - hard-coded so they cannot be flipped by config.
    sendDefaultPii: false,
    beforeSend: scrubPii,

    // Performance / profiling default to 0 so enabling the SDK never silently
    // turns on performance data.
    tracesSampleRate: readSampleRate('NEXT_PUBLIC_SENTRY_TRACES_SAMPLE_RATE'),
    replaysSessionSampleRate: readSampleRate(
      'NEXT_PUBLIC_SENTRY_REPLAYS_SESSION_SAMPLE_RATE',
    ),
    replaysOnErrorSampleRate: readSampleRate(
      'NEXT_PUBLIC_SENTRY_REPLAYS_ON_ERROR_SAMPLE_RATE',
    ),

    // Opt into Replay only when the env explicitly asks for it. The mask-all
    // defaults are preserved so learner PII in the DOM is never captured.
    integrations:
      readSampleRate('NEXT_PUBLIC_SENTRY_REPLAYS_SESSION_SAMPLE_RATE') > 0 ||
      readSampleRate('NEXT_PUBLIC_SENTRY_REPLAYS_ON_ERROR_SAMPLE_RATE') > 0
        ? [
            Sentry.replayIntegration({
              maskAllText: true,
              maskAllInputs: true,
              blockAllMedia: true,
            }),
          ]
        : [],
  });
}
