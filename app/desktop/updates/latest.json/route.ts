import { NextResponse } from 'next/server';
import { readDesktopFeed } from '@/lib/native-releases';

/**
 * Stable Tauri updater feed served from the VPS release catalog.
 * Installed desktops must never need GitHub to check or download updates.
 */
export async function GET() {
  const manifest = readDesktopFeed();
  if (!manifest) {
    return NextResponse.json({ error: 'Desktop updater feed is temporarily unavailable.' }, {
      status: 503,
      headers: { 'Cache-Control': 'no-store' },
    });
  }

  return NextResponse.json(manifest, {
    headers: { 'Cache-Control': 'public, max-age=30, s-maxage=30, must-revalidate' },
  });
}
