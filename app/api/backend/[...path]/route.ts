export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

const DEFAULT_PROXY_TARGET = 'http://localhost:5198';

function resolveProxyTarget(pathSegments: string[], searchParams: URLSearchParams): string {
  const baseUrl = (process.env.API_PROXY_TARGET_URL ?? DEFAULT_PROXY_TARGET).replace(/\/$/, '');
  const path = pathSegments.join('/');
  const search = searchParams.toString();
  return `${baseUrl}/${path}${search ? `?${search}` : ''}`;
}

async function proxyRequest(request: Request, context: { params: Promise<{ path: string[] }> }) {
  const { path } = await context.params;
  const targetUrl = resolveProxyTarget(path, new URL(request.url).searchParams);
  const headers = new Headers(request.headers);

  headers.delete('host');
  headers.delete('connection');
  headers.delete('content-length');
  headers.delete('expect');

  const body = request.method === 'GET' || request.method === 'HEAD'
    ? undefined
    : await request.text();

  const upstreamResponse = await fetch(targetUrl, {
    method: request.method,
    headers,
    body: body && body.length > 0 ? body : undefined,
    redirect: 'manual',
  });

  const responseHeaders = new Headers(upstreamResponse.headers);
  responseHeaders.delete('content-length');
  responseHeaders.delete('connection');

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
