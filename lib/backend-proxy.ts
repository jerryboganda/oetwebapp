export const DEFAULT_PROXY_TARGET = 'http://127.0.0.1:5198';

/**
 * Allowed request origins for CSRF protection (RBAC-05).
 * Requests from unknown origins on state-changing methods are rejected.
 */
const CSRF_SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);
const CSRF_COOKIE = 'oet_csrf';
const REFRESH_COOKIE = 'oet_rt';
const CSRF_HEADER = 'x-csrf-token';

/**
 * Auth bootstrap endpoints are exempt from the proxy CSRF check.
 *
 * These endpoints are the entry points that establish (or replace) the auth
 * session and the `oet_csrf` cookie itself. A returning user with a stale
 * `oet_rt` refresh cookie but no/expired `oet_csrf` cookie would otherwise
 * be permanently locked out of sign-in until they manually clear cookies.
 *
 * The backend still performs its own anti-replay protection on these paths
 * (single-use refresh-token rotation, password+email validation, MFA, etc.),
 * so skipping the proxy CSRF here does not weaken the security model.
 */
const AUTH_BOOTSTRAP_PATH_PATTERN = /^\/?api\/backend\/v1\/auth(\/|$)/i;

function isAuthBootstrapRequest(request: Request): boolean {
  try {
    const { pathname } = new URL(request.url);
    return AUTH_BOOTSTRAP_PATH_PATTERN.test(pathname);
  } catch {
    return false;
  }
}

export function validateRequestOrigin(request: Request): boolean {
  const method = request.method.toUpperCase();
  if (CSRF_SAFE_METHODS.has(method)) return true;

  const origin = request.headers.get('origin');
  const referer = request.headers.get('referer');
  const source = origin || referer;

  if (!source) {
    return process.env.NODE_ENV !== 'production';
  }

  try {
    const url = new URL(source);
    if (url.origin === new URL(request.url).origin) return true;
    if (process.env.NODE_ENV !== 'production' && (url.hostname === 'localhost' || url.hostname === '127.0.0.1')) {
      return true;
    }
    for (const trustedOrigin of resolveTrustedOrigins()) {
      if (url.origin === trustedOrigin) return true;
    }
    // Allow Capacitor/Electron origins
    if (url.protocol === 'capacitor:' || url.protocol === 'file:') return true;
    return false;
  } catch {
    return false;
  }
}

export function validateProxyCsrf(request: Request): boolean {
  const method = request.method.toUpperCase();
  if (CSRF_SAFE_METHODS.has(method)) return true;

  // Auth bootstrap endpoints (sign-in, refresh, sign-out, register, MFA, SSO,
  // password reset, email verification) are exempt — they ARE the mechanism
  // that establishes the CSRF cookie. A user with a stale refresh cookie but
  // expired CSRF cookie must still be able to sign in.
  if (isAuthBootstrapRequest(request)) return true;

  const cookies = parseCookieHeader(request.headers.get('cookie'));
  if (!cookies.has(REFRESH_COOKIE)) {
    return true;
  }

  const cookieToken = cookies.get(CSRF_COOKIE);
  const headerToken = request.headers.get(CSRF_HEADER);
  return Boolean(cookieToken && headerToken && cookieToken === headerToken);
}

function resolveTrustedOrigins(): Set<string> {
  const values = [
    process.env.APP_URL,
    process.env.NEXT_PUBLIC_APP_URL,
    process.env.NEXT_PUBLIC_SITE_URL,
    process.env.CORS_ALLOWED_ORIGINS,
  ];
  const origins = new Set<string>();

  for (const value of values) {
    if (!value) continue;
    for (const raw of value.split(',')) {
      try {
        origins.add(new URL(raw.trim()).origin);
      } catch {
        // Ignore malformed environment entries.
      }
    }
  }

  return origins;
}

function parseCookieHeader(cookieHeader: string | null): Map<string, string> {
  const cookies = new Map<string, string>();
  if (!cookieHeader) return cookies;

  for (const part of cookieHeader.split(';')) {
    const [rawName, ...rawValueParts] = part.trim().split('=');
    if (!rawName) continue;
    cookies.set(rawName, rawValueParts.join('='));
  }

  return cookies;
}

const SENSITIVE_REQUEST_HEADERS = new Set([
  'host',
  'connection',
  'content-length',
  'expect',
  'forwarded',
  'x-forwarded-for',
  'x-forwarded-host',
  'x-forwarded-proto',
  'x-middleware-subrequest',
]);

const UNSAFE_RESPONSE_HEADERS = new Set([
  'connection',
  'content-encoding',
  'content-length',
  'keep-alive',
  'proxy-authenticate',
  'proxy-authorization',
  'te',
  'trailer',
  'transfer-encoding',
  'upgrade',
]);

export function validateProxyPathSegments(pathSegments: string[]): string[] {
  if (pathSegments.length === 0) {
    throw new Error('Invalid proxy path.');
  }

  if (pathSegments[0] !== 'v1') {
    throw new Error('Invalid proxy path.');
  }

  if (pathSegments.some((segment) => segment.length === 0 || segment === '.' || segment === '..' || segment.includes('\0') || segment.includes('/'))) {
    throw new Error('Invalid proxy path.');
  }

  return pathSegments;
}

export function resolveProxyTarget(pathSegments: string[], searchParams: URLSearchParams, baseUrl = DEFAULT_PROXY_TARGET): string {
  // Trim whitespace defensively: env values set via `cmd /k set FOO=val&& ...`
  // capture a trailing space before the `&&`, which would otherwise produce
  // an invalid URL like "http://host:port /v1/..." and a 500 from fetch().
  const normalizedBaseUrl = (baseUrl ?? DEFAULT_PROXY_TARGET).trim().replace(/\/$/, '');
  if (!normalizedBaseUrl) {
    throw new Error('Invalid proxy base URL.');
  }
  const normalizedPath = validateProxyPathSegments(pathSegments).join('/');
  const search = searchParams.toString();
  return `${normalizedBaseUrl}/${normalizedPath}${search ? `?${search}` : ''}`;
}

export function sanitizeProxyHeaders(headers: Headers): Headers {
  const sanitizedHeaders = new Headers(headers);
  const namesToRemove = new Set<string>();

  for (const [headerName] of sanitizedHeaders) {
    const normalizedName = headerName.toLowerCase();
    if (normalizedName.startsWith('x-debug-') || SENSITIVE_REQUEST_HEADERS.has(normalizedName)) {
      namesToRemove.add(headerName);
    }
  }

  for (const headerName of namesToRemove) {
    sanitizedHeaders.delete(headerName);
  }

  return sanitizedHeaders;
}

export function sanitizeProxyResponseHeaders(headers: Headers): Headers {
  const sanitizedHeaders = new Headers(headers);
  const namesToRemove = new Set<string>();

  for (const [headerName] of sanitizedHeaders) {
    if (UNSAFE_RESPONSE_HEADERS.has(headerName.toLowerCase())) {
      namesToRemove.add(headerName);
    }
  }

  for (const headerName of namesToRemove) {
    sanitizedHeaders.delete(headerName);
  }

  return sanitizedHeaders;
}
