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
const AUTH_BOOTSTRAP_PATH_PATTERN = /^\/?api\/backend\/v1\/auth\/(?:register|sign-in|refresh|sign-out|external\/[^/]+\/exchange|email\/(?:send-verification-otp|verify-otp)|forgot-password|reset-password|mfa\/(?:challenge|recovery))\/?$/i;

function isAuthBootstrapRequest(request: Request): boolean {
  try {
    const { pathname } = new URL(request.url);
    return AUTH_BOOTSTRAP_PATH_PATTERN.test(pathname);
  } catch {
    return false;
  }
}

/**
 * SignalR hub endpoints are exempt from the proxy CSRF check.
 *
 * SignalR negotiate (POST) and polling requests do not include the
 * x-csrf-token header. The hub connections are protected at the backend
 * level by [Authorize] — a valid JWT bearer token is required before any
 * data is exchanged. Blocking the negotiate step here would prevent real-
 * time notifications and conversation streaming from connecting at all.
 *
 * `owner-agent` (Owner Agent Console) is additionally gated by the backend
 * `OwnerAgent` policy: owner account allow-list + system_admin + a valid
 * `X-Owner-Agent-Unlock` ticket re-checked on every forwarded batch.
 *
 * `speaking/live-rooms` (the tutor-room cue hub, mapped as
 * `/v1/speaking/live-rooms/hub`) has two path segments before `hub`, which the
 * single-segment hubs above never had. It is exempt on the same grounds: the hub
 * is `RequireAuthorization()` and its negotiate POST carries only the bearer token.
 * Without the exemption, a browser holding the `oet_rt` cookie (Path=/, so every
 * proxied request) failed validateProxyCsrf on negotiate, because SignalR sends no
 * x-csrf-token header, and the cue channel never connected.
 */
const SIGNALR_HUB_PATH_PATTERN = /^\/?api\/backend\/v1\/(?:notifications|conversations|ai-assistant|owner-agent|speaking\/live-rooms)\/hub(\/|$|\?)/i;

function isSignalRHubRequest(request: Request): boolean {
  try {
    const { pathname } = new URL(request.url);
    return SIGNALR_HUB_PATH_PATTERN.test(pathname);
  } catch {
    return false;
  }
}

/**
 * Payment webhook endpoints (Whop, Stripe, etc.) are server-to-server callbacks
 * and do not send browser cookies or CSRF tokens.
 */
const PAYMENT_WEBHOOK_PATH_PATTERN = /^\/?api\/backend\/v1\/payment\/webhooks(\/|$|\?)/i;

function isPaymentWebhookRequest(request: Request): boolean {
  try {
    const { pathname } = new URL(request.url);
    return PAYMENT_WEBHOOK_PATH_PATTERN.test(pathname);
  } catch {
    return false;
  }
}

export function validateRequestOrigin(request: Request): boolean {
  const method = request.method.toUpperCase();
  if (CSRF_SAFE_METHODS.has(method)) return true;
  if (isPaymentWebhookRequest(request)) return true;

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
    // Allow Capacitor/native shell origins
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
  // password reset, email verification) are exempt because they are the mechanism
  // that establishes the CSRF cookie. A user with a stale refresh cookie but
  // expired CSRF cookie must still be able to sign in.
  if (isAuthBootstrapRequest(request)) return true;

  // SignalR hub endpoints (notifications, conversations) are exempt — the
  // hub bearer JWT enforces auth at the backend; CSRF here would only break
  // real-time connectivity without adding meaningful protection.
  if (isSignalRHubRequest(request)) return true;

  // Payment gateway webhooks are server-to-server POST callbacks
  if (isPaymentWebhookRequest(request)) return true;

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
  // NOTE: 'x-forwarded-for' is deliberately NOT stripped. The API resolves it
  // through ForwardedHeadersMiddleware, which only honours hops originating
  // inside Proxy:KnownNetworks (the docker subnets) — see Program.cs. Stripping
  // it made Connection.RemoteIpAddress the web container's IP for every
  // learner, collapsing the AuthBruteforce limiter into ONE global 100/min
  // bucket shared by sign-in, register, verify-otp, MFA and password-reset.
  // Keep 'x-forwarded-host' stripped though: it carries the WEB host, which is
  // absent from the API's AllowedHosts, so HostFilteringMiddleware would 400
  // every proxied request.
  'x-forwarded-host',
  'x-forwarded-proto',
  'x-middleware-subrequest',
  // Client-supplied edge headers. No Cloudflare fronts this deployment, so
  // nothing legitimately produces these — yet the API treats them as
  // authoritative (SecurityEventLogger CF-Connecting-IP, the sign-in country
  // allow-list and IRegionDetector CF-IPCountry). Forwarding them let any
  // caller forge their own audit IP, country gate and billing region.
  'cf-connecting-ip',
  'cf-ipcountry',
  'cf-ray',
  'true-client-ip',
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

  if (!['v1', 'hubs', 'ws'].includes(pathSegments[0])) {
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

/**
 * Request bodies with a declared length above this are streamed to the API instead of
 * being buffered whole in the web container (1 GB per blue/green slot). That is what
 * speaking recordings, 8 MB admin upload chunks and whole ZIP imports used to cost in
 * memory, per request. Smaller bodies, and bodies of unknown length, are buffered
 * exactly as before, so every ordinary JSON call is unchanged.
 */
export const STREAM_BODY_THRESHOLD_BYTES = 1024 * 1024;

/**
 * The declared length of a request body that should be streamed, or null when it
 * should be buffered. Setting `BFF_STREAM_REQUEST_BODIES=0` in `.env.production`
 * (forwarded to the web slots by the `web-env` block in docker-compose.production.yml)
 * switches streaming off without a code change once the slot is recreated by the
 * next rollout (the value itself is read per request).
 */
export function streamedBodyLength(request: Request): number | null {
  if (process.env.BFF_STREAM_REQUEST_BODIES === '0') return null;
  if (!request.body) return null;

  const declared = request.headers.get('content-length');
  if (!declared || !/^\d+$/.test(declared)) return null;

  const length = Number(declared);
  return Number.isSafeInteger(length) && length > STREAM_BODY_THRESHOLD_BYTES ? length : null;
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
