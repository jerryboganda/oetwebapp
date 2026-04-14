export const DEFAULT_PROXY_TARGET = 'http://127.0.0.1:5198';

/**
 * Allowed request origins for CSRF protection (RBAC-05).
 * Requests from unknown origins on state-changing methods are rejected.
 */
const CSRF_SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

export function validateRequestOrigin(request: Request): boolean {
  const method = request.method.toUpperCase();
  if (CSRF_SAFE_METHODS.has(method)) return true;

  const origin = request.headers.get('origin');
  const referer = request.headers.get('referer');
  const source = origin || referer;

  // Allow requests with no origin header (non-browser clients, same-origin)
  if (!source) return true;

  try {
    const url = new URL(source);
    // Allow localhost development and configured app URLs
    if (url.hostname === 'localhost' || url.hostname === '127.0.0.1') return true;
    if (url.hostname === 'app.oetwithdrhesham.co.uk') return true;
    // Allow Capacitor/Electron origins
    if (url.protocol === 'capacitor:' || url.protocol === 'file:') return true;
    return false;
  } catch {
    return false;
  }
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
  const normalizedBaseUrl = baseUrl.replace(/\/$/, '');
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
