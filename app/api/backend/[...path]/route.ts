export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

import {
  resolveProxyTarget,
  sanitizeProxyHeaders,
  sanitizeProxyResponseHeaders,
  validateProxyCsrf,
  validateRequestOrigin,
} from '../../../../lib/backend-proxy';

function isAnalyticsEventPath(path: string[]) {
  return path.length === 3 && path[0] === 'v1' && path[1] === 'analytics' && path[2] === 'events';
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

  let body: string | undefined;
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    try {
      body = await request.text();
    } catch (error) {
      if (isAnalyticsEventPath(path)) {
        return new Response(null, { status: 204 });
      }

      throw error;
    }
  }
  const bodyText = body ?? '';
  const hasBody = bodyText.length > 0;

  if (isAnalyticsEventPath(path)) {
    if (!hasBody) {
      return new Response(null, { status: 204 });
    }
  }

  if (!hasBody) {
    headers.delete('content-type');
    headers.delete('content-encoding');
  }

  const upstreamResponse = await fetch(targetUrl, {
    method: request.method,
    headers,
    body: hasBody ? bodyText : undefined,
    redirect: 'manual',
  });

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
