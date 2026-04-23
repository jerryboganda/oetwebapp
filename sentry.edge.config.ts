// Edge-runtime Sentry init. Loaded from instrumentation.ts when Next.js runs
// middleware or edge route handlers. Keeps the integration list minimal
// because many Node APIs are unavailable at the edge.

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

    sendDefaultPii: false,
    beforeSend: scrubPii,

    tracesSampleRate: readSampleRate('SENTRY_TRACES_SAMPLE_RATE'),
  });
}
