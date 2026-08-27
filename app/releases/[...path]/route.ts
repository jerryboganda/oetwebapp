import { Readable } from 'node:stream';
import { NextRequest, NextResponse } from 'next/server';
import { openReleaseAsset, resolveReleaseAsset } from '@/lib/native-releases';

export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

/**
 * Serves versioned installers from the VPS release catalog.
 * Production nginx can intercept /releases/ for sendfile/range; this route is
 * the same-origin fallback used by Next.js and local verification.
 */
export async function GET(
  _request: NextRequest,
  { params }: { params: Promise<{ path: string[] }> },
) {
  const { path: parts } = await params;
  const asset = resolveReleaseAsset(parts.join('/'));
  if (!asset) {
    return new NextResponse('Not found', {
      status: 404,
      headers: { 'Cache-Control': 'no-store' },
    });
  }

  return new NextResponse(Readable.toWeb(openReleaseAsset(asset)) as ReadableStream, {
    status: 200,
    headers: {
      'Content-Type': asset.contentType,
      'Content-Length': String(asset.size),
      'Content-Disposition': asset.contentDisposition,
      'Cache-Control': asset.cacheControl,
      'X-Content-Type-Options': 'nosniff',
      'Accept-Ranges': 'bytes',
    },
  });
}
