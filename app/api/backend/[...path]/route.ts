export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

import {
  resolveProxyTarget,
  sanitizeProxyHeaders,
  sanitizeProxyResponseHeaders,
  streamedBodyLength,
  validateProxyCsrf,
  validateRequestOrigin,
} from '../../../../lib/backend-proxy';

function isAnalyticsEventPath(path: string[]) {
  return path[0] === 'v1'
    && path[1] === 'analytics'
    && path[2] === 'events'
    && (path.length === 3 || (path.length === 4 && path[3] === 'batch'));
}

async function proxyRequest(request: Request, context: { params: Promise<{ path: string[] }> }) {
  // CSRF origin validation for state-changing methods
  if (!validateRequestOrigin(request)) {
    return new Response('Forbidden: invalid request origin', { status: 403 });
  }
  if (!validateProxyCsrf(request)) {
    return new Response('Forbidden: missing or invalid CSRF token', { status: 403 });
  }

  const { path } = await context.params;

  let targetUrl: string;
  try {
    targetUrl = resolveProxyTarget(path, new URL(request.url).searchParams, process.env.API_PROXY_TARGET_URL);
  } catch {
    return new Response('Invalid proxy path', { status: 400 });
  }

  const headers = sanitizeProxyHeaders(request.headers);

  let body: BodyInit | undefined;
  let streamed = false;
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    const length = isAnalyticsEventPath(path) ? null : streamedBodyLength(request);
    if (length !== null) {
      // Pass the body through. The sanitizer strips content-length (it is hop-by-hop
      // bookkeeping), so restore the declared length: the API then sees the same framing
      // it saw when the body was buffered, rather than chunked transfer encoding.
      body = request.body ?? undefined;
      streamed = true;
      headers.set('content-length', String(length));
    } else {
      try {
        body = await request.arrayBuffer();
      } catch (error) {
        if (isAnalyticsEventPath(path)) {
          return new Response(null, { status: 204 });
        }

        throw error;
      }
    }
  }
  const hasBody = streamed || (body instanceof ArrayBuffer && body.byteLength > 0);

  if (isAnalyticsEventPath(path)) {
    if (!hasBody) {
      return new Response(null, { status: 204 });
    }
  }

  if (!hasBody) {
    headers.delete('content-type');
    headers.delete('content-encoding');
  }

  // `duplex: 'half'` is required by fetch for a streamed request body.
  const upstreamInit: RequestInit & { duplex?: 'half' } = {
    method: request.method,
    headers,
    body: hasBody ? body : undefined,
    redirect: 'manual',
    signal: request.signal,
    ...(streamed ? { duplex: 'half' as const } : {}),
  };
  const upstreamResponse = await fetch(targetUrl, upstreamInit);

  const responseHeaders = sanitizeProxyResponseHeaders(upstreamResponse.headers);

  return new Response(upstreamResponse.body, {
    status: upstreamResponse.status,
    headers: responseHeaders,
  });
}

export async function GET(request: Request, context: { params: Promise<{ path: string[] }> }) {
  return proxyRequest(request, context);
}

export async function POST(request: Request, context: { params: Promise<{ path: string[] }> }) {
  return proxyRequest(request, context);
}

export async function PUT(request: Request, context: { params: Promise<{ path: string[] }> }) {
  return proxyRequest(request, context);
}

export async function PATCH(request: Request, context: { params: Promise<{ path: string[] }> }) {
  return proxyRequest(request, context);
}

export async function DELETE(request: Request, context: { params: Promise<{ path: string[] }> }) {
  return proxyRequest(request, context);
}

export async function OPTIONS(request: Request, context: { params: Promise<{ path: string[] }> }) {
  return proxyRequest(request, context);
}
