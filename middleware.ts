import { NextRequest, NextResponse } from 'next/server';

// Cookie name must match AUTH_INDICATOR_COOKIE in lib/auth-storage.ts
// Duplicated here because Edge runtime cannot import browser-only modules
const AUTH_COOKIE = 'oet_auth';
const CSRF_COOKIE = 'oet_csrf';
const CSRF_HEADER = 'x-csrf-token';
const NONCE_HEADER = 'x-nonce';

/** Generate a hex CSRF token using Web Crypto (Edge Runtime compatible). */
function generateCsrfToken(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

/** Per-request CSP nonce. Base64 keeps it small and CSP-header-safe. */
function generateNonce(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  // btoa handles binary-to-base64 in Edge runtime.
  let binary = '';
  for (let i = 0; i < bytes.length; i += 1) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

/**
 * Build the per-response Content-Security-Policy.
 *
 * Notes:
 * - 'strict-dynamic' is what makes nonce-based CSP work with Next.js chunk loading:
 *   trusted scripts (those carrying the nonce) can load further scripts without each
 *   one being individually allowlisted. When 'strict-dynamic' is present, host-source
 *   allowlists are ignored by modern browsers — that's by design and it's the point.
 * - Styles still need 'unsafe-inline' because Tailwind's CSS-in-JS and Next.js dev-time
 *   style injection emit inline <style>. Nonce-only styles break in practice. We accept
 *   this because style-based XSS is rare and we kill script-based XSS which is the real
 *   risk for this app (stored learner HTML renders via dangerouslySetInnerHTML in the
 *   reading module — that path is being additionally sanitized server-side separately).
 * - 'unsafe-eval' only in dev for React fast-refresh.
 */
function buildCsp(nonce: string, apiOrigins: string[], apiWsOrigins: string[], isDev: boolean): string {
  const scriptSrc = [
    "'self'",
    `'nonce-${nonce}'`,
    "'strict-dynamic'",
    ...(isDev ? ["'unsafe-eval'"] : []),
  ].join(' ');

  const connectSrc = [
    "'self'",
    'blob:',
    ...apiOrigins,
    ...apiWsOrigins,
    'https://*.googleapis.com',
  ].join(' ');

  const directives = [
    "default-src 'self'",
    `script-src ${scriptSrc}`,
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
    "font-src 'self' https://fonts.gstatic.com",
    "img-src 'self' data: blob:",
    `connect-src ${connectSrc}`,
    `media-src 'self' blob: ${apiOrigins.join(' ')}`,
    "worker-src 'self' blob:",
    "frame-src 'self'",
    "frame-ancestors 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    // upgrade-insecure-requests breaks plain-HTTP local dev (assets get rewritten to https://);
    // only emit it in production where the app is served over HTTPS.
    ...(isDev ? [] : ['upgrade-insecure-requests']),
  ];
  return directives.join('; ');
}

/**
 * API origins whitelisted for connect-src/media-src. Computed at module load; the
 * middleware runs in Edge runtime so process.env is read at build time.
 */
function resolveApiOrigins(): { http: string[]; ws: string[] } {
  const publicBase = process.env.NEXT_PUBLIC_API_BASE_URL;
  const proxyBase = process.env.API_PROXY_TARGET_URL;
  const fallback = 'http://127.0.0.1:5198';
  const raw = publicBase || proxyBase || fallback;
  let origin: string;
  try {
    origin = new URL(raw).origin;
  } catch {
    origin = new URL(fallback).origin;
  }
  // Expand loopback to both 127.0.0.1 and localhost so dev works on either.
  let http: string[] = [origin];
  try {
    const u = new URL(origin);
    if (u.hostname === '127.0.0.1' || u.hostname === 'localhost') {
      const port = u.port ? `:${u.port}` : '';
      http = [`${u.protocol}//127.0.0.1${port}`, `${u.protocol}//localhost${port}`];
    }
  } catch {
    // keep http as-is
  }
  const ws = http.map((o) =>
    o.startsWith('https://')
      ? `wss://${o.slice('https://'.length)}`
      : o.startsWith('http://')
        ? `ws://${o.slice('http://'.length)}`
        : o,
  );
  return { http, ws };
}

const API_ORIGINS = resolveApiOrigins();

// Public routes that don't require authentication
// Convention: all pages under the (auth) route group are public
const PUBLIC_PATHS = new Set([
  '/sign-in',
  '/register',
  '/register/success',
  '/terms',
  '/privacy',
  '/forgot-password',
  '/forgot-password/verify',
  '/reset-password',
  '/reset-password/success',
  '/verify-email',
  '/mfa/challenge',
  '/mfa/setup',
  '/mfa/recovery',
  '/auth/callback',
]);

function isPublicPath(pathname: string): boolean {
  if (PUBLIC_PATHS.has(pathname)) return true;
  if (pathname.startsWith('/auth/callback/')) return true;
  return false;
}

/** Mutation methods that require CSRF validation */
const CSRF_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;
  const isDev = process.env.NODE_ENV !== 'production';

  // ── Generate per-request CSP nonce ──
  // Next.js reads the x-nonce request header and auto-injects the nonce into its
  // framework <script> tags. 'strict-dynamic' lets those trusted scripts then
  // load further chunks without every chunk needing its own allowlist entry.
  const nonce = generateNonce();
  const csp = buildCsp(nonce, API_ORIGINS.http, API_ORIGINS.ws, isDev);

  const requestHeaders = new Headers(request.headers);
  requestHeaders.set(NONCE_HEADER, nonce);
  // Next.js specifically looks for the CSP value on the request headers so it
  // can extract the nonce and stamp it onto framework scripts during SSR.
  requestHeaders.set('content-security-policy', csp);

  // Build the response early so we can attach the CSP header regardless of the
  // auth branch taken below.
  function withCsp(res: NextResponse): NextResponse {
    res.headers.set('Content-Security-Policy', csp);
    return res;
  }

  if (isPublicPath(pathname)) {
    return withCsp(NextResponse.next({ request: { headers: requestHeaders } }));
  }

  const authCookie = request.cookies.get(AUTH_COOKIE);

  if (!authCookie) {
    const signInUrl = new URL('/sign-in', request.url);
    signInUrl.searchParams.set('next', pathname);
    return withCsp(NextResponse.redirect(signInUrl));
  }

  // ── CSRF double-submit cookie pattern ──
  // For API proxy mutation requests, verify the CSRF header matches the cookie.
  if (pathname.startsWith('/api/backend/') && CSRF_METHODS.has(request.method)) {
    const csrfCookie = request.cookies.get(CSRF_COOKIE)?.value;
    const csrfHeader = request.headers.get(CSRF_HEADER);
    if (!csrfCookie || !csrfHeader || csrfCookie !== csrfHeader) {
      return withCsp(
        NextResponse.json(
          { error: 'CSRF_VALIDATION_FAILED', message: 'Missing or invalid CSRF token' },
          { status: 403 },
        ),
      );
    }
  }

  // Ensure every authenticated response has a CSRF cookie
  const response = NextResponse.next({ request: { headers: requestHeaders } });
  const existingCsrf = request.cookies.get(CSRF_COOKIE);
  if (!existingCsrf) {
    const token = generateCsrfToken();
    response.cookies.set(CSRF_COOKIE, token, {
      httpOnly: false, // Must be readable by JS to include in headers
      secure: request.nextUrl.protocol === 'https:',
      sameSite: 'strict',
      path: '/',
      maxAge: 60 * 60 * 24, // 24 hours
    });
  }

  return withCsp(response);
}

export const config = {
  matcher: [
    '/((?!api|_next/static|_next/image|favicon\\.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico|woff|woff2|ttf|eot|css|js|map)$).*)',
  ],
};
