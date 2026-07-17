import { trace, Tracer } from "@opentelemetry/api";

/**
 * Observability helpers for the OET Prep Platform.
 *
 * Sentry is the primary error/performance tool (configured in sentry.*.config.ts).
 * OpenTelemetry is available for future custom tracing and AI-agent instrumentation.
 */

const TRACER_NAME = "oet-prep";

export function getTracer(): Tracer {
  return trace.getTracer(TRACER_NAME);
}

export function withSpan<T>(name: string, fn: () => T): T {
  const tracer = getTracer();
  return tracer.startActiveSpan(name, (span) => {
    try {
      const result = fn();
      span.setStatus({ code: 1 });
      return result;
    } catch (error) {
      span.recordException(error instanceof Error ? error : new Error(String(error)));
      span.setStatus({ code: 2 });
      throw error;
    } finally {
      span.end();
    }
  });
}
