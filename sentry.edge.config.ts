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
import { isOwnerAgentSentryEvent } from '@/lib/owner-agent/route-scope';

const dsn = readSentryDsn();

if (dsn) {
  Sentry.init({
    dsn,
    environment: readSentryEnvironment(),
    release: readSentryRelease(),

    sendDefaultPii: false,
    // Owner Agent Console requests (/admin/agent-console, /v1/owner-agent) carry
    // production transcripts and unlock headers: never report them.
    beforeSend: (event, hint) => (isOwnerAgentSentryEvent(event) ? null : scrubPii(event, hint)),
    beforeSendTransaction: (event) => (isOwnerAgentSentryEvent(event) ? null : event),

    tracesSampleRate: readSampleRate('SENTRY_TRACES_SAMPLE_RATE'),
  });
}
