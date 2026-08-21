import { NextRequest, NextResponse } from 'next/server';
import { readMobileRelease } from '@/lib/native-releases';

/**
 * Same-origin native release discovery for automatic mobile update checks.
 * Android uses the VPS-hosted APK as a fallback when Play in-app update is
 * unavailable. iOS is returned only when a production IPA catalog exists.
 */
export async function GET(request: NextRequest) {
  const platform = request.nextUrl.searchParams.get('platform');
  if (platform !== 'android' && platform !== 'ios') {
    return NextResponse.json({ error: 'platform must be android or ios' }, { status: 400 });
  }

  const release = readMobileRelease(platform);
  if (!release) {
    return NextResponse.json({ error: `No ${platform} release is published.` }, { status: 404 });
  }

  return NextResponse.json({
    platform: release.platform,
    version: release.version,
    downloadUrl: release.downloadUrl,
    digest: release.digest ?? (release.sha256 ? `sha256:${release.sha256}` : null),
    publishedAt: release.publishedAt ?? null,
  }, {
    headers: { 'Cache-Control': 'public, max-age=60, s-maxage=60, must-revalidate' },
  });
}
