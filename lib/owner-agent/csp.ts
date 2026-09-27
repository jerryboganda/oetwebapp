/**
 * Strict Content-Security-Policy for /admin/agent-console/** (plan Phase 4):
 *   script-src 'self' 'nonce-…'; img-src 'self' data:; connect-src 'self';
 *   frame-src 'none'; frame-ancestors 'none'
 *
 * Same nonce model as the app-wide policy in proxy.ts ('self' for our own
 * chunks + a per-request nonce for inline scripts, no 'strict-dynamic' — see
 * the note there about Turbopack chunk loaders). Everything third-party
 * (payments, Zoom, reCAPTCHA, CDNs, Sentry ingest) is dropped: the console
 * only ever talks to the same-origin /api/backend proxy.
 *
 * Runtime-neutral: imported by proxy.ts.
 */
export function buildOwnerAgentConsoleCsp(nonce: string, isDev: boolean): string {
  const scriptSrc = ["'self'", `'nonce-${nonce}'`, ...(isDev ? ["'unsafe-eval'"] : [])].join(' ');
  const directives = [
    "default-src 'self'",
    `script-src ${scriptSrc}`,
    // Same trade-off as the app-wide policy: Tailwind/Next inject inline <style>.
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
    "font-src 'self' https://fonts.gstatic.com",
    "img-src 'self' data:",
    "connect-src 'self'",
    "media-src 'self'",
    "worker-src 'self'",
    "manifest-src 'self'",
    "frame-src 'none'",
    "frame-ancestors 'none'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    ...(isDev ? [] : ['upgrade-insecure-requests']),
  ];
  return directives.join('; ');
}
