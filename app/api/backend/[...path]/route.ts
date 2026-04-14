export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

import { resolveProxyTarget, sanitizeProxyHeaders, sanitizeProxyResponseHeaders, validateRequestOrigin } from '../../../../lib/backend-proxy';

async function proxyRequest(request: Request, context: { params: Promise<{ path: string[] }> }) {
  // CSRF origin validation for state-changing methods
  if (!validateRequestOrigin(request)) {
    return new Response('Forbidden: invalid request origin', { status: 403 });
  }

  const { path } = await context.params;

  let targetUrl: string;
  try {
    targetUrl = resolveProxyTarget(path, new URL(request.url).searchParams, process.env.API_PROXY_TARGET_URL);
  } catch {
    return new Response('Invalid proxy path', { status: 400 });
  }

  const headers = sanitizeProxyHeaders(request.headers);

  const body = request.method === 'GET' || request.method === 'HEAD'
    ? undefined
    : await request.text();

  const upstreamResponse = await fetch(targetUrl, {
    method: request.method,
    headers,
    body: body && body.length > 0 ? body : undefined,
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
