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
 * - We deliberately do NOT use 'strict-dynamic'. Turbopack (unlike webpack's
 *   __webpack_nonce__) does not stamp the per-request nonce onto its chunk-loader
 *   scripts, so under 'strict-dynamic' Firefox/Chromium reject our own same-origin
 *   /_next/static chunks ("script-src-elem … violates 'strict-dynamic'"), while
 *   WebKit silently passes (it doesn't enforce 'strict-dynamic'). Instead we allowlist
 *   'self' for our own bundles and require 'nonce-…' for inline scripts — enforced
 *   uniformly across browsers. Dropping 'strict-dynamic' also re-activates the host
 *   allowlists below (e.g. the Zoom origins), which it had otherwise suppressed.
 * - Styles still need 'unsafe-inline' because Tailwind's CSS-in-JS and Next.js dev-time
 *   style injection emit inline <style>. Nonce-only styles break in practice. We accept
 *   this because style-based XSS is rare and we kill script-based XSS which is the real
 *   risk for this app (stored learner HTML renders via dangerouslySetInnerHTML in the
 *   reading module — that path is being additionally sanitized server-side separately).
 * - 'unsafe-eval' only in dev for React fast-refresh.
 */
function buildCsp(nonce: string, apiOrigins: string[], apiWsOrigins: string[], mediaCdnOrigins: string[], isDev: boolean): string {
  const zoomHttpOrigins = ['https://zoom.us', 'https://*.zoom.us', 'https://zoom.com', 'https://*.zoom.com', 'https://source.zoom.us'];
  const zoomWsOrigins = ['wss://zoom.us', 'wss://*.zoom.us', 'wss://zoom.com', 'wss://*.zoom.com'];
  // PayPal embedded checkout (JS SDK + Smart Buttons + Advanced Card Fields). The SDK
  // loads its script from www.paypal.com / paypalobjects.com, renders the buttons and
  // hosted card fields inside *.paypal.com iframes, and calls the *.paypal.com REST /
  // GraphQL endpoints. Without these, the SDK is blocked by CSP and silently fails to
  // render ("Loading secure payment…" then a blank box). Covers live + sandbox (both
  // are *.paypal.com) and Venmo, which the SDK offers alongside PayPal.
  const paypalHttpOrigins = ['https://*.paypal.com', 'https://*.paypalobjects.com', 'https://*.venmo.com'];
  const scriptSrc = [
    "'self'",
    `'nonce-${nonce}'`,
    ...zoomHttpOrigins,
    ...paypalHttpOrigins,
    ...(isDev ? ["'unsafe-eval'"] : []),
  ].join(' ');

  const connectSrc = [
    "'self'",
    'blob:',
    ...apiOrigins,
    ...apiWsOrigins,
    ...zoomHttpOrigins,
    ...zoomWsOrigins,
    ...paypalHttpOrigins,
    // Bunny Stream. Two distinct hosts:
    //  - playback CDN (vz-*.b-cdn.net): hls.js fetches HLS playlists/segments via
    //    XHR inside the native app WebViews (which load this same remote origin).
    //  - upload API (video.bunnycdn.com): the admin browser uploads video files
    //    straight to Bunny via resumable TUS (POST/PATCH/HEAD). Without this host
    //    in connect-src the browser blocks the upload ("response code: n/a").
    ...mediaCdnOrigins,
    'https://video.bunnycdn.com',
    'https://*.googleapis.com',
  ].join(' ');

  const directives = [
    "default-src 'self'",
    `script-src ${scriptSrc}`,
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
    "font-src 'self' https://fonts.gstatic.com",
    `img-src 'self' data: blob: ${zoomHttpOrigins.join(' ')} ${paypalHttpOrigins.join(' ')} ${mediaCdnOrigins.join(' ')}`,
    `connect-src ${connectSrc}`,
    `media-src 'self' blob: ${apiOrigins.join(' ')} ${zoomHttpOrigins.join(' ')} ${mediaCdnOrigins.join(' ')}`,
    `worker-src 'self' blob: ${zoomHttpOrigins.join(' ')}`,
    `frame-src 'self' ${zoomHttpOrigins.join(' ')} ${paypalHttpOrigins.join(' ')}`,
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

/**
 * Media CDN origins (Bunny Stream) for media-src/connect-src/img-src.
 * The wildcard default covers every Bunny pull-zone host (vz-*.b-cdn.net) so
 * the admin-configured hostname works without a rebuild; a custom CNAME needs
 * MEDIA_CDN_ORIGINS set (comma-separated origins) + redeploy — Edge middleware
 * env is baked at build time.
 */
function resolveMediaCdnOrigins(): string[] {
  const raw = process.env.MEDIA_CDN_ORIGINS;
  if (!raw) return ['https://*.b-cdn.net'];
  return raw
    .split(',')
    .map((value) => value.trim())
    .filter(Boolean);
}

const MEDIA_CDN_ORIGINS = resolveMediaCdnOrigins();

// Public routes that don't require authentication
// Convention: all pages under the (auth) route group are public
const PUBLIC_PATHS = new Set([
  '/sign-in',
  '/register',
  '/register/success',
  '/terms',
  '/privacy',
  '/support',
  '/forgot-password',
  '/forgot-password/verify',
  '/reset-password',
  '/reset-password/success',
  '/verify-email',
  '/mfa/challenge',
  '/mfa/setup',
  '/mfa/recovery',
  '/auth/callback',
  '/get-app',
  '/.well-known/apple-app-site-association',
  '/.well-known/assetlinks.json',
]);

function isPublicPath(pathname: string): boolean {
  if (PUBLIC_PATHS.has(pathname)) return true;
  if (pathname.startsWith('/auth/callback/')) return true;
  return false;
}

function isSponsorPortalEnabled(): boolean {
  return process.env.NEXT_PUBLIC_SPONSOR_PORTAL_ENABLED === 'true';
}

/** Mutation methods that require CSRF validation */
const CSRF_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

/**
 * Capture affiliate / agent attribution from URL params and persist as a cookie.
 * Phase 8 cookie-attribution: `?ref=CODE` or `?agent=CODE` sets `oet_affiliate`
 * for `cookieDays` (default 30). The cookie is read at signup and POSTed to
 * /v1/affiliates/track to record the AffiliateAttribution row.
 */
const AFFILIATE_COOKIE = 'oet_affiliate';
const AFFILIATE_COOKIE_DAYS = 30;

function applyAffiliateCookie(request: NextRequest, response: NextResponse): NextResponse {
  const code = request.nextUrl.searchParams.get('ref') ?? request.nextUrl.searchParams.get('agent');
  if (!code || !/^[A-Za-z0-9_-]{2,64}$/.test(code)) return response;
  if (request.cookies.get(AFFILIATE_COOKIE)) return response; // first-click wins
  response.cookies.set(AFFILIATE_COOKIE, code, {
    maxAge: AFFILIATE_COOKIE_DAYS * 24 * 60 * 60,
    path: '/',
    sameSite: 'lax',
    secure: process.env.NODE_ENV === 'production',
  });
  return response;
}

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;
  const isDev = process.env.NODE_ENV !== 'production';

  // ── Generate per-request CSP nonce ──
  // Next.js reads the x-nonce request header and auto-injects the nonce into its
  // framework <script> tags. 'strict-dynamic' lets those trusted scripts then
  // load further chunks without every chunk needing its own allowlist entry.
  const nonce = generateNonce();
  const csp = buildCsp(nonce, API_ORIGINS.http, API_ORIGINS.ws, MEDIA_CDN_ORIGINS, isDev);

  const requestHeaders = new Headers(request.headers);
  requestHeaders.set(NONCE_HEADER, nonce);
  // Next.js specifically looks for the CSP value on the request headers so it
  // can extract the nonce and stamp it onto framework scripts during SSR.
  requestHeaders.set('content-security-policy', csp);

  // Build the response early so we can attach the CSP header regardless of the
  // auth branch taken below.
  function withCsp(res: NextResponse): NextResponse {
    res.headers.set('Content-Security-Policy', csp);
    return applyAffiliateCookie(request, res);
  }

  if (pathname.startsWith('/sponsor') && !isSponsorPortalEnabled()) {
    return withCsp(NextResponse.redirect(new URL('/support', request.url)));
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
    // Skip:
    //  - /api routes (proxied separately)
    //  - Next.js internal static + image optimizer paths
    //  - favicon, robots, manifest, common PWA + static asset extensions
    //  - JSON files served from /public (e.g. manifest.json, well-known)
    // Without skipping manifest.json the middleware redirects it to /sign-in
    // when the user is anonymous, browser then parses the HTML as JSON and
    // logs a "Manifest: Syntax error" in the console on every page load.
    '/((?!api|_next/static|_next/image|favicon\\.ico|manifest\\.json|robots\\.txt|sitemap\\.xml|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico|woff|woff2|ttf|eot|css|js|map|json)$).*)',
  ],
};
