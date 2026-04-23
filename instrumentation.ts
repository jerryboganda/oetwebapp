// Next.js instrumentation entry point. Called once per runtime at boot.
// Delegates to sentry.server.config.ts / sentry.edge.config.ts so the two
// runtimes stay isolated (no Node APIs at the edge).
//
// When NEXT_PUBLIC_SENTRY_DSN / SENTRY_DSN are unset the config files are
// still imported but do nothing - Sentry never initialises. This keeps
// local dev, CI, and DSN-less previews noise-free.

export async function register() {
  if (process.env.NEXT_RUNTIME === 'nodejs') {
    await import('./sentry.server.config');
  } else if (process.env.NEXT_RUNTIME === 'edge') {
    await import('./sentry.edge.config');
  }
}
