import { NextResponse } from 'next/server';
import { isMacDownloadDisabled, readDesktopFeed } from '@/lib/native-releases';

/**
 * Stable Tauri updater feed served from the VPS release catalog.
 * Installed desktops must never need GitHub to check or download updates.
 * While the mac kill-switch is armed, downloads.mac is stripped so the raw
 * DMG URL is not advertised to candidates (17 Sep 2026 handover); updater
 * platform entries are untouched, and Windows is unaffected.
 */
export async function GET() {
  const manifest = readDesktopFeed();
  if (!manifest) {
    return NextResponse.json({ error: 'Desktop updater feed is temporarily unavailable.' }, {
      status: 503,
      headers: { 'Cache-Control': 'no-store' },
    });
  }

  const body = isMacDownloadDisabled() && manifest.downloads?.mac
    ? {
        ...manifest,
        downloads: Object.fromEntries(
          Object.entries(manifest.downloads).filter(([platform]) => platform !== 'mac'),
        ),
      }
    : manifest;

  return NextResponse.json(body, {
    headers: { 'Cache-Control': 'public, max-age=30, s-maxage=30, must-revalidate' },
  });
}
