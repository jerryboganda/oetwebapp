// Server-side Sentry init. Loaded from instrumentation.ts at boot.

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

    // Privacy pins - identical to the backend SentryBootstrap.
    sendDefaultPii: false,
    beforeSend: scrubPii,

    tracesSampleRate: readSampleRate('SENTRY_TRACES_SAMPLE_RATE'),
  });
}
