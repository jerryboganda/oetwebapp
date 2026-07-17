# Observability (`lib/observability/`)

Shared observability helpers for the OET Prep Platform.

## Tools

- **Sentry** — error and performance monitoring (configured in root `sentry.*.config.ts`).
- **OpenTelemetry API** — available for custom tracing and future AI-agent instrumentation.

## Usage

```ts
import { getTracer, withSpan } from "@/lib/observability";

await withSpan("my-operation", async () => {
  // traced work
});
```

## Do not

- Do not send secrets, tokens, PII, or private paths in spans or logs.
- Do not initialize a second global tracer provider; Sentry manages the runtime SDK.
